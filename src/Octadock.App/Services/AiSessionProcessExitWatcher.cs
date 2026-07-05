using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Services;

/// <summary>
/// Marks PID-backed AI sessions Completed the INSTANT their process exits,
/// instead of waiting for the next discovery sweep. For every active session
/// with a PID it holds a process handle and awaits exit; on exit the session is
/// re-fetched and, if still active, completed with a timeline event. The set of
/// tracked sessions self-heals from the change bus, so sessions created by any
/// path (discovery, run/watch, hooks) are covered, including after a restart.
/// Scan-based reconciliation stays as the safety net for PID reuse and
/// processes we cannot open.
/// </summary>
public sealed partial class AiSessionProcessExitWatcher : IDisposable
{
    private static readonly TimeSpan SyncDebounce = TimeSpan.FromMilliseconds(300);

    private static readonly AiSessionStatus[] ActiveStatuses =
    [
        AiSessionStatus.Queued,
        AiSessionStatus.Running,
        AiSessionStatus.WaitingForInput,
        AiSessionStatus.Paused,
    ];

    private readonly IAiSessionRepository _sessions;
    private readonly IAiSessionChangeBus _bus;
    private readonly IClock _clock;
    private readonly ILogger<AiSessionProcessExitWatcher> _logger;
    private readonly ConcurrentDictionary<Guid, WatchEntry> _tracked = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _debounceCts;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates the watcher.</summary>
    public AiSessionProcessExitWatcher(
        IAiSessionRepository sessions,
        IAiSessionChangeBus bus,
        IClock clock,
        ILogger<AiSessionProcessExitWatcher> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Number of sessions currently being awaited (test/diagnostic hook).</summary>
    internal int TrackedCount => _tracked.Count;

    /// <summary>Starts watching and subscribes to session changes.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _lifetime = new CancellationTokenSource();
        _bus.SessionsChanged += OnSessionsChanged;
        _ = SyncAsync(_lifetime.Token);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bus.SessionsChanged -= OnSessionsChanged;
        _lifetime?.Cancel();
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();

        foreach (WatchEntry entry in _tracked.Values)
        {
            entry.Dispose();
        }

        _tracked.Clear();
        _lifetime?.Dispose();
        _syncGate.Dispose();
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        CancellationTokenSource? lifetime = _lifetime;
        if (lifetime is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        // Coalesce bursts of writes into one sync pass.
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _debounceCts = debounce;
        _ = DebouncedSyncAsync(debounce.Token);
    }

    private async Task DebouncedSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SyncDebounce, cancellationToken).ConfigureAwait(false);
            await SyncAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded or shutting down.
        }
    }

    /// <summary>Aligns the tracked set with the store's active PID-backed sessions.</summary>
    internal async Task SyncAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<AiSessionRecord> active = await _sessions.ListAsync(
                new AiSessionFilter { Statuses = ActiveStatuses, Limit = 1000 },
                cancellationToken).ConfigureAwait(false);

            var activeIds = new HashSet<Guid>();
            foreach (AiSessionRecord session in active)
            {
                if (session.Pid is not int pid)
                {
                    continue;
                }

                activeIds.Add(session.Id);
                if (_tracked.ContainsKey(session.Id))
                {
                    continue;
                }

                TrackSession(session.Id, pid);
            }

            // Sessions that are no longer active do not need an exit await.
            foreach (Guid id in _tracked.Keys)
            {
                if (!activeIds.Contains(id) && _tracked.TryRemove(id, out WatchEntry? stale))
                {
                    stale.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSyncFailed(ex);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void TrackSession(Guid sessionId, int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                process.Dispose();
                _ = CompleteIfStillActiveAsync(sessionId, exitCode: null);
                return;
            }

            // Required for WaitForExitAsync on processes we did not start.
            process.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Already gone (or unopenable): complete now; the scan reconciles
            // any PID-reuse edge cases.
            _ = CompleteIfStillActiveAsync(sessionId, exitCode: null);
            return;
        }

        var entry = new WatchEntry(process);
        if (!_tracked.TryAdd(sessionId, entry))
        {
            entry.Dispose();
            return;
        }

        _ = AwaitExitAsync(sessionId, entry);
    }

    private async Task AwaitExitAsync(Guid sessionId, WatchEntry entry)
    {
        CancellationToken lifetime = _lifetime?.Token ?? CancellationToken.None;
        int? exitCode = null;
        try
        {
            await entry.Process.WaitForExitAsync(lifetime).ConfigureAwait(false);
            try
            {
                exitCode = entry.Process.ExitCode;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Exit code is unavailable for processes we did not start.
            }
        }
        catch (OperationCanceledException)
        {
            return; // Shutdown.
        }
        catch (Exception ex)
        {
            LogWaitFailed(ex, sessionId);
            return; // The scan sweep remains the fallback for this session.
        }
        finally
        {
            if (_tracked.TryRemove(sessionId, out WatchEntry? removed))
            {
                removed.Dispose();
            }
        }

        await CompleteIfStillActiveAsync(sessionId, exitCode).ConfigureAwait(false);
    }

    /// <summary>Completes the session unless another writer already finished it.</summary>
    internal async Task CompleteIfStillActiveAsync(Guid sessionId, int? exitCode)
    {
        try
        {
            AiSessionRecord? current = await _sessions.GetAsync(sessionId).ConfigureAwait(false);
            if (current is null || !ActiveStatuses.Contains(current.Status))
            {
                return;
            }

            DateTimeOffset now = _clock.UtcNow;
            AiSessionStatus status = exitCode is > 0 ? AiSessionStatus.Failed : AiSessionStatus.Completed;
            await _sessions.UpdateAsync(current with
            {
                Status = status,
                EndedAt = now,
                LastEventAt = now,
                ExitCode = exitCode ?? current.ExitCode,
            }).ConfigureAwait(false);
            await _sessions.AddEventAsync(new AiSessionEventRecord
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                EventType = AiSessionEventType.Completed,
                CreatedAt = now,
                Message = exitCode is int code
                    ? $"Process exited with code {code} (detected instantly by the exit watcher)."
                    : "The tracked process exited (detected instantly by the exit watcher).",
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCompleteFailed(ex, sessionId);
        }
    }

    private sealed class WatchEntry : IDisposable
    {
        public WatchEntry(Process process) => Process = process;

        public Process Process { get; }

        public void Dispose()
        {
            try
            {
                Process.Dispose();
            }
            catch (Exception)
            {
                // Disposing a dead process handle is best-effort.
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Syncing the process-exit watch set failed.")]
    private partial void LogSyncFailed(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Waiting for exit of session {SessionId} failed; the scan sweep will reconcile it.")]
    private partial void LogWaitFailed(Exception exception, Guid sessionId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Completing exited session {SessionId} failed.")]
    private partial void LogCompleteFailed(Exception exception, Guid sessionId);
}

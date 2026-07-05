using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.App.Services.AiSessionDiscovery;
using Octadock.Core.Common;
using Octadock.Core.Persistence;

namespace Octadock.App.Services;

/// <summary>
/// Discovers already-running local AI coding sessions and mirrors them into
/// the durable Active AI Sessions table. Orchestrates a layered pipeline:
/// evidence collectors (process snapshots, Codex state, Claude Code state)
/// feed an identity resolver, and a coordinator syncs the resolved
/// observations to <see cref="IAiSessionRepository"/>. Updates are event-driven
/// (WMI process start/stop, provider state file changes) with periodic
/// reconciliation as the safety net.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiSessionDiscoveryService : IDisposable
{
    /// <summary>Debounce for event-triggered rescans so bursts coalesce into one scan.</summary>
    private static readonly TimeSpan EventScanDebounce = TimeSpan.FromMilliseconds(1500);

    /// <summary>Reconciliation interval when the event watchers are healthy.</summary>
    private static readonly TimeSpan EventDrivenScanInterval = TimeSpan.FromSeconds(30);

    /// <summary>Reconciliation interval when only polling is available.</summary>
    private static readonly TimeSpan PollingScanInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// UI-triggered scans within this window reuse the previous result. Kept
    /// shorter than the overlay's 2s refresh tick so overlay-driven scans do
    /// not alias against the cache and a manual Refresh stays near-fresh.
    /// </summary>
    private static readonly TimeSpan ScanResultReuseWindow = TimeSpan.FromSeconds(1);

    /// <summary>Process names that can plausibly host an AI session (prefilter for start events).</summary>
    private static readonly HashSet<string> InterestingProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "bun", "claude", "codex", "cursor-agent", "copilot", "gemini",
        "python", "python3", "aider", "goose", "opencode", "openhands", "jules", "devin",
    };

    private readonly IClock _clock;
    private readonly ILogger<AiSessionDiscoveryService> _logger;
    private readonly IReadOnlyList<IAiSessionEvidenceCollector> _collectors;
    private readonly AiSessionIdentityResolver _resolver;
    private readonly AiSessionDiscoveryCoordinator _coordinator;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateGate = new();

    private CancellationTokenSource? _lifecycleCts;
    private Task? _timerTask;
    private ProcessLifetimeWatcher? _processWatcher;
    private readonly List<ProviderStateWatcher> _stateWatchers = [];
    private HashSet<int> _trackedPids = [];
    private AiSessionDiscoveryResult? _lastResult;
    private DateTimeOffset? _lastScanCompletedAt;
    private int _rescanQueued;
    private bool _disposed;

    /// <summary>Creates the discovery service with the default collector pipeline.</summary>
    public AiSessionDiscoveryService(
        IAiSessionRepository sessions,
        IClock clock,
        ILogger<AiSessionDiscoveryService> logger)
        : this(sessions, clock, logger, collectors: null)
    {
    }

    /// <summary>Test seam: inject collectors and skip the OS watchers.</summary>
    internal AiSessionDiscoveryService(
        IAiSessionRepository sessions,
        IClock clock,
        ILogger<AiSessionDiscoveryService> logger,
        IReadOnlyList<IAiSessionEvidenceCollector>? collectors)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _collectors = collectors ??
        [
            new ProcessSnapshotEvidenceCollector(),
            new CodexStateEvidenceCollector(),
            new ClaudeCodeStateEvidenceCollector(),
        ];
        _resolver = new AiSessionIdentityResolver();
        _coordinator = new AiSessionDiscoveryCoordinator(sessions);
    }

    /// <summary>Starts periodic reconciliation and the event-driven watchers.</summary>
    public void Start()
    {
        CancellationToken token;
        lock (_stateGate)
        {
            if (_disposed || _lifecycleCts is not null)
            {
                return;
            }

            _lifecycleCts = new CancellationTokenSource();
            token = _lifecycleCts.Token;
        }

        (ProcessLifetimeWatcher? processWatcher, List<ProviderStateWatcher> stateWatchers) = CreateWatchers();
        bool watchersHealthy = processWatcher is not null || stateWatchers.Count > 0;
        LogDiscoveryStarted(watchersHealthy);

        var rollback = false;
        lock (_stateGate)
        {
            if (_lifecycleCts is null || _lifecycleCts.Token != token)
            {
                // Stop/Dispose won the race while watchers were starting: they
                // were never published, so roll them back below.
                rollback = true;
            }
            else
            {
                _processWatcher = processWatcher;
                _stateWatchers.AddRange(stateWatchers);
                _timerTask = RunLoopAsync(
                    watchersHealthy ? EventDrivenScanInterval : PollingScanInterval,
                    token);
            }
        }

        if (rollback)
        {
            DisposeWatchers(processWatcher, stateWatchers);
        }
    }

    /// <summary>Stops periodic reconciliation and disposes the watchers.</summary>
    public void Stop()
    {
        ProcessLifetimeWatcher? processWatcher;
        List<ProviderStateWatcher> stateWatchers;
        lock (_stateGate)
        {
            if (_lifecycleCts is null)
            {
                return;
            }

            _lifecycleCts.Cancel();
            _lifecycleCts.Dispose();
            _lifecycleCts = null;
            _timerTask = null;

            processWatcher = _processWatcher;
            _processWatcher = null;
            stateWatchers = [.. _stateWatchers];
            _stateWatchers.Clear();
        }

        // Dispose OUTSIDE _stateGate: ManagementEventWatcher.Stop blocks until
        // in-flight EventArrived callbacks return, and those callbacks acquire
        // _stateGate (OnProcessChanged) — disposing under the lock deadlocks.
        DisposeWatchers(processWatcher, stateWatchers);
    }

    private static void DisposeWatchers(
        ProcessLifetimeWatcher? processWatcher,
        List<ProviderStateWatcher> stateWatchers)
    {
        processWatcher?.Dispose();
        foreach (ProviderStateWatcher watcher in stateWatchers)
        {
            watcher.Dispose();
        }
    }

    /// <summary>
    /// Runs one discovery pass (collect, resolve, sync) and returns counters.
    /// UI callers polling in a tight loop are served the previous result while
    /// it is still fresh; event-driven and periodic scans always run fully.
    /// </summary>
    public Task<AiSessionDiscoveryResult> ScanOnceAsync(CancellationToken cancellationToken = default)
        => ScanCoreAsync(force: false, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        // _scanGate is intentionally not disposed: Stop() does not await
        // in-flight scans, and SemaphoreSlim only needs disposal when its
        // AvailableWaitHandle was materialized (it never is here). Leaving it
        // alive lets late scans finish their finally-Release safely.
    }

    private async Task<AiSessionDiscoveryResult> ScanCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset observedAt = _clock.UtcNow;
            if (!force &&
                _lastResult is { } cached &&
                _lastScanCompletedAt is { } completedAt &&
                observedAt - completedAt < ScanResultReuseWindow)
            {
                return cached;
            }

            AiSessionEvidenceBatch[] batches = await Task.WhenAll(
                _collectors.Select(c => CollectSafeAsync(c, observedAt, cancellationToken)))
                .ConfigureAwait(false);
            AiSessionResolution resolution = _resolver.Resolve(batches, observedAt);
            foreach (AiSessionResolutionDrop drop in resolution.Dropped)
            {
                LogEvidenceDropped(drop.Evidence.Detector, drop.Evidence.Provider.ToString(), drop.Reason);
            }

            AiSessionDiscoverySyncResult sync = await _coordinator
                .SyncAsync(resolution, observedAt, cancellationToken)
                .ConfigureAwait(false);

            var result = new AiSessionDiscoveryResult(sync.DetectedCount, sync.AddedCount, sync.CompletedCount);
            lock (_stateGate)
            {
                _trackedPids = sync.ActivePids.ToHashSet();
            }

            _lastResult = result;
            _lastScanCompletedAt = _clock.UtcNow;
            return result;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private static async Task<AiSessionEvidenceBatch> CollectSafeAsync(
        IAiSessionEvidenceCollector collector,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await collector.CollectAsync(observedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Collectors report failures through their batch; this is the belt
            // and braces for ones that throw anyway.
            return AiSessionEvidenceBatch.Failed(collector.Source, ex.Message);
        }
    }

    private (ProcessLifetimeWatcher? ProcessWatcher, List<ProviderStateWatcher> StateWatchers) CreateWatchers()
    {
        ProcessLifetimeWatcher? processWatcher = null;
        try
        {
            var candidate = new ProcessLifetimeWatcher();
            if (candidate.TryStart(OnProcessChanged))
            {
                processWatcher = candidate;
            }
            else
            {
                candidate.Dispose();
                LogWatcherUnavailable("process lifetime (WMI events)");
            }
        }
        catch (Exception ex)
        {
            LogWatcherFailed(ex, "process lifetime (WMI events)");
        }

        var stateWatchers = new List<ProviderStateWatcher>();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach ((string? directory, bool includeSubdirectories) in EnumerateStateDirectories(userProfile))
        {
            try
            {
                ProviderStateWatcher? watcher = ProviderStateWatcher.TryCreate(
                    directory,
                    includeSubdirectories,
                    OnProviderStateChanged);
                if (watcher is not null)
                {
                    stateWatchers.Add(watcher);
                }
            }
            catch (Exception ex)
            {
                LogWatcherFailed(ex, directory ?? "provider state");
            }
        }

        return (processWatcher, stateWatchers);
    }

    private static IEnumerable<(string? Directory, bool IncludeSubdirectories)> EnumerateStateDirectories(
        string userProfile)
    {
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            yield break;
        }

        yield return (Path.Combine(userProfile, ".codex"), true);
        yield return (ClaudeCodeStateEvidenceCollector.FindClaudeProjectsDirectory(), true);
    }

    private void OnProcessChanged(AiSessionProcessChange change)
    {
        if (change.Kind == AiSessionProcessChangeKind.Started)
        {
            if (InterestingProcessNames.Contains(change.ProcessName))
            {
                RequestScan();
            }

            return;
        }

        bool tracked;
        lock (_stateGate)
        {
            tracked = _trackedPids.Contains(change.Pid);
        }

        if (tracked)
        {
            RequestScan();
        }
    }

    private void OnProviderStateChanged() => RequestScan();

    /// <summary>Coalesces bursts of external events into one debounced scan.</summary>
    private void RequestScan()
    {
        if (Interlocked.CompareExchange(ref _rescanQueued, 1, 0) != 0)
        {
            return;
        }

        CancellationToken token;
        lock (_stateGate)
        {
            if (_disposed || _lifecycleCts is null)
            {
                Interlocked.Exchange(ref _rescanQueued, 0);
                return;
            }

            token = _lifecycleCts.Token;
        }

        _ = DebouncedScanAsync(token);
    }

    private async Task DebouncedScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(EventScanDebounce, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref _rescanQueued, 0);
            return;
        }

        Interlocked.Exchange(ref _rescanQueued, 0);
        await ScanSafeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        await ScanSafeAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await ScanSafeAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task ScanSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ScanCoreAsync(force: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // Shutdown race with the scan gate.
        }
        catch (Exception ex)
        {
            LogScanFailed(ex);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "AI session discovery scan failed.")]
    private partial void LogScanFailed(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "AI session discovery started (event watchers healthy: {WatchersHealthy}).")]
    private partial void LogDiscoveryStarted(bool watchersHealthy);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "AI session discovery watcher unavailable: {Watcher}. Falling back to polling.")]
    private partial void LogWatcherUnavailable(string watcher);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "AI session discovery watcher failed to start: {Watcher}.")]
    private partial void LogWatcherFailed(Exception exception, string watcher);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "AI session evidence dropped ({Detector}, {Provider}): {Reason}")]
    private partial void LogEvidenceDropped(string detector, string provider, string reason);
}

/// <summary>Result counters from one AI discovery pass.</summary>
public sealed record AiSessionDiscoveryResult(int DetectedCount, int AddedCount, int CompletedCount);

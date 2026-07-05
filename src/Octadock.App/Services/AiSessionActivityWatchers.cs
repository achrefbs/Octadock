using System.IO;
using System.Management;
using Microsoft.Extensions.Logging;

namespace Octadock.App.Services;

/// <summary>
/// Turns AI-session discovery from "poll every 10 seconds" into event-driven:
///
///  1. A WMI process-creation subscription fires within ~1–2 s of any
///     interesting tool starting (claude, codex, node, ollama, cursor-agent,
///     gemini, copilot) and triggers an immediate scan.
///  2. File watchers on Codex's home (state DB + rollout logs) and Claude
///     Code's project transcripts trigger an immediate scan whenever a
///     session writes activity, so status transitions (working → done)
///     surface in well under a second instead of on the next sweep.
///
/// All triggers funnel into one debounced scan call; the periodic sweep in
/// <see cref="AiSessionDiscoveryService"/> remains as the reconciliation
/// safety net when WMI or the watchers are unavailable.
/// </summary>
public sealed partial class AiSessionActivityWatchers : IDisposable
{
    private static readonly TimeSpan ScanDebounce = TimeSpan.FromMilliseconds(450);

    private static readonly string[] InterestingProcessNames =
    [
        "claude.exe", "codex.exe", "node.exe", "ollama.exe",
        "cursor-agent.exe", "gemini.exe", "copilot.exe",
    ];

    private readonly AiSessionDiscoveryService _discovery;
    private readonly ILogger<AiSessionActivityWatchers> _logger;
    private readonly List<FileSystemWatcher> _fileWatchers = [];
    private readonly object _gate = new();

    private ManagementEventWatcher? _processWatcher;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _debounceCts;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates the watchers.</summary>
    public AiSessionActivityWatchers(
        AiSessionDiscoveryService discovery,
        ILogger<AiSessionActivityWatchers> logger)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts every available trigger source. Each is best-effort.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _lifetime = new CancellationTokenSource();

        StartProcessCreationWatcher();

        string? home = TryGetUserProfile();
        if (home is not null)
        {
            // Codex Desktop/CLI: state DB writes and rollout-log appends signal
            // thread starts, activity, and task_complete transitions.
            StartFileWatcher(Path.Combine(home, ".codex"), "*.*", includeSubdirectories: true);

            // Claude Code: every conversation appends to a project transcript.
            StartFileWatcher(Path.Combine(home, ".claude", "projects"), "*.jsonl", includeSubdirectories: true);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime?.Cancel();
        _debounceCts?.Cancel();

        try
        {
            _processWatcher?.Stop();
            _processWatcher?.Dispose();
        }
        catch (Exception ex)
        {
            LogWatcherDisposeFailed(ex);
        }

        foreach (FileSystemWatcher watcher in _fileWatchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                LogWatcherDisposeFailed(ex);
            }
        }

        _fileWatchers.Clear();
        _debounceCts?.Dispose();
        _lifetime?.Dispose();
    }

    private void StartProcessCreationWatcher()
    {
        try
        {
            string names = string.Join(" OR ", InterestingProcessNames
                .Select(n => $"TargetInstance.Name = '{n}'"));
            var query = new WqlEventQuery(
                $"SELECT * FROM __InstanceCreationEvent WITHIN 2 " +
                $"WHERE TargetInstance ISA 'Win32_Process' AND ({names})");
            _processWatcher = new ManagementEventWatcher(query);
            _processWatcher.EventArrived += (_, _) => TriggerScan("process start");
            _processWatcher.Start();
        }
        catch (Exception ex) when (ex is ManagementException
            or System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException
            or TypeInitializationException
            or PlatformNotSupportedException)
        {
            // WMI unavailable: the periodic sweep still covers process starts.
            LogProcessWatcherUnavailable(ex);
            _processWatcher = null;
        }
    }

    private void StartFileWatcher(string directory, string filter, bool includeSubdirectories)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var watcher = new FileSystemWatcher(directory, filter)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += (_, _) => TriggerScan("file activity");
            watcher.Created += (_, _) => TriggerScan("file activity");
            watcher.Renamed += (_, _) => TriggerScan("file activity");
            watcher.Error += (_, e) => LogFileWatcherError(e.GetException(), directory);
            watcher.EnableRaisingEvents = true;
            _fileWatchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException
            or ArgumentException
            or UnauthorizedAccessException)
        {
            LogFileWatcherUnavailable(ex, directory);
        }
    }

    /// <summary>Coalesces trigger bursts into one scan (~450 ms trailing edge).</summary>
    internal void TriggerScan(string reason)
    {
        CancellationTokenSource? lifetime = _lifetime;
        if (_disposed || lifetime is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        lock (_gate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            var debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _debounceCts = debounce;
            _ = DebouncedScanAsync(reason, debounce.Token);
        }
    }

    private async Task DebouncedScanAsync(string reason, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ScanDebounce, cancellationToken).ConfigureAwait(false);
            LogTriggeredScan(reason);
            await _discovery.ScanOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer trigger or shutdown.
        }
        catch (Exception ex)
        {
            LogTriggeredScanFailed(ex);
        }
    }

    private static string? TryGetUserProfile()
    {
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(profile) ? null : profile;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "WMI process-creation events are unavailable; falling back to the periodic sweep only.")]
    private partial void LogProcessWatcherUnavailable(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "AI-session file watcher unavailable for '{Directory}'.")]
    private partial void LogFileWatcherUnavailable(Exception exception, string directory);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "AI-session file watcher error in '{Directory}'.")]
    private partial void LogFileWatcherError(Exception exception, string directory);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug,
        Message = "Event-driven AI-session scan triggered ({Reason}).")]
    private partial void LogTriggeredScan(string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Debug,
        Message = "Event-triggered AI-session scan failed.")]
    private partial void LogTriggeredScanFailed(Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
        Message = "Disposing an AI-session activity watcher failed.")]
    private partial void LogWatcherDisposeFailed(Exception exception);
}

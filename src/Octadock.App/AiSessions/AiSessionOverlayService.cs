using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.AiSessions;

/// <summary>
/// Maintains the passive bottom-right overlay that mirrors live/recent AI
/// sessions without requiring the full sessions window to be open.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class AiSessionOverlayService : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RecentCompletionWindow = TimeSpan.FromSeconds(30);
    private const int QueryLimit = 80;
    private const int OverlayLimit = 8;

    private readonly IAiSessionRepository _sessions;
    private readonly AiSessionDiscoveryService _discovery;
    private readonly IMonitorService _monitors;
    private readonly IWindowPresenter _presenter;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly ILogger<AiSessionOverlayService> _logger;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _observedActiveSessionIds = [];
    private readonly HashSet<Guid> _dismissedSessionIds = [];

    private AiSessionOverlayViewModel? _viewModel;
    private AiSessionOverlayWindow? _window;
    private DispatcherTimer? _timer;
    private CancellationTokenSource? _refreshCts;
    private Dispatcher? _dispatcher;
    private int _refreshing;
    private bool _startRequested;
    private bool _started;
    private bool _disposed;

    public AiSessionOverlayService(
        IAiSessionRepository sessions,
        AiSessionDiscoveryService discovery,
        IMonitorService monitors,
        IWindowPresenter presenter,
        ISettingsService settings,
        IClock clock,
        ILogger<AiSessionOverlayService> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts the overlay refresh loop on the WPF dispatcher.</summary>
    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _dispatcher = dispatcher;

        lock (_gate)
        {
            if (_startRequested)
            {
                return;
            }

            _startRequested = true;
        }

        _settings.Changed += OnSettingsChanged;
        ApplyOverlayEnabled(_settings.Current.AiSessions.OverlayEnabled);
    }

    private void ApplyOverlayEnabled(bool enabled)
    {
        Dispatcher dispatcher = _dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            if (enabled)
            {
                StartOnDispatcher();
                QueueRefresh();
            }
            else
            {
                StopOnDispatcher();
            }

            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            if (enabled)
            {
                StartOnDispatcher();
                QueueRefresh();
            }
            else
            {
                StopOnDispatcher();
            }
        }));
    }

    /// <summary>Stops the overlay refresh loop and closes the window.</summary>
    public void Stop()
    {
        bool unsubscribe;
        lock (_gate)
        {
            unsubscribe = _startRequested;
            _startRequested = false;
        }

        if (unsubscribe)
        {
            _settings.Changed -= OnSettingsChanged;
        }

        Dispatcher? dispatcher = _dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(StopOnDispatcher));
            return;
        }

        StopOnDispatcher();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        lock (_gate)
        {
            if (!_startRequested || _disposed)
            {
                return;
            }
        }

        ApplyOverlayEnabled(e.Settings.AiSessions.OverlayEnabled);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void StartOnDispatcher()
    {
        lock (_gate)
        {
            if (_disposed || !_startRequested || _started)
            {
                return;
            }

            _viewModel = new AiSessionOverlayViewModel();
            _window = new AiSessionOverlayWindow(_viewModel, _monitors);
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = RefreshInterval,
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            _started = true;
        }

        QueueRefresh();
    }

    private void StopOnDispatcher()
    {
        lock (_gate)
        {
            _timer?.Stop();
            if (_timer is not null)
            {
                _timer.Tick -= OnTimerTick;
            }

            _timer = null;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;

            if (_window is not null)
            {
                _window.Close();
                _window = null;
            }

            _viewModel = null;
            _observedActiveSessionIds.Clear();
            _dismissedSessionIds.Clear();
            _started = false;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                cts.Dispose();
                Interlocked.Exchange(ref _refreshing, 0);
                return;
            }

            _refreshCts = cts;
        }

        _ = RefreshAsync(cts);
    }

    private async Task RefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            CancellationToken cancellationToken = cts.Token;
            await _discovery.ScanOnceAsync(cancellationToken).ConfigureAwait(false);

            var filter = new AiSessionFilter
            {
                Limit = QueryLimit,
            };
            IReadOnlyList<AiSessionRecord> records = await _sessions.ListAsync(filter, cancellationToken)
                .ConfigureAwait(false);

            DateTimeOffset now = _clock.UtcNow;
            MarkObservedActive(records);
            PruneDismissed(records);
            List<AiSessionOverlayItemViewModel> items = records
                .Where(record => ShouldShowRecord(
                    record,
                    now,
                    WasObservedActive(record.Id),
                    IsDismissed(record.Id),
                    _settings.Current.AiSessions.ShowRecentCompletions))
                .OrderByDescending(record => record.IsActive)
                .ThenByDescending(record => record.LastEventAt ?? record.EndedAt ?? record.StartedAt)
                .Take(OverlayLimit)
                .Select(record => new AiSessionOverlayItemViewModel(
                    record,
                    now,
                    DismissSession,
                    _presenter.ShowAiSessions))
                .ToList();

            Dispatcher? dispatcher = _dispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            await dispatcher.InvokeAsync(() => ApplyItems(items), DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested || _disposed)
        {
            // Normal shutdown or coalesced refresh.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh AI session overlay.");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_refreshCts, cts))
                {
                    _refreshCts = null;
                }
            }

            cts.Dispose();
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void ApplyItems(IReadOnlyList<AiSessionOverlayItemViewModel> items)
    {
        if (_disposed || _viewModel is null || _window is null)
        {
            return;
        }

        _viewModel.ReplaceItems(items);
        if (items.Count == 0)
        {
            _window.Hide();
            return;
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        _window.Reposition();
    }

    private void MarkObservedActive(IReadOnlyList<AiSessionRecord> records)
    {
        lock (_gate)
        {
            foreach (AiSessionRecord record in records)
            {
                if (record.IsActive)
                {
                    _observedActiveSessionIds.Add(record.Id);
                }
            }
        }
    }

    private bool WasObservedActive(Guid id)
    {
        lock (_gate)
        {
            return _observedActiveSessionIds.Contains(id);
        }
    }

    private bool IsDismissed(Guid id)
    {
        lock (_gate)
        {
            return _dismissedSessionIds.Contains(id);
        }
    }

    private void DismissSession(Guid id)
    {
        lock (_gate)
        {
            _dismissedSessionIds.Add(id);
        }

        Dispatcher? dispatcher = _dispatcher ?? Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RemoveDismissedItem(id);
            return;
        }

        dispatcher.BeginInvoke(new Action(() => RemoveDismissedItem(id)));
    }

    private void RemoveDismissedItem(Guid id)
    {
        if (_viewModel is null || _window is null)
        {
            return;
        }

        _viewModel.RemoveItem(id);
        if (_viewModel.Sessions.Count == 0)
        {
            _window.Hide();
            return;
        }

        _window.Reposition();
    }

    private void PruneDismissed(IReadOnlyList<AiSessionRecord> records)
    {
        lock (_gate)
        {
            if (_dismissedSessionIds.Count == 0)
            {
                return;
            }

            HashSet<Guid> current = records.Select(record => record.Id).ToHashSet();
            _dismissedSessionIds.RemoveWhere(id => !current.Contains(id));
        }
    }

    internal static bool ShouldShowRecord(
        AiSessionRecord record,
        DateTimeOffset now,
        bool wasObservedActive,
        bool isDismissed,
        bool showRecentCompletions)
    {
        if (isDismissed)
        {
            return false;
        }

        if (IsHiddenDiscoveryShell(record))
        {
            return false;
        }

        if (record.IsActive)
        {
            return true;
        }

        if (!showRecentCompletions)
        {
            return false;
        }

        DateTimeOffset activity = record.LastEventAt ?? record.EndedAt ?? record.StartedAt;
        if (activity < now.Subtract(RecentCompletionWindow))
        {
            return false;
        }

        return !IsProcessDiscovery(record) || wasObservedActive;
    }

    private static bool IsHiddenDiscoveryShell(AiSessionRecord record)
    {
        if (!IsProcessDiscovery(record))
        {
            return false;
        }

        string? detector = ReadMetadataString(record.MetadataJson, "detector");
        return string.Equals(detector, "codex-desktop", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(detector, "codex-app-server", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProcessDiscovery(AiSessionRecord record)
        => string.Equals(
            ReadMetadataString(record.MetadataJson, "source"),
            "process-discovery",
            StringComparison.OrdinalIgnoreCase);

    private static string? ReadMetadataString(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

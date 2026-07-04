using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.AiSessions;

/// <summary>
/// View model for the Active AI Sessions window. It intentionally reads from the
/// durable repository rather than process state so completed sessions, watched
/// PIDs and future provider adapters all land in one surface.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiSessionsViewModel : ObservableObject
{
    private const int SessionLimit = 100;

    private readonly IAiSessionRepository _sessions;
    private readonly AiSessionDiscoveryService _discovery;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly IStoragePaths _paths;
    private readonly ILogger<AiSessionsViewModel> _logger;
    private int _timelineVersion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isBusy;

    [ObservableProperty]
    private string _summary = "Loading sessions...";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _emptyMessage = "No AI sessions yet.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private AiSessionRowViewModel? _selectedSession;

    [ObservableProperty]
    private bool _isTimelineEmpty = true;

    [ObservableProperty]
    private string _timelineMessage = "Select a session.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenStdoutLog))]
    private string? _stdoutLogPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenStderrLog))]
    private string? _stderrLogPath;

    /// <summary>Creates the view model over the AI session repository.</summary>
    public AiSessionsViewModel(
        IAiSessionRepository sessions,
        AiSessionDiscoveryService discovery,
        IClipboardService clipboard,
        INotificationService notifications,
        IStoragePaths paths,
        ILogger<AiSessionsViewModel> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Recent sessions shown in the left pane.</summary>
    public ObservableCollection<AiSessionRowViewModel> Sessions { get; } = [];

    /// <summary>Timeline events for the selected session.</summary>
    public ObservableCollection<AiSessionEventRowViewModel> Events { get; } = [];

    public bool HasSelection => SelectedSession is not null;

    public bool IsEmpty => !IsBusy && Sessions.Count == 0;

    public bool CanCopySelectedCommand => !string.IsNullOrWhiteSpace(SelectedSession?.RawCommand);

    public bool CanRevealSelectedCwd => SelectedSession?.HasWorkingDirectory == true;

    public bool CanOpenStdoutLog => !string.IsNullOrWhiteSpace(StdoutLogPath);

    public bool CanOpenStderrLog => !string.IsNullOrWhiteSpace(StderrLogPath);

    /// <summary>Reloads the recent session list.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Loading...";
        EmptyMessage = string.IsNullOrWhiteSpace(SearchText)
            ? "No AI sessions yet."
            : "No AI sessions match this search.";

        Guid? previousSelection = SelectedSession?.Id;
        try
        {
            await _discovery.ScanOnceAsync(cancellationToken).ConfigureAwait(true);

            var filter = new AiSessionFilter
            {
                Limit = SessionLimit,
                SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            };

            IReadOnlyList<AiSessionRecord> records = await _sessions.ListAsync(filter, cancellationToken)
                .ConfigureAwait(true);

            Sessions.Clear();
            AiSessionRowViewModel? selected = null;
            var now = DateTimeOffset.Now;
            var activeCount = 0;
            foreach (AiSessionRecord record in records)
            {
                var row = new AiSessionRowViewModel(record, now);
                Sessions.Add(row);
                if (row.IsActive)
                {
                    activeCount++;
                }

                if (previousSelection == row.Id)
                {
                    selected = row;
                }
            }

            SelectedSession = selected ?? Sessions.FirstOrDefault();
            Summary = FormatSummary(records.Count, activeCount, filter.SearchText);
            StatusMessage = records.Count == 0 ? "No sessions" : $"{records.Count} shown";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI sessions.");
            Sessions.Clear();
            Events.Clear();
            StdoutLogPath = null;
            StderrLogPath = null;
            SelectedSession = null;
            Summary = "AI sessions could not be loaded.";
            StatusMessage = "Load failed";
            TimelineMessage = "Timeline unavailable.";
            IsTimelineEmpty = true;
            _notifications.Notify("AI Sessions", "Could not load tracked sessions.", NotificationKind.Error);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand]
    private void CopyCommandText()
    {
        string? command = SelectedSession?.RawCommand;
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        try
        {
            _clipboard.SetText(command);
            _notifications.Notify("AI Sessions", "Command copied.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy AI session command.");
            _notifications.Notify("AI Sessions", "Could not copy the command.", NotificationKind.Error);
        }
    }

    [RelayCommand]
    private void CopySessionId()
    {
        if (SelectedSession is null)
        {
            return;
        }

        try
        {
            _clipboard.SetText(SelectedSession.Id.ToString("D"));
            _notifications.Notify("AI Sessions", "Session ID copied.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy AI session ID.");
            _notifications.Notify("AI Sessions", "Could not copy the session ID.", NotificationKind.Error);
        }
    }

    [RelayCommand]
    private void RevealCwd()
    {
        string? cwd = SelectedSession?.RawCwd;
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(cwd))
            {
                _notifications.Notify("AI Sessions", "The working folder no longer exists.", NotificationKind.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = cwd,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open AI session working folder {Cwd}.", cwd);
            _notifications.Notify("AI Sessions", "Could not open the working folder.", NotificationKind.Error);
        }
    }

    [RelayCommand]
    private void OpenStdoutLog() => OpenLog(StdoutLogPath, "stdout");

    [RelayCommand]
    private void OpenStderrLog() => OpenLog(StderrLogPath, "stderr");

    private void OpenLog(string? relativePath, string stream)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            string path = _paths.ToAbsolute(relativePath);
            if (!File.Exists(path))
            {
                _notifications.Notify("AI Sessions", $"The {stream} log file no longer exists.", NotificationKind.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open AI session {Stream} log {Path}.", stream, relativePath);
            _notifications.Notify("AI Sessions", $"Could not open the {stream} log.", NotificationKind.Error);
        }
    }

    partial void OnSelectedSessionChanged(AiSessionRowViewModel? value)
    {
        OnPropertyChanged(nameof(CanCopySelectedCommand));
        OnPropertyChanged(nameof(CanRevealSelectedCwd));
        _ = LoadTimelineAsync(value);
    }

    private async Task LoadTimelineAsync(AiSessionRowViewModel? session)
    {
        int version = Interlocked.Increment(ref _timelineVersion);
        Events.Clear();
        StdoutLogPath = null;
        StderrLogPath = null;
        IsTimelineEmpty = true;

        if (session is null)
        {
            TimelineMessage = "Select a session.";
            return;
        }

        TimelineMessage = "Loading timeline...";
        try
        {
            IReadOnlyList<AiSessionEventRecord> events = await _sessions.GetEventsAsync(session.Id)
                .ConfigureAwait(true);
            IReadOnlyList<AiSessionArtifactRecord> artifacts = await _sessions.GetArtifactsAsync(session.Id)
                .ConfigureAwait(true);

            if (version != _timelineVersion)
            {
                return;
            }

            foreach (AiSessionEventRecord record in events)
            {
                Events.Add(new AiSessionEventRowViewModel(record));
            }

            IsTimelineEmpty = Events.Count == 0;
            TimelineMessage = Events.Count == 0 ? "No timeline events yet." : string.Empty;
            StdoutLogPath = FindLogPath(artifacts, "stdout");
            StderrLogPath = FindLogPath(artifacts, "stderr");
        }
        catch (Exception ex)
        {
            if (version != _timelineVersion)
            {
                return;
            }

            _logger.LogWarning(ex, "Failed to load AI session timeline {SessionId}.", session.Id);
            Events.Clear();
            StdoutLogPath = null;
            StderrLogPath = null;
            IsTimelineEmpty = true;
            TimelineMessage = "Could not load timeline.";
        }
    }

    private static string? FindLogPath(IReadOnlyList<AiSessionArtifactRecord> artifacts, string stream)
    {
        AiSessionArtifactRecord? match = artifacts.FirstOrDefault(artifact =>
            artifact.ArtifactKind == AiSessionArtifactKind.Log &&
            !string.IsNullOrWhiteSpace(artifact.Path) &&
            string.Equals(ReadMetadataValue(artifact.MetadataJson, "stream"), stream, StringComparison.OrdinalIgnoreCase));

        match ??= artifacts.FirstOrDefault(artifact =>
            artifact.ArtifactKind == AiSessionArtifactKind.Log &&
            !string.IsNullOrWhiteSpace(artifact.Path) &&
            string.Equals(artifact.Title, $"{stream} log", StringComparison.OrdinalIgnoreCase));

        return match?.Path;
    }

    private static string? ReadMetadataValue(string? metadataJson, string key)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(key, out JsonElement element)
                ? element.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string FormatSummary(int total, int active, string? searchText)
    {
        string prefix = string.IsNullOrWhiteSpace(searchText)
            ? "Recent tracked process sessions"
            : $"Search results for \"{searchText}\"";
        return total == 0
            ? $"{prefix} - none found"
            : $"{prefix} - {total} shown, {active} active";
    }
}

/// <summary>Display projection for one persisted AI session.</summary>
public sealed class AiSessionRowViewModel
{
    private static readonly SolidColorBrush AccentBrush = FrozenBrush(Color.FromRgb(45, 212, 191));
    private static readonly SolidColorBrush SuccessBrush = FrozenBrush(Color.FromRgb(134, 239, 172));
    private static readonly SolidColorBrush DangerBrush = FrozenBrush(Color.FromRgb(248, 113, 113));
    private static readonly SolidColorBrush MutedBrush = FrozenBrush(Color.FromRgb(148, 163, 184));
    private static readonly SolidColorBrush DarkTextBrush = FrozenBrush(Color.FromRgb(8, 34, 31));
    private static readonly SolidColorBrush LightTextBrush = FrozenBrush(Color.FromRgb(241, 245, 249));

    public AiSessionRowViewModel(AiSessionRecord record, DateTimeOffset now)
    {
        Id = record.Id;
        Provider = record.Provider;
        Status = record.Status;
        Title = string.IsNullOrWhiteSpace(record.Title) ? "Untitled session" : record.Title;
        RawCommand = record.Command;
        RawCwd = record.Cwd;
        CwdLabel = string.IsNullOrWhiteSpace(record.Cwd) ? "No working folder recorded" : record.Cwd;
        CommandLabel = string.IsNullOrWhiteSpace(record.Command) ? "No command recorded" : record.Command;
        ProviderLabel = AiSessionFormatting.Humanize(record.Provider);
        StatusLabel = AiSessionFormatting.Humanize(record.Status);
        IsActive = record.IsActive;
        StartedLabel = $"Started {AiSessionFormatting.RelativeTime(record.StartedAt, now)}";
        LastActivityLabel = $"Last activity {AiSessionFormatting.RelativeTime(record.LastEventAt ?? record.StartedAt, now)}";
        DurationLabel = AiSessionFormatting.Duration(record.StartedAt, record.EndedAt ?? now);
        ProcessLabel = record.Pid is null ? "No PID" : $"PID {record.Pid.Value}";
        ExitLabel = record.ExitCode is null ? (IsActive ? "Running" : "No exit code") : $"Exit {record.ExitCode.Value}";
        BranchLabel = string.IsNullOrWhiteSpace(record.GitBranch) ? "No branch" : record.GitBranch;
        DetailLine = $"{ProviderLabel} - {StatusLabel} - {ProcessLabel} - {ExitLabel} - {LastActivityLabel}";
        StatusBrush = ResolveStatusBrush(record.Status);
        StatusForeground = record.Status is AiSessionStatus.Failed or AiSessionStatus.Cancelled
            ? LightTextBrush
            : DarkTextBrush;
    }

    public Guid Id { get; }

    public AiSessionProvider Provider { get; }

    public AiSessionStatus Status { get; }

    public string Title { get; }

    public string? RawCommand { get; }

    public string? RawCwd { get; }

    public string CommandLabel { get; }

    public string CwdLabel { get; }

    public string ProviderLabel { get; }

    public string StatusLabel { get; }

    public bool IsActive { get; }

    public bool HasWorkingDirectory => !string.IsNullOrWhiteSpace(RawCwd);

    public string StartedLabel { get; }

    public string LastActivityLabel { get; }

    public string DurationLabel { get; }

    public string ProcessLabel { get; }

    public string ExitLabel { get; }

    public string BranchLabel { get; }

    public string DetailLine { get; }

    public Brush StatusBrush { get; }

    public Brush StatusForeground { get; }

    private static SolidColorBrush ResolveStatusBrush(AiSessionStatus status)
        => status switch
        {
            AiSessionStatus.Completed => SuccessBrush,
            AiSessionStatus.Failed or AiSessionStatus.Cancelled => DangerBrush,
            AiSessionStatus.Running or AiSessionStatus.WaitingForInput or AiSessionStatus.Queued => AccentBrush,
            _ => MutedBrush,
        };

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Display projection for one session timeline event.</summary>
public sealed class AiSessionEventRowViewModel
{
    public AiSessionEventRowViewModel(AiSessionEventRecord record)
    {
        TypeLabel = AiSessionFormatting.Humanize(record.EventType);
        CreatedLabel = record.CreatedAt.ToLocalTime().ToString("g");
        Message = string.IsNullOrWhiteSpace(record.Message) ? "No details recorded." : record.Message;
    }

    public string TypeLabel { get; }

    public string CreatedLabel { get; }

    public string Message { get; }
}

internal static class AiSessionFormatting
{
    public static string Humanize(Enum value)
    {
        string text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(text[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    public static string RelativeTime(DateTimeOffset timestamp, DateTimeOffset now)
    {
        TimeSpan delta = now - timestamp;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalSeconds < 45)
        {
            return "just now";
        }

        if (delta.TotalMinutes < 60)
        {
            int minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
            return $"{minutes}m ago";
        }

        if (delta.TotalHours < 24)
        {
            int hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return $"{hours}h ago";
        }

        if (delta.TotalDays < 7)
        {
            int days = Math.Max(1, (int)Math.Round(delta.TotalDays));
            return $"{days}d ago";
        }

        return timestamp.ToLocalTime().ToString("g");
    }

    public static string Duration(DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        TimeSpan duration = endedAt - startedAt;
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalSeconds < 60)
        {
            return $"{Math.Max(0, (int)Math.Round(duration.TotalSeconds))}s";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        if (duration.TotalHours < 24)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{(int)duration.TotalDays}d {duration.Hours}h";
    }
}

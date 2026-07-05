using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Octadock.Core.Models;

namespace Octadock.App.AiSessions;

/// <summary>Compact projection for the bottom-right live AI session overlay.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiSessionOverlayViewModel : ObservableObject
{
    [ObservableProperty]
    private string _headline = "AI Sessions";

    [ObservableProperty]
    private string _subline = string.Empty;

    /// <summary>Sessions currently shown in the overlay.</summary>
    public ObservableCollection<AiSessionOverlayItemViewModel> Sessions { get; } = [];

    public bool HasItems => Sessions.Count > 0;

    /// <summary>
    /// Synchronizes the visible items with a freshly built list, updating
    /// existing rows IN PLACE. Rebuilding containers on every 2-second refresh
    /// restarted the live ring animation (visible stutter), closed tooltips,
    /// and swallowed in-flight clicks; this only adds/removes/moves rows when
    /// membership or order actually changed. Returns true when the set of rows
    /// (not just their labels) changed, so the caller knows to reposition.
    /// </summary>
    public bool SyncItems(IReadOnlyList<AiSessionOverlayItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        bool membershipChanged = false;
        var incomingIds = items.Select(i => i.Id).ToHashSet();

        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!incomingIds.Contains(Sessions[i].Id))
            {
                Sessions.RemoveAt(i);
                membershipChanged = true;
            }
        }

        for (int target = 0; target < items.Count; target++)
        {
            AiSessionOverlayItemViewModel incoming = items[target];
            int existingIndex = -1;
            for (int i = target; i < Sessions.Count; i++)
            {
                if (Sessions[i].Id == incoming.Id)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                Sessions.Insert(target, incoming);
                membershipChanged = true;
            }
            else
            {
                if (existingIndex != target)
                {
                    Sessions.Move(existingIndex, target);
                    membershipChanged = true;
                }

                Sessions[target].UpdateFrom(incoming);
            }
        }

        while (Sessions.Count > items.Count)
        {
            Sessions.RemoveAt(Sessions.Count - 1);
            membershipChanged = true;
        }

        int liveCount = items.Count(i => i.IsActive);
        int doneCount = items.Count - liveCount;
        Headline = liveCount > 0
            ? $"{liveCount} AI session{Plural(liveCount)} live"
            : "AI Sessions";
        Subline = liveCount > 0 && doneCount > 0
            ? $"{doneCount} recently finished"
            : liveCount > 0
                ? "Watching local work"
                : $"{doneCount} recently finished";
        OnPropertyChanged(nameof(HasItems));
        return membershipChanged;
    }

    public void RemoveItem(Guid id)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (Sessions[i].Id == id)
            {
                Sessions.RemoveAt(i);
            }
        }

        OnPropertyChanged(nameof(HasItems));
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

/// <summary>
/// One visual item in the live AI session overlay. Mutable display properties
/// are observable so a refresh can update a row in place instead of replacing
/// its container (which restarted the live-ring animation).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiSessionOverlayItemViewModel : ObservableObject
{
    // Obsidian-glass status colors: live = brand teal (not blue), waiting =
    // amber, done = green, failed = rose, quiet = muted slate.
    private static readonly SolidColorBrush LiveBrush = FrozenBrush(Color.FromRgb(45, 212, 191));
    private static readonly SolidColorBrush WaitingBrush = FrozenBrush(Color.FromRgb(251, 191, 36));
    private static readonly SolidColorBrush DoneBrush = FrozenBrush(Color.FromRgb(74, 222, 128));
    private static readonly SolidColorBrush FailedBrush = FrozenBrush(Color.FromRgb(251, 113, 133));
    private static readonly SolidColorBrush QuietBrush = FrozenBrush(Color.FromRgb(141, 160, 188));

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private AiSessionStatus _status;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _statusLabel;

    [ObservableProperty]
    private string _statusGlyph;

    [ObservableProperty]
    private string _activityLabel;

    [ObservableProperty]
    private string _detailLabel;

    [ObservableProperty]
    private string _toolTipLabel;

    [ObservableProperty]
    private Brush _statusBrush;

    [ObservableProperty]
    private Brush _accentBrush;

    public AiSessionOverlayItemViewModel(
        AiSessionRecord record,
        DateTimeOffset now,
        Action<Guid>? dismiss = null,
        Action? open = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        Id = record.Id;
        _title = string.IsNullOrWhiteSpace(record.Title) ? "Untitled AI session" : record.Title;
        _status = record.Status;
        _isActive = record.IsActive;
        ProviderLabel = AiSessionFormatting.Humanize(record.Provider);
        _statusLabel = AiSessionFormatting.Humanize(record.Status);
        _statusBrush = ResolveStatusBrush(record.Status);
        _accentBrush = _statusBrush;
        _statusGlyph = ResolveStatusGlyph(record.Status);
        OpenCommand = new RelayCommand(() => open?.Invoke());
        DismissCommand = new RelayCommand(() => dismiss?.Invoke(Id));

        DateTimeOffset activity = record.LastEventAt ?? record.EndedAt ?? record.StartedAt;
        _activityLabel = _isActive
            ? $"Live for {AiSessionFormatting.Duration(record.StartedAt, now)}"
            : $"{_statusLabel} {AiSessionFormatting.RelativeTime(activity, now)}";

        string location = FormatLocation(record.Cwd);
        string process = record.Pid is null ? "No PID" : $"PID {record.Pid.Value}";
        _detailLabel = string.IsNullOrWhiteSpace(location)
            ? process
            : $"{location} - {process}";
        _toolTipLabel = $"{Title}\n{StatusLabel} - {ActivityLabel}\n{DetailLabel}";
    }

    public Guid Id { get; }

    public string ProviderLabel { get; }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand DismissCommand { get; }

    /// <summary>Copies the freshly computed display state onto this (same-id) row.</summary>
    public void UpdateFrom(AiSessionOverlayItemViewModel other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Title = other.Title;
        Status = other.Status;
        IsActive = other.IsActive;
        StatusLabel = other.StatusLabel;
        StatusGlyph = other.StatusGlyph;
        ActivityLabel = other.ActivityLabel;
        DetailLabel = other.DetailLabel;
        ToolTipLabel = other.ToolTipLabel;
        StatusBrush = other.StatusBrush;
        AccentBrush = other.AccentBrush;
    }

    private static string FormatLocation(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return string.Empty;
        }

        try
        {
            string? name = Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? cwd : name;
        }
        catch (ArgumentException)
        {
            return cwd;
        }
    }

    private static SolidColorBrush ResolveStatusBrush(AiSessionStatus status)
        => status switch
        {
            AiSessionStatus.Queued or AiSessionStatus.Running => LiveBrush,
            AiSessionStatus.WaitingForInput or AiSessionStatus.Paused => WaitingBrush,
            AiSessionStatus.Completed => DoneBrush,
            AiSessionStatus.Failed or AiSessionStatus.Cancelled => FailedBrush,
            _ => QuietBrush,
        };

    private static string ResolveStatusGlyph(AiSessionStatus status)
        => status switch
        {
            AiSessionStatus.Queued or AiSessionStatus.Running => "\uE895",
            AiSessionStatus.WaitingForInput or AiSessionStatus.Paused => "\uE7BA",
            AiSessionStatus.Completed => "\uE8FB",
            AiSessionStatus.Failed or AiSessionStatus.Cancelled => "\uE711",
            _ => "\uE946",
        };

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

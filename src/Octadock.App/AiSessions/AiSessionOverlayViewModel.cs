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

    public void ReplaceItems(IReadOnlyList<AiSessionOverlayItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Sessions.Clear();
        foreach (AiSessionOverlayItemViewModel item in items)
        {
            Sessions.Add(item);
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

/// <summary>One visual item in the live AI session overlay.</summary>
[SupportedOSPlatform("windows")]
public sealed class AiSessionOverlayItemViewModel
{
    private static readonly SolidColorBrush LiveBrush = FrozenBrush(Color.FromRgb(56, 189, 248));
    private static readonly SolidColorBrush WaitingBrush = FrozenBrush(Color.FromRgb(251, 191, 36));
    private static readonly SolidColorBrush DoneBrush = FrozenBrush(Color.FromRgb(74, 222, 128));
    private static readonly SolidColorBrush FailedBrush = FrozenBrush(Color.FromRgb(251, 113, 133));
    private static readonly SolidColorBrush QuietBrush = FrozenBrush(Color.FromRgb(148, 163, 184));

    public AiSessionOverlayItemViewModel(
        AiSessionRecord record,
        DateTimeOffset now,
        Action<Guid>? dismiss = null,
        Action? open = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        Id = record.Id;
        Title = string.IsNullOrWhiteSpace(record.Title) ? "Untitled AI session" : record.Title;
        Status = record.Status;
        IsActive = record.IsActive;
        ProviderLabel = AiSessionFormatting.Humanize(record.Provider);
        StatusLabel = AiSessionFormatting.Humanize(record.Status);
        StatusBrush = ResolveStatusBrush(record.Status);
        AccentBrush = StatusBrush;
        StatusGlyph = ResolveStatusGlyph(record.Status);
        OpenCommand = new RelayCommand(() => open?.Invoke());
        DismissCommand = new RelayCommand(() => dismiss?.Invoke(Id));

        DateTimeOffset activity = record.LastEventAt ?? record.EndedAt ?? record.StartedAt;
        ActivityLabel = IsActive
            ? $"Live for {AiSessionFormatting.Duration(record.StartedAt, now)}"
            : $"{StatusLabel} {AiSessionFormatting.RelativeTime(activity, now)}";

        string location = FormatLocation(record.Cwd);
        string process = record.Pid is null ? "No PID" : $"PID {record.Pid.Value}";
        DetailLabel = string.IsNullOrWhiteSpace(location)
            ? process
            : $"{location} - {process}";
        ToolTipLabel = $"{Title}\n{StatusLabel} - {ActivityLabel}\n{DetailLabel}";
    }

    public Guid Id { get; }

    public string Title { get; }

    public AiSessionStatus Status { get; }

    public bool IsActive { get; }

    public string ProviderLabel { get; }

    public string StatusLabel { get; }

    public string StatusGlyph { get; }

    public string ActivityLabel { get; }

    public string DetailLabel { get; }

    public string ToolTipLabel { get; }

    public Brush StatusBrush { get; }

    public Brush AccentBrush { get; }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand DismissCommand { get; }

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

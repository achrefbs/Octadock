namespace Octadock.Core.Settings;

/// <summary>
/// The complete, strongly-typed application settings aggregate. Persisted as
/// individual key/value rows (see <see cref="SettingKeys"/>) but surfaced to the
/// app as one immutable object. Use <c>with</c> expressions to produce edited
/// copies.
/// </summary>
public sealed record OctadockSettings
{
    /// <summary>Schema version for settings migrations.</summary>
    public int Version { get; init; } = 2;

    public GeneralSettings General { get; init; } = new();

    public DockSettings Dock { get; init; } = new();

    public CaptureSettings Capture { get; init; } = new();

    public ShelfSettings Shelf { get; init; } = new();

    public HistorySettings History { get; init; } = new();

    public ClipboardSettings Clipboard { get; init; } = new();

    public OcrSettings Ocr { get; init; } = new();

    public SpeechSettings Speech { get; init; } = new();

    public RecordingSettings Recording { get; init; } = new();

    public AiSessionSettings AiSessions { get; init; } = new();

    public ShortcutSettings Shortcuts { get; init; } = new();

    public AutomationSettings Automation { get; init; } = new();

    /// <summary>A fresh settings object with all defaults.</summary>
    public static OctadockSettings Defaults => new();
}

/// <summary>Helpers that derive concrete values from settings enums.</summary>
public static class SettingsMath
{
    /// <summary>Returns the auto-close delay in seconds, or 0 for never / after-action.</summary>
    public static int AutoCloseSeconds(this ShelfAutoCloseMode mode) => mode switch
    {
        ShelfAutoCloseMode.Seconds30 => 30,
        ShelfAutoCloseMode.Minutes1 => 60,
        ShelfAutoCloseMode.Minutes5 => 300,
        _ => 0,
    };

    /// <summary>Returns the retention window in days, or <c>null</c> for forever/disabled.</summary>
    public static int? RetentionDays(this HistoryRetention retention) => retention switch
    {
        HistoryRetention.OneDay => 1,
        HistoryRetention.SevenDays => 7,
        HistoryRetention.ThirtyDays => 30,
        _ => null,
    };
}

using Octadock.Core.Commands;
using Octadock.Core.Hotkeys;

namespace Octadock.Core.Settings;

/// <summary>General application behavior.</summary>
public sealed record GeneralSettings
{
    public bool LaunchAtLogin { get; init; }

    public bool ShowTrayIcon { get; init; } = true;

    public bool ShowTaskbarIcon { get; init; }

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>Set once the first-run wizard has completed.</summary>
    public bool FirstRunCompleted { get; init; }

    /// <summary>Opt-in crash/error reporting. Off by default (privacy-first).</summary>
    public bool CrashReportingEnabled { get; init; }
}

/// <summary>The always-on-screen Octadock dock capsule.</summary>
public sealed record DockSettings
{
    /// <summary>Whether the permanent dock capsule is shown.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// True once the user has dragged the dock, so <see cref="AnchorX"/>/<see cref="AnchorY"/>
    /// hold a saved position that overrides the default bottom-center placement.
    /// </summary>
    public bool HasCustomAnchor { get; init; }

    /// <summary>The saved anchor-center X in physical pixels (valid when <see cref="HasCustomAnchor"/>).</summary>
    public int AnchorX { get; init; }

    /// <summary>The saved anchor-center Y in physical pixels (valid when <see cref="HasCustomAnchor"/>).</summary>
    public int AnchorY { get; init; }
}

/// <summary>Capture behavior and output.</summary>
public sealed record CaptureSettings
{
    public PostCaptureAction DefaultAction { get; init; } = PostCaptureAction.Shelf;

    /// <summary>Folder used by Save. Empty means prompt / use Pictures\Octadock.</summary>
    public string SaveDirectory { get; init; } = string.Empty;

    public string FilenameTemplate { get; init; } = "Screenshot {yyyy}-{MM}-{dd} at {HH}.{mm}.{ss}";

    public bool IncludeCursor { get; init; }

    public bool WindowShadow { get; init; } = true;

    /// <summary>Exclude Octadock's own windows from captures where the OS supports it.</summary>
    public bool ExcludeOctadockWindows { get; init; } = true;

    public MultiMonitorCaptureMode MultiMonitorMode { get; init; } = MultiMonitorCaptureMode.ActiveMonitor;

    public CaptureImageFormat ImageFormat { get; init; } = CaptureImageFormat.Png;

    /// <summary>JPEG quality (1-100) when <see cref="ImageFormat"/> is JPEG.</summary>
    public int JpegQuality { get; init; } = 90;

    public int SelfTimerSeconds { get; init; } = 5;

    /// <summary>Freeze the screen contents while selecting a region.</summary>
    public bool FreezeScreen { get; init; } = true;
}

/// <summary>Capture Shelf appearance and lifecycle.</summary>
public sealed record ShelfSettings
{
    public ShelfAnchor Anchor { get; init; } = ShelfAnchor.BottomLeft;

    public ShelfSize Size { get; init; } = ShelfSize.Medium;

    public ShelfAutoCloseMode AutoClose { get; init; } = ShelfAutoCloseMode.Never;

    /// <summary>Allow restoring the most recently closed shelf item.</summary>
    public bool RestoreEnabled { get; init; } = true;

    /// <summary>Margin in DIPs between the shelf and the work-area edges.</summary>
    public int MarginDip { get; init; } = 16;

    /// <summary>Maximum number of items stacked on the shelf at once.</summary>
    public int MaxItems { get; init; } = 8;
}

/// <summary>Local history and retention.</summary>
public sealed record HistorySettings
{
    public bool Enabled { get; init; } = true;

    public HistoryRetention Retention { get; init; } = HistoryRetention.ThirtyDays;
}

/// <summary>OCR configuration.</summary>
public sealed record OcrSettings
{
    public OcrProvider Provider { get; init; } = OcrProvider.WindowsMediaOcr;

    public OcrTextMode OutputMode { get; init; } = OcrTextMode.Lines;

    /// <summary>Preferred recognition language (BCP-47), or empty to auto-detect.</summary>
    public string PreferredLanguage { get; init; } = string.Empty;
}

/// <summary>Speech-to-text and dictation configuration.</summary>
public sealed record SpeechSettings
{
    public const string DefaultProvider = "whisper";
    public const string OpenAiProvider = "openai";
    public const string DefaultWhisperModel = "small";
    public const string DefaultOpenAiModel = "gpt-4o-transcribe";
    public const string DefaultLanguage = "";
    public const string DefaultInsertionMode = "paste";

    /// <summary>Speech provider id. Defaults to local Whisper; cloud providers are opt-in.</summary>
    public string Provider { get; init; } = DefaultProvider;

    /// <summary>Whisper ggml model variant, for example "small", "base.en", or "small.en".</summary>
    public string WhisperModel { get; init; } = DefaultWhisperModel;

    /// <summary>OpenAI transcription model used by the opt-in cloud provider.</summary>
    public string OpenAiModel { get; init; } = DefaultOpenAiModel;

    /// <summary>BCP-47-ish language hint, or empty to let the provider auto-detect.</summary>
    public string Language { get; init; } = DefaultLanguage;

    /// <summary>How recognized text is inserted: "paste" or "clipboard".</summary>
    public string InsertionMode { get; init; } = DefaultInsertionMode;

    /// <summary>
    /// User-defined dictation replacements, one per line. Supported separators
    /// are "=>" and "="; for example: "arrow function => =>".
    /// </summary>
    public string CustomDictionary { get; init; } = string.Empty;
}

/// <summary>Screen-recording configuration.</summary>
public sealed record RecordingSettings
{
    public int Fps { get; init; } = 30;

    public RecordingQuality Quality { get; init; } = RecordingQuality.Medium;

    public bool IncludeCursor { get; init; } = true;

    public bool IncludeMicrophone { get; init; }

    public bool IncludeSystemAudio { get; init; }
}

/// <summary>Active AI Sessions visibility and notification behavior.</summary>
public sealed record AiSessionSettings
{
    /// <summary>Show the passive desktop overlay for live/recent AI sessions.</summary>
    public bool OverlayEnabled { get; init; } = true;

    /// <summary>Keep recently completed sessions visible briefly in the overlay.</summary>
    public bool ShowRecentCompletions { get; init; } = true;
}

/// <summary>Global capture shortcuts.</summary>
public sealed record ShortcutSettings
{
    public HotkeyGesture CaptureArea { get; init; } = Parse("Ctrl+Shift+4");

    public HotkeyGesture CaptureWindow { get; init; } = Parse("Ctrl+Shift+5");

    public HotkeyGesture CaptureFullscreen { get; init; } = Parse("Ctrl+Shift+3");

    public HotkeyGesture CapturePreviousArea { get; init; } = Parse("Ctrl+Shift+6");

    public HotkeyGesture AllInOne { get; init; } = Parse("Ctrl+Shift+1");

    public HotkeyGesture Dictation { get; init; } = Parse("Ctrl+Shift+2");

    public HotkeyGesture Ocr { get; init; } = Parse("Ctrl+Shift+7");

    public HotkeyGesture Record { get; init; } = Parse("Ctrl+Shift+8");

    /// <summary>Enumerates each action with its configured gesture.</summary>
    public IEnumerable<(HotkeyAction Action, HotkeyGesture Gesture)> Enumerate()
    {
        yield return (HotkeyAction.CaptureArea, CaptureArea);
        yield return (HotkeyAction.CaptureWindow, CaptureWindow);
        yield return (HotkeyAction.CaptureFullscreen, CaptureFullscreen);
        yield return (HotkeyAction.CapturePreviousArea, CapturePreviousArea);
        yield return (HotkeyAction.AllInOne, AllInOne);
        yield return (HotkeyAction.Dictation, Dictation);
        yield return (HotkeyAction.Ocr, Ocr);
        yield return (HotkeyAction.Record, Record);
    }

    private static HotkeyGesture Parse(string chord)
        => HotkeyGesture.TryParse(chord, out HotkeyGesture g) ? g : HotkeyGesture.None;
}

/// <summary>Automation surface toggles.</summary>
public sealed record AutomationSettings
{
    public bool ProtocolEnabled { get; init; } = true;

    public bool CliEnabled { get; init; } = true;
}

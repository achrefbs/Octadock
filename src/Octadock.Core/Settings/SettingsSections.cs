using Octadock.Core.Commands;
using Octadock.Core.Hotkeys;

namespace Octadock.Core.Settings;

/// <summary>General application behavior.</summary>
public sealed record GeneralSettings
{
    /// <summary>Default-on for fresh profiles; first run offers a clear opt-out. A stored choice always wins.</summary>
    public bool LaunchAtLogin { get; init; } = true;

    public bool ShowTrayIcon { get; init; } = true;

    public bool ShowTaskbarIcon { get; init; }

    /// <summary>
    /// Dark is Octadock's default look (the "obsidian glass" identity); Light
    /// and System remain selectable in Settings.
    /// </summary>
    public ThemePreference Theme { get; init; } = ThemePreference.Dark;

    /// <summary>Set once the first-run wizard has completed.</summary>
    public bool FirstRunCompleted { get; init; }

    /// <summary>Opt-in crash/error reporting. Off by default (privacy-first).</summary>
    public bool CrashReportingEnabled { get; init; }
}

/// <summary>The always-on-screen Octadock dock capsule.</summary>
public sealed record DockSettings
{
    /// <summary>Anchor fractions within each display's work area, independent of DPI and desktop origin.</summary>
    public Dictionary<string, DockAnchor> MonitorAnchors { get; init; } = new(StringComparer.OrdinalIgnoreCase);

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

/// <summary>A normalized dock center relative to a monitor's usable area.</summary>
public sealed record DockAnchor
{
    public double X { get; init; }
    public double Y { get; init; }
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

    /// <summary>Show the live dimensions and the magnifier loupe while selecting a region.</summary>
    public bool PrecisionAids { get; init; } = true;

    /// <summary>Constrain area selection to exactly <see cref="FixedWidth"/> × <see cref="FixedHeight"/> physical pixels.</summary>
    public bool FixedSizeEnabled { get; init; }

    /// <summary>Fixed selection width in physical pixels (0 = unset).</summary>
    public int FixedWidth { get; init; }

    /// <summary>Fixed selection height in physical pixels (0 = unset).</summary>
    public int FixedHeight { get; init; }

    /// <summary>Keep the most recent selection's aspect ratio while dragging a new one.</summary>
    public bool LockAspectRatio { get; init; }

    /// <summary>Default save behavior for quick edits made in the annotation editor.</summary>
    public ImageEditSaveBehavior ImageEditSaveBehavior { get; init; } = ImageEditSaveBehavior.Ask;
}

/// <summary>Capture Shelf appearance and lifecycle.</summary>
public sealed record ShelfSettings
{
    /// <summary>Show the optional Shelf frame/header. Off keeps only the capture surfaces visible.</summary>
    public bool ShowChrome { get; init; }

    public ShelfAnchor Anchor { get; init; } = ShelfAnchor.BottomLeft;

    public ShelfSize Size { get; init; } = ShelfSize.Medium;

    public ShelfAutoCloseMode AutoClose { get; init; } = ShelfAutoCloseMode.Never;

    /// <summary>
    /// Legacy persisted peek choice retained for settings compatibility. The
    /// current Shelf always uses its fixed edge tab to collapse in place.
    /// </summary>
    public ShelfPeekBehavior PeekBehavior { get; init; } = ShelfPeekBehavior.CollapseToEdge;

    /// <summary>Allow restoring the most recently closed shelf item.</summary>
    public bool RestoreEnabled { get; init; } = true;

    /// <summary>
    /// Margin in DIPs between the shelf and the work-area edges. Shares the 12-DIP
    /// edge rhythm the Dock capsule rests on, so shelf and dock sit on one rail
    /// instead of floating at unrelated offsets.
    /// </summary>
    public int MarginDip { get; init; } = 12;

    /// <summary>Maximum number of items stacked on the shelf at once.</summary>
    public int MaxItems { get; init; } = 8;
}

/// <summary>Local history and retention.</summary>
public sealed record HistorySettings
{
    public bool Enabled { get; init; } = true;

    public HistoryRetention Retention { get; init; } = HistoryRetention.ThirtyDays;
}

/// <summary>Clipboard history monitoring and retention. Everything stays local.</summary>
public sealed record ClipboardSettings
{
    /// <summary>
    /// Watch the Windows clipboard and keep a searchable local history. Off on
    /// fresh installs until the user explicitly opts in during first run or Settings.
    /// </summary>
    public bool MonitorEnabled { get; init; }

    /// <summary>Also keep copied images (stored as managed PNG files), not only text.</summary>
    public bool IncludeImages { get; init; } = true;

    /// <summary>Maximum number of clips kept before the oldest non-favorites are removed.</summary>
    public int MaxItems { get; init; } = 500;
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
    public const string ParakeetProvider = "parakeet";
    public const string WhisperProvider = "whisper";
    public const string OpenAiProvider = "openai";
    public const string DefaultProvider = ParakeetProvider;
    public const string DefaultParakeetModel = "parakeet-tdt-0.6b-v3-int8";
    public const string DefaultWhisperModel = "small";
    public const string DefaultLanguage = "";
    public const string DefaultInsertionMode = "paste";
    public const string ActivationModeToggle = "toggle";
    public const string ActivationModeHold = "hold";
    public const string ActivationModeBoth = "both";
    public const string DefaultActivationMode = ActivationModeToggle;

    /// <summary>Speech provider id. Local Parakeet or Whisper.</summary>
    public string Provider { get; init; } = DefaultProvider;

    /// <summary>Parakeet (sherpa-onnx) model variant used by the local default engine.</summary>
    public string ParakeetModel { get; init; } = DefaultParakeetModel;

    /// <summary>Whisper ggml model variant, for example "small", "base.en", or "small.en".</summary>
    public string WhisperModel { get; init; } = DefaultWhisperModel;


    /// <summary>BCP-47-ish language hint, or empty to let the provider auto-detect.</summary>
    public string Language { get; init; } = DefaultLanguage;

    /// <summary>
    /// The Windows capture endpoint id dictation records from, or empty to
    /// use the system default microphone. A stored id that no longer exists
    /// fails the dictation start with an actionable error (never a silent
    /// fallback to a different microphone).
    /// </summary>
    public string MicrophoneDeviceId { get; init; } = string.Empty;

    /// <summary>How recognized text is inserted: "paste" or "clipboard".</summary>
    public string InsertionMode { get; init; } = DefaultInsertionMode;

    /// <summary>
    /// User-defined dictation replacements, one per line. Supported separators
    /// are "=>" and "="; for example: "arrow function => =>".
    /// </summary>
    public string CustomDictionary { get; init; } = string.Empty;

    /// <summary>
    /// How the dictation shortcut behaves: "toggle" (press to start, press to
    /// stop — a registered hotkey), "hold" (push-to-talk via a low-level
    /// keyboard hook), or "both" (hold to talk, tap to toggle).
    /// </summary>
    public string ActivationMode { get; init; } = DefaultActivationMode;

    /// <summary>
    /// Show the live transcript on the dictation pill while speaking (needs a
    /// streaming-capable provider and the local VAD; falls back silently).
    /// </summary>
    public bool LivePartials { get; init; } = true;

    /// <summary>Stop and insert automatically after ~2 s of silence following speech.</summary>
    public bool AutoStopOnSilence { get; init; }

}

/// <summary>Read-aloud configuration.</summary>
public sealed record ReadSettings
{
    public const string WindowsTtsProvider = "windows";
    public const string ElevenLabsTtsProvider = "elevenlabs";
    public const string DefaultTtsProvider = WindowsTtsProvider;
    public const double DefaultRate = 1.0;

    /// <summary>Voice provider id: installed Windows voices.</summary>
    public string TtsProvider { get; init; } = DefaultTtsProvider;

    /// <summary>Preferred voice (display-name fragment or provider voice id); empty = default voice.</summary>
    public string Voice { get; init; } = string.Empty;

    /// <summary>Speaking rate multiplier, clamped to 0.5–3.0.</summary>
    public double Rate { get; init; } = DefaultRate;
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

/// <summary>Global shortcuts. Fresh profiles ship defaults for Area capture, Full screen and Dictate only; every other action ships unassigned (but editable), and stored gestures always win.</summary>
public sealed record ShortcutSettings
{
    public HotkeyGesture CaptureArea { get; init; } = Parse("Ctrl+Shift+4");

    public HotkeyGesture CaptureWindow { get; init; } = HotkeyGesture.None;

    public HotkeyGesture CaptureFullscreen { get; init; } = Parse("Ctrl+Shift+3");

    public HotkeyGesture CapturePreviousArea { get; init; } = HotkeyGesture.None;

    // The AllInOne/HUD shortcut was removed with the HUD; any stored chord stays inert.

    public HotkeyGesture Dictation { get; init; } = Parse("Ctrl+Shift+2");

    public HotkeyGesture Ocr { get; init; } = HotkeyGesture.None;

    public HotkeyGesture Record { get; init; } = HotkeyGesture.None;

    public HotkeyGesture ClipboardHistory { get; init; } = HotkeyGesture.None;

    public HotkeyGesture ReadAloud { get; init; } = HotkeyGesture.None;

    /// <summary>Enumerates each action with its configured gesture.</summary>
    public IEnumerable<(HotkeyAction Action, HotkeyGesture Gesture)> Enumerate()
    {
        yield return (HotkeyAction.CaptureArea, CaptureArea);
        yield return (HotkeyAction.CaptureWindow, CaptureWindow);
        yield return (HotkeyAction.CaptureFullscreen, CaptureFullscreen);
        yield return (HotkeyAction.CapturePreviousArea, CapturePreviousArea);
        // HotkeyAction.AllInOne was removed with the HUD; no gesture is enumerated for it.
        yield return (HotkeyAction.Dictation, Dictation);
        yield return (HotkeyAction.Ocr, Ocr);
        yield return (HotkeyAction.Record, Record);
        yield return (HotkeyAction.ClipboardHistory, ClipboardHistory);
        yield return (HotkeyAction.ReadAloud, ReadAloud);
    }

    private static HotkeyGesture Parse(string chord)
        => HotkeyGesture.TryParse(chord, out HotkeyGesture g) ? g : HotkeyGesture.None;
}

/// <summary>Automation surface toggles.</summary>
public sealed record AutomationSettings
{
    /// <summary>
    /// When true, <c>octadock://</c> URLs may reach the running tray instance.
    /// Defaults off for new profiles so a random site cannot drive capture/OCR/record
    /// until the user explicitly enables protocol automation in Settings.
    /// </summary>
    public bool ProtocolEnabled { get; init; }

    public bool CliEnabled { get; init; } = true;
}

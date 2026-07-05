namespace Octadock.Core.Settings;

/// <summary>
/// The flat, dotted key names under which settings are persisted in the
/// <c>settings</c> table. These are the stable wire keys from the data spec and
/// must not change without a settings migration.
/// </summary>
public static class SettingKeys
{
    // General
    public const string GeneralLaunchAtLogin = "general.launchAtLogin";
    public const string GeneralShowTrayIcon = "general.showTrayIcon";
    public const string GeneralShowTaskbarIcon = "general.showTaskbarIcon";
    public const string GeneralTheme = "general.theme";
    public const string GeneralFirstRunCompleted = "general.firstRunCompleted";
    public const string GeneralCrashReportingEnabled = "general.crashReportingEnabled";

    // Capture
    public const string CaptureDefaultAction = "capture.defaultAction";
    public const string CaptureSaveDirectory = "capture.saveDirectory";
    public const string CaptureFilenameTemplate = "capture.filenameTemplate";
    public const string CaptureIncludeCursor = "capture.includeCursor";
    public const string CaptureWindowShadow = "capture.windowShadow";
    public const string CaptureExcludeOctadockWindows = "capture.excludeOctadockWindows";
    public const string CaptureMultiMonitorMode = "capture.multiMonitorMode";
    public const string CaptureImageFormat = "capture.imageFormat";
    public const string CaptureJpegQuality = "capture.jpegQuality";
    public const string CaptureSelfTimerSeconds = "capture.selfTimerSeconds";
    public const string CaptureFreezeScreen = "capture.freezeScreen";

    // Dock
    public const string DockEnabled = "dock.enabled";
    public const string DockHasCustomAnchor = "dock.hasCustomAnchor";
    public const string DockAnchorX = "dock.anchorX";
    public const string DockAnchorY = "dock.anchorY";

    // Shelf
    public const string ShelfAnchor = "shelf.anchor";
    public const string ShelfSize = "shelf.size";
    public const string ShelfAutoClose = "shelf.autoClose";
    public const string ShelfAutoCloseSecondsLegacy = "shelf.autoCloseSeconds";
    public const string ShelfRestoreEnabled = "shelf.restoreEnabled";
    public const string ShelfMarginDip = "shelf.marginDip";
    public const string ShelfMaxItems = "shelf.maxItems";

    // History
    public const string HistoryEnabled = "history.enabled";
    public const string HistoryRetention = "history.retention";
    public const string HistoryRetentionDaysLegacy = "history.retentionDays";

    // Clipboard history
    public const string ClipboardMonitorEnabled = "clipboard.monitorEnabled";
    public const string ClipboardIncludeImages = "clipboard.includeImages";
    public const string ClipboardMaxItems = "clipboard.maxItems";

    // OCR
    public const string OcrProvider = "ocr.provider";
    public const string OcrOutputMode = "ocr.outputMode";
    public const string OcrPreferredLanguage = "ocr.preferredLanguage";

    // Speech
    public const string SpeechProvider = "speech.provider";
    public const string SpeechParakeetModel = "speech.parakeetModel";
    public const string SpeechWhisperModel = "speech.whisperModel";
    public const string SpeechOpenAiModel = "speech.openAiModel";
    public const string SpeechLanguage = "speech.language";
    public const string SpeechInsertionMode = "speech.insertionMode";
    public const string SpeechCustomDictionary = "speech.customDictionary";
    public const string SpeechActivationMode = "speech.activationMode";
    public const string SpeechLivePartials = "speech.livePartials";
    public const string SpeechAutoStopOnSilence = "speech.autoStopOnSilence";

    // Recording
    public const string RecordingFps = "recording.fps";
    public const string RecordingQuality = "recording.quality";
    public const string RecordingIncludeCursor = "recording.includeCursor";
    public const string RecordingIncludeMicrophone = "recording.includeMicrophone";
    public const string RecordingIncludeSystemAudio = "recording.includeSystemAudio";

    // Shortcuts
    public const string ShortcutCaptureArea = "shortcuts.captureArea";
    public const string ShortcutCaptureWindow = "shortcuts.captureWindow";
    public const string ShortcutCaptureFullscreen = "shortcuts.captureFullscreen";
    public const string ShortcutCapturePreviousArea = "shortcuts.capturePreviousArea";
    public const string ShortcutAllInOne = "shortcuts.allInOne";
    public const string ShortcutDictation = "shortcuts.dictation";
    public const string ShortcutOcr = "shortcuts.ocr";
    public const string ShortcutRecord = "shortcuts.record";
    public const string ShortcutClipboardHistory = "shortcuts.clipboardHistory";
    public const string ShortcutReadAloud = "shortcuts.readAloud";

    // Read aloud
    public const string ReadTtsProvider = "read.ttsProvider";
    public const string ReadVoice = "read.voice";
    public const string ReadRate = "read.rate";

    // Automation
    public const string AutomationProtocolEnabled = "automation.protocolEnabled";
    public const string AutomationCliEnabled = "automation.cliEnabled";

    // Settings schema
    public const string SettingsVersion = "settings.version";
}

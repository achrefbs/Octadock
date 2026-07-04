namespace Octadock.Core.Settings;

/// <summary>Corner the Capture Shelf docks to on its monitor.</summary>
public enum ShelfAnchor
{
    BottomLeft = 0,
    BottomRight,
    TopLeft,
    TopRight,
}

/// <summary>Relative on-screen size of shelf items.</summary>
public enum ShelfSize
{
    Small = 0,
    Medium,
    Large,
}

/// <summary>
/// When the shelf auto-closes an item. Matches the settings options: never,
/// after the user takes an action, or after a fixed delay.
/// </summary>
public enum ShelfAutoCloseMode
{
    Never = 0,
    AfterAction,
    Seconds30,
    Minutes1,
    Minutes5,
}

/// <summary>How long captures are retained before automatic cleanup.</summary>
public enum HistoryRetention
{
    /// <summary>History disabled entirely.</summary>
    Disabled = 0,
    OneDay,
    SevenDays,
    ThirtyDays,
    Forever,
}

/// <summary>Which monitor(s) a fullscreen capture targets.</summary>
public enum MultiMonitorCaptureMode
{
    ActiveMonitor = 0,
    SelectedMonitor,
    AllMonitors,
}

/// <summary>OCR engine selection. Windows.Media.Ocr is the shipped default.</summary>
public enum OcrProvider
{
    WindowsMediaOcr = 0,
    WindowsAiTextRecognition,
    Tesseract,
}

/// <summary>Recording quality preset.</summary>
public enum RecordingQuality
{
    Low = 0,
    Medium,
    High,
}

/// <summary>Requested application theme.</summary>
public enum ThemePreference
{
    System = 0,
    Light,
    Dark,
}

/// <summary>Default still-image encoding for saved captures.</summary>
public enum CaptureImageFormat
{
    Png = 0,
    Jpeg,
}

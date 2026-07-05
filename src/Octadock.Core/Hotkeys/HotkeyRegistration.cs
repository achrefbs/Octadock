namespace Octadock.Core.Hotkeys;

/// <summary>Identifies a logical action a hotkey triggers.</summary>
public enum HotkeyAction
{
    CaptureArea = 0,
    CaptureWindow,
    CaptureFullscreen,
    CapturePreviousArea,
    AllInOne,
    Dictation,
    Ocr,
    Record,
    ClipboardHistory,
    ReadAloud,
}

/// <summary>Outcome of attempting to register a single global hotkey.</summary>
public sealed record HotkeyRegistration(
    HotkeyAction Action,
    HotkeyGesture Gesture,
    bool Success,
    string? Error = null)
{
    /// <summary>True when registration failed because the chord is already taken.</summary>
    public bool IsConflict => !Success;
}

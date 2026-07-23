namespace Octadock.Core.Commands;

/// <summary>
/// The set of automation verbs Octadock understands, shared by the
/// <c>octadock://</c> protocol handler and the <c>octadock.exe</c> CLI.
/// The associated wire tokens are defined in <see cref="CommandTokens"/>.
/// </summary>
public enum CommandType
{
    Unknown = 0,
    // Tombstoned in this version: the all-in-one HUD was removed (see CommandTokens.IsRemoved).
    AllInOne,
    CaptureArea,
    CapturePreviousArea,
    CaptureFullscreen,
    CaptureWindow,
    SelfTimer,
    ScrollingCapture,

    // Removed in this version; parse-recognized tombstones only (see CommandTokens.IsRemoved).
    Pin,
    RecordScreen,
    CaptureText,
    ReadAloud,
    AiActions,
    Dictation,
    OpenAnnotate,
    OpenFromClipboard,
    AddShelfItem,
    Open,
    OpenHistory,
    OpenClipboardHistory,
    OpenContext,
    OpenTextTools,
    RestoreRecentlyClosed,
    ClearHistory,
    OpenSettings,
    Quit,

    /// <summary>Activate a license key on this device (<c>octadock://activate?key=…</c>).</summary>
    Activate,
}

namespace Octadock.Core.Commands;

/// <summary>
/// The set of automation verbs Octadock understands, shared by the
/// <c>octadock://</c> protocol handler and the <c>octadock.exe</c> CLI.
/// The associated wire tokens are defined in <see cref="CommandTokens"/>.
/// </summary>
public enum CommandType
{
    Unknown = 0,
    AllInOne,
    CaptureArea,
    CapturePreviousArea,
    CaptureFullscreen,
    CaptureWindow,
    SelfTimer,
    ScrollingCapture,
    Pin,
    RecordScreen,
    CaptureText,
    ReadAloud,
    Dictation,
    OpenAnnotate,
    OpenFromClipboard,
    AddShelfItem,
    Open,
    OpenHistory,
    OpenClipboardHistory,
    OpenTextTools,
    RestoreRecentlyClosed,
    ClearHistory,
    OpenSettings,
    Quit,
}

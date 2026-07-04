namespace Octadock.Core.Models;

/// <summary>Classifies a capture for filtering and behavior.</summary>
public enum CaptureType
{
    Area = 0,
    Window,
    Fullscreen,
    Scrolling,
    Recording,

    /// <summary>An image that was the source of an OCR text extraction.</summary>
    OcrSource,

    /// <summary>An external file the user dropped onto the shelf / added to history.</summary>
    External,
}

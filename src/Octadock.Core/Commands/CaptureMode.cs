namespace Octadock.Core.Commands;

/// <summary>Capture modes addressable via the <c>mode</c> parameter (the all-in-one HUD that consumed them was removed).</summary>
public enum CaptureMode
{
    Area = 0,
    Window,
    Fullscreen,
    Scrolling,
    Ocr,
    Record,
}

/// <summary>Direction for scrolling capture.</summary>
public enum ScrollDirection
{
    Vertical = 0,
    Horizontal,
}

/// <summary>Output shaping for OCR text.</summary>
public enum OcrTextMode
{
    /// <summary>Collapse whitespace/line breaks into compact text.</summary>
    Compact = 0,

    /// <summary>Preserve line breaks between recognized lines.</summary>
    Lines,

    /// <summary>Attempt to preserve visual layout/columns.</summary>
    Layout,
}

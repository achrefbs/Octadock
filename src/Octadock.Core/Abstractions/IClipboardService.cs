using Octadock.Core.Imaging;

namespace Octadock.Core.Abstractions;

/// <summary>
/// Reads and writes images to the Windows clipboard. Implementations marshal to
/// an STA thread as required and, where useful, additionally place file-drop
/// data so paste targets that prefer files still work.
/// </summary>
public interface IClipboardService
{
    /// <summary>True when the clipboard currently holds a bitmap.</summary>
    bool ContainsImage();

    /// <summary>Places an encoded image on the clipboard as a bitmap, throwing if the clipboard cannot be written.</summary>
    void SetImage(EncodedImage image);

    /// <summary>Places an image file on the clipboard as both a bitmap and a file drop, throwing if the clipboard cannot be written.</summary>
    void SetImageFromFile(string filePath);

    /// <summary>Places a set of file paths on the clipboard as a file-drop list, throwing if the clipboard cannot be written.</summary>
    void SetFileDropList(IEnumerable<string> filePaths);

    /// <summary>Copies plain text to the clipboard (used by OCR), throwing if the clipboard cannot be written.</summary>
    void SetText(string text);

    /// <summary>Reads plain text from the clipboard, or null when the clipboard has no usable text.</summary>
    string? TryGetText();

    /// <summary>Reads the current clipboard bitmap as PNG bytes, or null when absent.</summary>
    EncodedImage? TryGetImage();
}

using System.Windows.Media.Imaging;
using Octadock.Core.Capture;

namespace Octadock.App.Services;

/// <summary>
/// The convenience imaging surface the Capture Shelf, annotation editor and pins
/// consume. A thin, WPF-friendly wrapper over the Core <c>IImageEncoder</c>: load
/// a file or convert a captured frame to a <see cref="BitmapSource"/>, and encode
/// a bitmap back to PNG bytes (for clipboard, drag-drop and export).
/// </summary>
public interface IImageLoadService
{
    /// <summary>Loads an image file into a frozen, thread-safe bitmap (no file handle retained).</summary>
    BitmapSource LoadFromFile(string absolutePath);

    /// <summary>Converts a captured frame's BGRA pixels into a frozen bitmap.</summary>
    BitmapSource ToBitmapSource(CapturedFrame frame);

    /// <summary>Encodes a bitmap to PNG bytes.</summary>
    byte[] EncodePng(BitmapSource image);
}

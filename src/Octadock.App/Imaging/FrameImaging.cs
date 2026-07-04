using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Octadock.Core.Capture;

namespace Octadock.App.Imaging;

/// <summary>
/// Shared helpers that turn a Core <see cref="CapturedFrame"/> (a raw BGRA/PBGRA
/// pixel buffer with an arbitrary row stride) into a WPF <see cref="BitmapSource"/>,
/// and encode any <see cref="BitmapSource"/> to PNG/JPEG bytes. Centralized so the
/// encoder, thumbnail generator and image-load service all agree on pixel handling.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FrameImaging
{
    /// <summary>
    /// Builds a frozen <see cref="BitmapSource"/> from a captured frame. The result
    /// is a 96-DPI image sized in pixels; DPI/monitor scaling is applied by callers
    /// when placing it on screen. Frozen so it can cross threads safely.
    /// </summary>
    public static BitmapSource ToBitmapSource(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        PixelFormat format = frame.Format == FramePixelFormat.Pbgra32
            ? PixelFormats.Pbgra32
            : PixelFormats.Bgra32;

        // Copy the pixel span into a managed array (BitmapSource.Create needs an
        // array/pointer; ReadOnlyMemory cannot be pinned as-is here).
        ReadOnlySpan<byte> src = frame.Pixels.Span;
        byte[] buffer = src.ToArray();

        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            format,
            palette: null,
            buffer,
            frame.Stride);

        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Encodes a bitmap to PNG bytes.</summary>
    public static byte[] EncodePng(BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(EnsureFrozen(image)));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Encodes a bitmap to JPEG bytes at the given quality (1-100).</summary>
    public static byte[] EncodeJpeg(BitmapSource image, int quality)
    {
        ArgumentNullException.ThrowIfNull(image);

        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(EnsureFrozen(image)));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Loads an image file fully into memory (OnLoad) and freezes it so no file
    /// handle is retained and the bitmap is thread-safe.
    /// </summary>
    public static BitmapSource LoadFromFile(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            throw new ArgumentException("Path is required.", nameof(absolutePath));
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        // IgnoreImageCache: WPF caches decoded bitmaps per URI, so after a shelf
        // transform rewrites the original/thumbnail at the same path a plain
        // reload would return the stale pixels. Bypass the URI cache to reload.
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(absolutePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource EnsureFrozen(BitmapSource image)
    {
        if (image.IsFrozen)
        {
            return image;
        }

        BitmapSource clone = image.Clone();
        clone.Freeze();
        return clone;
    }
}

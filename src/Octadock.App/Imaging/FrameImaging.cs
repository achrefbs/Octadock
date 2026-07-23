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
    internal const long MaxCompressedFileBytes = 64L * 1024 * 1024;
    internal const int MaxPixelDimension = 32_768;
    internal const long MaxDecodedPixels = 100_000_000;
    internal const long MaxDecodedBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Builds a frozen <see cref="BitmapSource"/> from a captured frame. The result
    /// is a 96-DPI image sized in pixels; DPI/monitor scaling is applied by callers
    /// when placing it on screen. Frozen so it can cross threads safely.
    /// </summary>
    public static BitmapSource ToBitmapSource(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        ValidateDimensions(frame.Width, frame.Height);

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

        using FileStream stream = OpenImageFile(absolutePath);
        ValidateCompressedLength(stream.Length);
        InspectDecoder(stream, absolutePath);
        stream.Position = 0;

        try
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            BitmapFrame frame = ValidateDecoder(decoder, absolutePath);
            frame.Freeze();
            return frame;
        }
        catch (ImageDecodeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw MapDecodeFailure(absolutePath, ex);
        }
    }

    /// <summary>Decodes clipboard/managed bytes through the same decoded-memory guardrails.</summary>
    internal static BitmapSource LoadFromBytes(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ImageDecodeException(
                ImageFailureKind.Empty,
                ImageFailureCopy.For(ImageFailureKind.Empty));
        }

        if (bytes.Length > MaxCompressedFileBytes)
        {
            throw new ImageDecodeException(
                ImageFailureKind.TooLarge,
                ImageFailureCopy.For(ImageFailureKind.TooLarge));
        }

        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        try
        {
            BitmapDecoder metadata = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);
            _ = ValidateDecoder(metadata, path: null);
            stream.Position = 0;
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            BitmapFrame frame = ValidateDecoder(decoder, path: null);
            frame.Freeze();
            return frame;
        }
        catch (ImageDecodeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw MapDecodeFailure(path: null, ex);
        }
    }

    internal static void ValidateDimensions(int width, int height, int bitsPerPixel = 32)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ImageDecodeException(
                ImageFailureKind.Malformed,
                ImageFailureCopy.For(ImageFailureKind.Malformed));
        }

        long pixels;
        try
        {
            pixels = checked((long)width * height);
        }
        catch (OverflowException ex)
        {
            throw new ImageDecodeException(
                ImageFailureKind.TooLarge,
                ImageFailureCopy.For(ImageFailureKind.TooLarge),
                ex);
        }

        long decodedBytes;
        try
        {
            int boundedBitsPerPixel = Math.Max(32, bitsPerPixel);
            decodedBytes = checked((pixels * boundedBitsPerPixel + 7) / 8);
        }
        catch (OverflowException ex)
        {
            throw new ImageDecodeException(
                ImageFailureKind.TooLarge,
                ImageFailureCopy.For(ImageFailureKind.TooLarge),
                ex);
        }
        if (width > MaxPixelDimension ||
            height > MaxPixelDimension ||
            pixels > MaxDecodedPixels ||
            decodedBytes > MaxDecodedBytes)
        {
            throw new ImageDecodeException(
                ImageFailureKind.TooLarge,
                "This image exceeds Octadock's safe decoded-pixel limit.");
        }
    }

    private static ImageSourceInfo InspectDecoder(Stream stream, string? path)
    {
        try
        {
            ValidateOptionalCodecSignature(stream, path);
            stream.Position = 0;
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);
            BitmapFrame frame = ValidateDecoder(decoder, path);
            return new ImageSourceInfo(frame.PixelWidth, frame.PixelHeight, stream.Length);
        }
        catch (ImageDecodeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw MapDecodeFailure(path, ex);
        }
    }

    private static BitmapFrame FirstFrame(BitmapDecoder decoder, string? path)
    {
        if (decoder.Frames.Count == 0)
        {
            throw new ImageDecodeException(
                ImageFailureKind.Malformed,
                ImageFailureCopy.For(ImageFailureKind.Malformed));
        }

        return decoder.Frames[0];
    }

    private static BitmapFrame ValidateDecoder(BitmapDecoder decoder, string? path)
    {
        BitmapFrame first = FirstFrame(decoder, path);
        long totalPixels = 0;
        long totalDecodedBytes = 0;
        foreach (BitmapFrame frame in decoder.Frames)
        {
            int bitsPerPixel = Math.Max(32, frame.Format.BitsPerPixel);
            ValidateDimensions(frame.PixelWidth, frame.PixelHeight, bitsPerPixel);
            try
            {
                long pixels = checked((long)frame.PixelWidth * frame.PixelHeight);
                totalPixels = checked(totalPixels + pixels);
                totalDecodedBytes = checked(totalDecodedBytes + ((pixels * bitsPerPixel + 7) / 8));
            }
            catch (OverflowException ex)
            {
                throw new ImageDecodeException(
                    ImageFailureKind.TooLarge,
                    ImageFailureCopy.For(ImageFailureKind.TooLarge),
                    ex);
            }

            if (totalPixels > MaxDecodedPixels || totalDecodedBytes > MaxDecodedBytes)
            {
                throw new ImageDecodeException(
                    ImageFailureKind.TooLarge,
                    "This image exceeds Octadock's safe decoded-memory limit.");
            }
        }

        return first;
    }

    private static FileStream OpenImageFile(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            // Keep the validated compressed source immutable through WIC's
            // metadata and OnLoad decode passes. A concurrent writer receives a
            // sharing violation and the caller presents a stable Busy state.
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

    private static void ValidateCompressedLength(long length)
    {
        if (length == 0)
        {
            throw new ImageDecodeException(
                ImageFailureKind.Empty,
                ImageFailureCopy.For(ImageFailureKind.Empty));
        }

        if (length > MaxCompressedFileBytes)
        {
            throw new ImageDecodeException(
                ImageFailureKind.TooLarge,
                "This image is too large to open safely.");
        }
    }

    internal static ImageDecodeException MapDecodeFailure(string? path, Exception exception)
    {
        ImageFailureKind kind = exception switch
        {
            FileNotFoundException or DirectoryNotFoundException => ImageFailureKind.NotFound,
            UnauthorizedAccessException => ImageFailureKind.AccessDenied,
            IOException io when (io.HResult & 0xFFFF) is 32 or 33 => ImageFailureKind.Busy,
            _ when RequiresOptionalCodec(path) && ContainsCodecComponentMissing(exception) =>
                ImageFailureKind.CodecUnavailable,
            _ => ImageFailureKind.Malformed,
        };
        return new ImageDecodeException(kind, ImageFailureCopy.For(kind), exception);
    }

    private static bool RequiresOptionalCodec(string? path)
        => Path.GetExtension(path ?? string.Empty).ToLowerInvariant() is
            ".avif" or ".heic" or ".heif" or ".webp" or ".jxr" or ".wdp";

    private static void ValidateOptionalCodecSignature(Stream stream, string? path)
    {
        string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        if (!RequiresOptionalCodec(path))
        {
            return;
        }

        Span<byte> header = stackalloc byte[16];
        stream.Position = 0;
        int read = 0;
        while (read < header.Length)
        {
            int count = stream.Read(header[read..]);
            if (count == 0)
            {
                break;
            }

            read += count;
        }
        bool plausible = extension switch
        {
            ".webp" => read >= 12 &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".avif" or ".heic" or ".heif" => read >= 12 &&
                header.Slice(4, 4).SequenceEqual("ftyp"u8),
            ".jxr" or ".wdp" => read >= 4 &&
                ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0xBC && header[3] == 0x01) ||
                 (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x01 && header[3] == 0xBC)),
            _ => true,
        };
        if (!plausible)
        {
            throw new ImageDecodeException(
                ImageFailureKind.Malformed,
                ImageFailureCopy.For(ImageFailureKind.Malformed));
        }
    }

    private static bool ContainsCodecComponentMissing(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            // WINCODEC_ERR_COMPONENTNOTFOUND may be wrapped by WPF in a
            // FileFormatException or another managed exception. Preserve the
            // HRESULT check so corrupt optional-format files are not mislabeled.
            if (unchecked((uint)current.HResult) == 0x88982F50)
            {
                return true;
            }
        }

        return false;
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

internal sealed record ImageSourceInfo(int PixelWidth, int PixelHeight, long CompressedBytes);

internal sealed class ImageDecodeException : IOException
{
    public ImageDecodeException(
        ImageFailureKind failureKind,
        string productMessage,
        Exception? innerException = null)
        : base(productMessage, innerException)
    {
        FailureKind = failureKind;
    }

    public ImageFailureKind FailureKind { get; }
}

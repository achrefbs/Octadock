using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Imaging;

namespace Octadock.App.Imaging;

/// <summary>
/// <see cref="IImageEncoder"/> implemented with WPF's <see cref="BitmapEncoder"/>
/// family. Converts a captured BGRA frame to a <see cref="BitmapSource"/> and
/// encodes it to PNG or JPEG. WebP/BMP fall back to PNG (P0 scope).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpfImageEncoder : IImageEncoder
{
    private readonly ILogger<WpfImageEncoder> _logger;

    /// <summary>Creates the WPF image encoder.</summary>
    public WpfImageEncoder(ILogger<WpfImageEncoder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public EncodedImage Encode(CapturedFrame frame, EncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);

        BitmapSource bitmap = FrameImaging.ToBitmapSource(frame);
        return Encode(bitmap, options);
    }

    /// <summary>Encodes an already-decoded bitmap (used by the shelf/editor pipelines).</summary>
    public EncodedImage Encode(BitmapSource bitmap, EncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(options);

        switch (options.Format)
        {
            case ExportImageFormat.Jpeg:
                return new EncodedImage(FrameImaging.EncodeJpeg(bitmap, options.Quality), ExportImageFormat.Jpeg);

            case ExportImageFormat.Png:
                return new EncodedImage(FrameImaging.EncodePng(bitmap), ExportImageFormat.Png);

            default:
                _logger.LogDebug("Format {Format} not natively supported by WPF encoder; emitting PNG.", options.Format);
                return new EncodedImage(FrameImaging.EncodePng(bitmap), ExportImageFormat.Png);
        }
    }

    /// <inheritdoc />
    public async Task EncodeToFileAsync(CapturedFrame frame, string path, EncodeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        // Encode on the calling (worker) thread — BitmapSource work must not block
        // the UI thread and does not require it once the frame is a plain buffer.
        EncodedImage encoded = Encode(frame, options);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            useAsync: true);

        await stream.WriteAsync(encoded.Bytes, cancellationToken).ConfigureAwait(false);
    }
}

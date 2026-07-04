using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Imaging;

namespace Octadock.App.Imaging;

/// <summary>
/// <see cref="IThumbnailGenerator"/> using WPF imaging. Downscales captures/files
/// so the longest edge is at most <see cref="MaxEdge"/> and emits JPEG thumbnails
/// (the storage convention; see <c>IStoragePaths.BuildThumbnailRelativePath</c>).
/// Decodes files at reduced resolution via <c>DecodePixelWidth</c> to keep memory
/// low.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpfThumbnailGenerator : IThumbnailGenerator
{
    private const int ThumbnailJpegQuality = 82;

    private readonly ILogger<WpfThumbnailGenerator> _logger;

    /// <summary>Creates the thumbnail generator.</summary>
    public WpfThumbnailGenerator(ILogger<WpfThumbnailGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int MaxEdge => 480;

    /// <inheritdoc />
    public EncodedImage Generate(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        BitmapSource source = FrameImaging.ToBitmapSource(frame);
        BitmapSource scaled = Downscale(source);
        return new EncodedImage(FrameImaging.EncodeJpeg(scaled, ThumbnailJpegQuality), ExportImageFormat.Jpeg);
    }

    /// <inheritdoc />
    public async Task GenerateToFileAsync(string sourceImagePath, string thumbnailPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceImagePath))
        {
            throw new ArgumentException("Source path is required.", nameof(sourceImagePath));
        }

        if (string.IsNullOrWhiteSpace(thumbnailPath))
        {
            throw new ArgumentException("Thumbnail path is required.", nameof(thumbnailPath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        byte[] jpeg = DecodeAndScaleFile(sourceImagePath);

        string? directory = Path.GetDirectoryName(thumbnailPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            thumbnailPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            useAsync: true);

        await stream.WriteAsync(jpeg, cancellationToken).ConfigureAwait(false);
    }

    private byte[] DecodeAndScaleFile(string sourceImagePath)
    {
        try
        {
            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            // Decode straight to (roughly) thumbnail resolution to avoid loading
            // full 8K frames into memory. Height follows aspect automatically.
            decoded.DecodePixelWidth = MaxEdge;
            decoded.UriSource = new Uri(sourceImagePath, UriKind.Absolute);
            decoded.EndInit();
            decoded.Freeze();

            BitmapSource scaled = Downscale(decoded);
            return FrameImaging.EncodeJpeg(scaled, ThumbnailJpegQuality);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate a thumbnail for {Path}.", sourceImagePath);
            throw;
        }
    }

    private BitmapSource Downscale(BitmapSource source)
    {
        int longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest <= MaxEdge || longest == 0)
        {
            if (source.IsFrozen)
            {
                return source;
            }

            BitmapSource frozen = source.Clone();
            frozen.Freeze();
            return frozen;
        }

        double scale = (double)MaxEdge / longest;
        var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }
}

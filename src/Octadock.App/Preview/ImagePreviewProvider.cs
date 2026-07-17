using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Preview;

/// <summary>
/// <see cref="IFilePreviewProvider"/> for raster image files supported by
/// <see cref="ImageFileSupport"/>.
/// This provider lives in the App project (not Core) so Core stays WPF-free: it
/// only validates that the file exists and is within a sane size, then returns a
/// <see cref="FilePreviewKind.Image"/> result carrying the path. The actual
/// decoding happens in <see cref="PreviewCardWindow"/> using WPF imaging.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ImagePreviewProvider : IFilePreviewProvider
{
    /// <summary>Hard cap on image size; larger files are rejected rather than decoded.</summary>
    private const long MaxBytes = 64 * 1024 * 1024;

    /// <inheritdoc />
    public bool CanPreview(string extension)
        => ImageFileSupport.IsSupportedRasterExtension(extension);

    /// <inheritdoc />
    public Task<FilePreviewResult> LoadAsync(
        string path, FilePreviewOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return Task.FromResult(FilePreviewResult.Fail(path, "The file could not be found."));
            }

            if (info.Length == 0)
            {
                return Task.FromResult(FilePreviewResult.Fail(path, "The image is empty."));
            }

            if (info.Length > MaxBytes)
            {
                return Task.FromResult(FilePreviewResult.Fail(path, "The image is too large to preview."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            (int? width, int? height) = ReadSourceDimensions(path);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new FilePreviewResult
            {
                Kind = FilePreviewKind.Image,
                FilePath = path,
                ImagePath = path,
                ImagePixelWidth = width,
                ImagePixelHeight = height,
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(FilePreviewResult.Fail(path, ex.Message));
        }
    }

    private static (int? Width, int? Height) ReadSourceDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.None);
            BitmapFrame frame = decoder.Frames[0];
            return frame.PixelWidth > 0 && frame.PixelHeight > 0
                ? (frame.PixelWidth, frame.PixelHeight)
                : (null, null);
        }
        catch (Exception)
        {
            // The existing bounded display decode remains authoritative. Metadata
            // failure must not turn a previewable image into an open failure.
            return (null, null);
        }
    }
}

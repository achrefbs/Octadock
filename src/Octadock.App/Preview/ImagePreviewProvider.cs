using System.IO;
using System.Runtime.Versioning;
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

            return Task.FromResult(new FilePreviewResult
            {
                Kind = FilePreviewKind.Image,
                FilePath = path,
                ImagePath = path,
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
}

using System.IO;
using System.Runtime.Versioning;
using Octadock.App.Imaging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Preview;

/// <summary>Validates compressed and decoded image resource bounds before the Pin route decodes it.</summary>
[SupportedOSPlatform("windows")]
public sealed class ImagePreviewProvider : IFilePreviewProvider
{
    public bool CanPreview(string extension)
        => ImageFileSupport.IsSupportedRasterExtension(extension);

    public Task<FilePreviewResult> LoadAsync(
        string path,
        FilePreviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ImageSourceInfo info = FrameImaging.InspectFile(path);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new FilePreviewResult
            {
                Kind = FilePreviewKind.Image,
                FilePath = path,
                ImagePath = path,
                ImagePixelWidth = info.PixelWidth,
                ImagePixelHeight = info.PixelHeight,
                SourceByteLength = info.CompressedBytes,
                Scope = new PreviewContentScope
                {
                    StartByte = 0,
                    EndByteExclusive = info.CompressedBytes,
                    Label = "Complete image",
                },
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ImagePreviewException ex)
        {
            long? sourceLength = null;
            try
            {
                sourceLength = new FileInfo(path).Length;
            }
            catch (Exception)
            {
            }

            return Task.FromResult(FilePreviewResult.Fail(path, ex.FailureKind, sourceLength));
        }
        catch (FileNotFoundException)
        {
            return Task.FromResult(FilePreviewResult.Fail(path, FilePreviewFailureKind.NotFound));
        }
        catch (DirectoryNotFoundException)
        {
            return Task.FromResult(FilePreviewResult.Fail(path, FilePreviewFailureKind.NotFound));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(FilePreviewResult.Fail(path, FilePreviewFailureKind.AccessDenied));
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            return Task.FromResult(FilePreviewResult.Fail(path, FilePreviewFailureKind.Busy));
        }
        catch (Exception)
        {
            return Task.FromResult(FilePreviewResult.Fail(path, FilePreviewFailureKind.Malformed));
        }
    }
}

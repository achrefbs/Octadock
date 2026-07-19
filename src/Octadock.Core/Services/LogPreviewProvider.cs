using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>Shows a bounded, code-point-safe tail of a log without locking live writers.</summary>
public sealed class LogPreviewProvider : IFilePreviewProvider
{
    internal const int MaxBytes = 256 * 1024;

    public int Priority => 1;

    public bool CanPreview(string extension)
        => extension is ".log";

    public async Task<FilePreviewResult> LoadAsync(
        string path,
        FilePreviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            PreviewTextReadResult read = await PreviewTextReader
                .ReadTailAsync(path, MaxBytes, cancellationToken)
                .ConfigureAwait(false);
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.PlainText,
                FilePath = path,
                SourceContent = read.Text,
                SourceByteLength = read.SourceByteLength,
                DetectedEncoding = read.DetectedEncoding,
                Scope = read.Scope,
                Warnings = read.Warnings,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return PreviewFailureMapper.FromException(path, ex);
        }
    }
}

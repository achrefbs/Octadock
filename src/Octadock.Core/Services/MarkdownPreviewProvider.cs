using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>Returns clean bounded Markdown source for App-layer rendering.</summary>
public sealed class MarkdownPreviewProvider : IFilePreviewProvider
{
    internal const int MaxBytes = 256 * 1024;

    public int Priority => 1;

    public bool CanPreview(string extension)
        => extension is ".md" or ".markdown";

    public async Task<FilePreviewResult> LoadAsync(
        string path,
        FilePreviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            PreviewTextReadResult read = await PreviewTextReader
                .ReadHeadAsync(path, MaxBytes, cancellationToken)
                .ConfigureAwait(false);
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Markdown,
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

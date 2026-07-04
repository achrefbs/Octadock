using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// <see cref="IFilePreviewProvider"/> for Markdown. Reads the source (capped)
/// and returns <see cref="FilePreviewKind.Markdown"/>; the App-layer card
/// renders headings, lists, code blocks, quotes, and inline emphasis. Wins over
/// the plain text provider via priority.
/// </summary>
public sealed class MarkdownPreviewProvider : IFilePreviewProvider
{
    /// <summary>Maximum number of characters rendered.</summary>
    internal const int MaxBytes = 256 * 1024;

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanPreview(string extension)
        => extension is ".md" or ".markdown";

    /// <inheritdoc />
    public async Task<FilePreviewResult> LoadAsync(
        string path, FilePreviewOptions options, CancellationToken cancellationToken)
    {
        try
        {
            (string text, bool truncated) =
                await PreviewTextReader.ReadHeadAsync(path, MaxBytes, cancellationToken).ConfigureAwait(false);

            if (truncated)
            {
                text += "\n\n… *(truncated — showing the first 256 KB)*";
            }

            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Markdown,
                FilePath = path,
                Text = text,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FilePreviewResult.Fail(path, ex.Message);
        }
    }
}

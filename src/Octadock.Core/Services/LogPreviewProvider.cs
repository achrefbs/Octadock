using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// <see cref="IFilePreviewProvider"/> for log files. Unlike the generic text
/// provider (which shows the head of a file), logs preview their TAIL — the
/// newest entries are what a developer opens a log for. Live logs are never
/// locked. Wins over the plain text provider via priority.
/// </summary>
public sealed class LogPreviewProvider : IFilePreviewProvider
{
    /// <summary>Size of the tail window shown for large logs.</summary>
    internal const int MaxBytes = 256 * 1024;

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanPreview(string extension)
        => extension is ".log";

    /// <inheritdoc />
    public async Task<FilePreviewResult> LoadAsync(
        string path, FilePreviewOptions options, CancellationToken cancellationToken)
    {
        try
        {
            (string text, bool truncated, long fileBytes) =
                await PreviewTextReader.ReadTailAsync(path, MaxBytes, cancellationToken).ConfigureAwait(false);

            string body = truncated
                ? $"… (showing the last 256 KB of a {PreviewTextReader.FormatSize(fileBytes)} log)\n\n" + text
                : text;

            return new FilePreviewResult
            {
                Kind = FilePreviewKind.PlainText,
                FilePath = path,
                Text = body,
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

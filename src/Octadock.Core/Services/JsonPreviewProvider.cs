using System.Text.Json;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// <see cref="IFilePreviewProvider"/> for JSON: pretty-prints valid documents
/// (accepting comments and trailing commas, so config-style JSON works) and
/// falls back to the raw text with a trailing note when the file is invalid or
/// too large to format. Wins over the plain text provider via priority.
/// </summary>
public sealed class JsonPreviewProvider : IFilePreviewProvider
{
    /// <summary>Files larger than this are shown raw instead of being parsed.</summary>
    internal const int MaxFormatBytes = 2 * 1024 * 1024;

    /// <summary>Raw fallback window when the file is invalid or oversized.</summary>
    private const int MaxRawBytes = 256 * 1024;

    /// <inheritdoc />
    public int Priority => 1;

    /// <inheritdoc />
    public bool CanPreview(string extension)
        => extension is ".json" or ".jsonc";

    /// <inheritdoc />
    public async Task<FilePreviewResult> LoadAsync(
        string path, FilePreviewOptions options, CancellationToken cancellationToken)
    {
        try
        {
            (string text, bool truncated) =
                await PreviewTextReader.ReadHeadAsync(path, MaxFormatBytes, cancellationToken).ConfigureAwait(false);

            if (truncated)
            {
                string window = text.Length > MaxRawBytes ? text[..MaxRawBytes] : text;
                return Text(path, window + "\n\n… (too large to format as JSON — showing the first 256 KB)");
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });
                string pretty = JsonSerializer.Serialize(
                    doc.RootElement,
                    new JsonSerializerOptions { WriteIndented = true });
                return Text(path, pretty);
            }
            catch (JsonException ex)
            {
                string window = text.Length > MaxRawBytes
                    ? text[..MaxRawBytes] + "\n\n… (truncated — showing the first 256 KB)"
                    : text;
                string location = ex.LineNumber is long line
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $" at line {line + 1}")
                    : string.Empty;
                return Text(path, window + $"\n\n… (shown as-is — not valid JSON{location})");
            }
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

    private static FilePreviewResult Text(string path, string body)
        => new() { Kind = FilePreviewKind.PlainText, FilePath = path, Text = body };
}

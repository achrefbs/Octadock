using System.IO;
using System.Text;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// <see cref="IFilePreviewProvider"/> for plain-text-ish files: documents
/// (<c>.txt</c>/<c>.log</c>/<c>.md</c>/<c>.json</c>), config
/// (<c>.xml</c>/<c>.yaml</c>/<c>.toml</c>/<c>.ini</c>/…), and source code
/// (<c>.cs</c>/<c>.js</c>/<c>.py</c>/…). Reads up to <see cref="MaxBytes"/> and
/// renders the body verbatim; larger files are truncated with a trailing note.
/// Later phases add JSON/Markdown rendering.
/// </summary>
public sealed class TextPreviewProvider : IFilePreviewProvider
{
    /// <summary>Maximum number of bytes read before the body is truncated.</summary>
    private const int MaxBytes = 256 * 1024;

    /// <summary>
    /// The plain-text-ish extensions this provider claims (with dot, lowercase).
    /// Files with no extension are deliberately not matched.
    /// </summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents.
        ".txt", ".log", ".md", ".json",

        // Config / markup.
        ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".css", ".html", ".htm",

        // Source code.
        ".cs", ".js", ".ts", ".jsx", ".tsx", ".py", ".rb", ".go", ".rs", ".java",
        ".c", ".cpp", ".h", ".sql", ".sh", ".ps1", ".bat",

        // Project / tooling files.
        ".csproj", ".sln", ".gitignore",
    };

    /// <inheritdoc />
    public bool CanPreview(string extension)
        => !string.IsNullOrEmpty(extension) && TextExtensions.Contains(extension);

    /// <inheritdoc />
    public async Task<FilePreviewResult> LoadAsync(
        string path, FilePreviewOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            // ReadWrite|Delete share: never lock a log another process is tailing.
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024, useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            // Read one byte past the cap so we can tell "exactly MaxBytes" from
            // "larger than MaxBytes and therefore truncated".
            var window = new char[MaxBytes + 1];
            int total = 0;
            while (total < window.Length)
            {
                int n = await reader
                    .ReadAsync(window.AsMemory(total, window.Length - total), cancellationToken)
                    .ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }

                total += n;
            }

            bool truncated = total > MaxBytes;
            int take = truncated ? MaxBytes : total;
            var text = new StringBuilder(take + 64);
            text.Append(window, 0, take);
            if (truncated)
            {
                text.Append("\n\n… (truncated — showing the first 256 KB)");
            }

            return new FilePreviewResult
            {
                Kind = FilePreviewKind.PlainText,
                FilePath = path,
                Text = text.ToString(),
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

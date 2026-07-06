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
        ".txt", ".text", ".log", ".md", ".markdown", ".mkd", ".mdown", ".mdx",
        ".rst", ".adoc", ".asciidoc", ".tex", ".bib", ".json",

        // Config / markup.
        ".xml", ".xhtml", ".xsd", ".xsl", ".xslt", ".dtd", ".svg", ".rss", ".atom",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".properties",
        ".css", ".scss", ".sass", ".less", ".styl", ".html", ".htm",
        ".jsonc", ".json5", ".jsonl", ".ndjson", ".env", ".editorconfig",
        ".tf", ".tfvars", ".hcl", ".proto", ".graphql", ".gql", ".prisma", ".plist",

        // Source code.
        ".cs", ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts", ".jsx", ".tsx",
        ".vue", ".svelte", ".astro", ".py", ".pyi", ".rb", ".go", ".rs", ".java",
        ".kt", ".kts", ".swift", ".scala", ".dart", ".php", ".pl", ".pm", ".lua",
        ".r", ".jl", ".ex", ".exs", ".erl", ".hs", ".ml", ".mli", ".fs", ".fsx",
        ".clj", ".cljs", ".groovy", ".vb", ".nim", ".zig", ".coffee", ".elm",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".m", ".mm",
        ".sql", ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".psd1",
        ".bat", ".cmd", ".awk",

        // Project / tooling files.
        ".csproj", ".fsproj", ".vbproj", ".sln", ".props", ".targets",
        ".gradle", ".cmake", ".mk", ".mak", ".nix", ".bicep",
        ".gitignore", ".gitattributes", ".dockerignore", ".patch", ".diff", ".lock",
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

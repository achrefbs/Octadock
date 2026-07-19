using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>Bounded strict-text provider for common document, config, and source extensions.</summary>
public sealed class TextPreviewProvider : IFilePreviewProvider
{
    internal const int MaxBytes = 256 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".log", ".md", ".markdown", ".mkd", ".mdown", ".mdx",
        ".rst", ".adoc", ".asciidoc", ".tex", ".bib", ".json",
        ".xml", ".xhtml", ".xsd", ".xsl", ".xslt", ".dtd", ".svg", ".rss", ".atom",
        ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".properties",
        ".css", ".scss", ".sass", ".less", ".styl", ".html", ".htm",
        ".jsonc", ".json5", ".jsonl", ".ndjson", ".env", ".editorconfig",
        ".tf", ".tfvars", ".hcl", ".proto", ".graphql", ".gql", ".prisma", ".plist",
        ".cs", ".js", ".mjs", ".cjs", ".ts", ".mts", ".cts", ".jsx", ".tsx",
        ".vue", ".svelte", ".astro", ".py", ".pyi", ".rb", ".go", ".rs", ".java",
        ".kt", ".kts", ".swift", ".scala", ".dart", ".php", ".pl", ".pm", ".lua",
        ".r", ".jl", ".ex", ".exs", ".erl", ".hs", ".ml", ".mli", ".fs", ".fsx",
        ".clj", ".cljs", ".groovy", ".vb", ".nim", ".zig", ".coffee", ".elm",
        ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh", ".m", ".mm",
        ".sql", ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".psd1",
        ".bat", ".cmd", ".awk",
        ".csproj", ".fsproj", ".vbproj", ".sln", ".props", ".targets",
        ".gradle", ".cmake", ".mk", ".mak", ".nix", ".bicep",
        ".gitignore", ".gitattributes", ".dockerignore", ".patch", ".diff", ".lock",
    };

    public bool CanPreview(string extension)
        => !string.IsNullOrEmpty(extension) && TextExtensions.Contains(extension);

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

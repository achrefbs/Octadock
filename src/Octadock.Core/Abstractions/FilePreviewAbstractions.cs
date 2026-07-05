namespace Octadock.Core.Abstractions;

/// <summary>
/// One provider per previewable file type. Providers parse into a
/// <see cref="FilePreviewResult"/> off the UI thread; the App-layer card window
/// decides how to render each result kind.
/// </summary>
public interface IFilePreviewProvider
{
    /// <summary>Provider priority when several claim an extension (higher wins).</summary>
    int Priority => 0;

    /// <summary>True when this provider can preview the given extension (".csv", with dot, lowercase).</summary>
    bool CanPreview(string extension);

    /// <summary>
    /// Loads the preview model. Must stream/cap its own reads (see
    /// <see cref="FilePreviewOptions.MaxImmediateRows"/>) so a huge file never
    /// blocks the open animation.
    /// </summary>
    Task<FilePreviewResult> LoadAsync(string path, FilePreviewOptions options, CancellationToken cancellationToken);
}

/// <summary>Load-time knobs, from settings with sane defaults.</summary>
public sealed record FilePreviewOptions
{
    /// <summary>Rows parsed before the card first renders; the rest load in the background.</summary>
    public int MaxImmediateRows { get; init; } = 500;

    /// <summary>Hard cap on rows kept in memory (guardrail for 1M-row files).</summary>
    public int MaxTotalRows { get; init; } = 250_000;

    /// <summary>Explicit delimiter override; null = auto-detect (',' ';' '\t' '|').</summary>
    public char? Delimiter { get; init; }
}

/// <summary>
/// Discriminated result. Exactly one payload is non-null; <see cref="Kind"/> says which.
/// </summary>
public sealed record FilePreviewResult
{
    /// <summary>Which payload is populated.</summary>
    public required FilePreviewKind Kind { get; init; }

    /// <summary>The full path that was previewed.</summary>
    public required string FilePath { get; init; }

    /// <summary>Human-readable failure reason when <see cref="Kind"/> is <see cref="FilePreviewKind.Error"/>.</summary>
    public string? Error { get; init; }

    /// <summary>The parsed table when <see cref="Kind"/> is <see cref="FilePreviewKind.Csv"/>.</summary>
    public CsvPreviewModel? Csv { get; init; }

    /// <summary>
    /// The text body when <see cref="Kind"/> is <see cref="FilePreviewKind.PlainText"/>,
    /// or the "Name / Size / Created / Modified / Full path" lines when
    /// <see cref="Kind"/> is <see cref="FilePreviewKind.FileInfo"/>.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// The full path to the image on disk when <see cref="Kind"/> is
    /// <see cref="FilePreviewKind.Image"/>. Decoding happens in the App-layer card
    /// so Core stays WPF-free.
    /// </summary>
    public string? ImagePath { get; init; }

    /// <summary>Builds an error result for <paramref name="path"/> carrying <paramref name="error"/>.</summary>
    public static FilePreviewResult Fail(string path, string error)
        => new() { Kind = FilePreviewKind.Error, FilePath = path, Error = error };
}

/// <summary>The kind of payload carried by a <see cref="FilePreviewResult"/>.</summary>
public enum FilePreviewKind
{
    /// <summary>The preview failed; see <see cref="FilePreviewResult.Error"/>.</summary>
    Error = 0,

    /// <summary>A parsed CSV/TSV table; see <see cref="FilePreviewResult.Csv"/>.</summary>
    Csv,

    /// <summary>A plain-text body; see <see cref="FilePreviewResult.Text"/>.</summary>
    PlainText,

    /// <summary>An image to decode and display; see <see cref="FilePreviewResult.ImagePath"/>.</summary>
    Image,

    /// <summary>
    /// Fallback for unsupported types: a small "file card" of metadata carried in
    /// <see cref="FilePreviewResult.Text"/> plus an "open with the default app" action.
    /// </summary>
    FileInfo,

    /// <summary>
    /// Markdown source carried in <see cref="FilePreviewResult.Text"/>; the App-layer
    /// card renders headings, lists, code blocks, and inline emphasis.
    /// </summary>
    Markdown,

    // Later phases: Code (syntax highlighting)
}

/// <summary>Parsed CSV: header, typed columns, and (initially partial) rows.</summary>
public sealed class CsvPreviewModel
{
    /// <summary>The header columns with their inferred types.</summary>
    public required IReadOnlyList<CsvColumn> Columns { get; init; }

    /// <summary>Rows available immediately; grows as the background load appends.</summary>
    public required IReadOnlyList<string[]> Rows { get; init; }

    /// <summary>Total data rows in the file, or null while the background scan is still counting.</summary>
    public long? TotalRowCount { get; init; }

    /// <summary>The delimiter the file was parsed with.</summary>
    public required char Delimiter { get; init; }

    /// <summary>True when <see cref="Rows"/> holds only the immediate window of a larger file.</summary>
    public bool IsPartial { get; init; }
}

/// <summary>A CSV column with its inferred type (drives right-alignment + the stats bar).</summary>
public sealed record CsvColumn(string Name, CsvColumnType Type);

/// <summary>The inferred type of a CSV column.</summary>
public enum CsvColumnType
{
    /// <summary>Free text; left-aligned.</summary>
    Text = 0,

    /// <summary>Numeric; right-aligned and eligible for the stats bar.</summary>
    Number,

    /// <summary>Date/time values.</summary>
    DateTime,

    /// <summary>Boolean values.</summary>
    Boolean,
}

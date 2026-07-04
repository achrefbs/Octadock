// Example implementation for the file-preview proposal (docs/proposals/file-preview-quicklook.md).
// Target location: src/Octadock.Core/Abstractions/ (contracts) — platform-agnostic, unit-testable.

using Octadock.Core.Geometry;

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
    public required FilePreviewKind Kind { get; init; }
    public required string FilePath { get; init; }
    public string? Error { get; init; }

    public CsvPreviewModel? Csv { get; init; }
    public string? Text { get; init; }

    public static FilePreviewResult Fail(string path, string error)
        => new() { Kind = FilePreviewKind.Error, FilePath = path, Error = error };
}

public enum FilePreviewKind
{
    Error = 0,
    Csv,
    PlainText,
    // Later phases: Json, Markdown, Image, Code
}

/// <summary>Parsed CSV: header, typed columns, and (initially partial) rows.</summary>
public sealed class CsvPreviewModel
{
    public required IReadOnlyList<CsvColumn> Columns { get; init; }

    /// <summary>Rows available immediately; grows as the background load appends.</summary>
    public required IReadOnlyList<string[]> Rows { get; init; }

    /// <summary>Total data rows in the file, or null while the background scan is still counting.</summary>
    public long? TotalRowCount { get; init; }

    public required char Delimiter { get; init; }

    /// <summary>True when <see cref="Rows"/> holds only the immediate window of a larger file.</summary>
    public bool IsPartial { get; init; }
}

/// <summary>A CSV column with its inferred type (drives right-alignment + the stats bar).</summary>
public sealed record CsvColumn(string Name, CsvColumnType Type);

public enum CsvColumnType
{
    Text = 0,
    Number,
    DateTime,
    Boolean,
}

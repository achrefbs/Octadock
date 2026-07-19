namespace Octadock.Core.Abstractions;

/// <summary>
/// One provider per previewable file type. Providers parse into a bounded,
/// source-preserving <see cref="FilePreviewResult"/> off the UI thread; the App
/// layer decides how to present that result.
/// </summary>
public interface IFilePreviewProvider
{
    /// <summary>Provider priority when several claim an extension (higher wins).</summary>
    int Priority => 0;

    /// <summary>True when this provider can preview the given extension (".csv", with dot, lowercase).</summary>
    bool CanPreview(string extension);

    /// <summary>Loads a bounded preview without putting product messages in source content.</summary>
    Task<FilePreviewResult> LoadAsync(string path, FilePreviewOptions options, CancellationToken cancellationToken);
}

/// <summary>Load-time guardrails. Defaults are deliberately conservative for an immediate preview.</summary>
public sealed record FilePreviewOptions
{
    /// <summary>Maximum data rows shown in the immediate CSV/TSV sample.</summary>
    public int MaxImmediateRows { get; init; } = 500;

    /// <summary>Reserved hard cap for future background scans.</summary>
    public int MaxTotalRows { get; init; } = 250_000;

    /// <summary>Explicit delimiter override; null = auto-detect (',' ';' '\t' '|').</summary>
    public char? Delimiter { get; init; }

    /// <summary>Maximum columns accepted in a CSV/TSV record.</summary>
    public int MaxCsvColumns { get; init; } = 256;

    /// <summary>Maximum decoded characters accepted in one CSV/TSV field.</summary>
    public int MaxCsvFieldCharacters { get; init; } = 256 * 1024;

    /// <summary>Maximum decoded characters accepted in one CSV/TSV record.</summary>
    public int MaxCsvRecordCharacters { get; init; } = 1024 * 1024;

    /// <summary>Maximum decoded characters retained across the visible CSV/TSV sample.</summary>
    public int MaxCsvSampleCharacters { get; init; } = 8 * 1024 * 1024;
}

/// <summary>
/// A truthful preview result. Source content is kept separate from rendered
/// content and all warnings/failures are structured, so clipboard actions can
/// never accidentally copy Octadock's presentation messages.
/// </summary>
public sealed record FilePreviewResult
{
    private string? _legacyText;
    private string? _legacyError;

    /// <summary>Which payload is populated.</summary>
    public required FilePreviewKind Kind { get; init; }

    /// <summary>The full path that was requested.</summary>
    public required string FilePath { get; init; }

    /// <summary>Original decoded source bytes within the shown window, with no Octadock notices.</summary>
    public string? SourceContent { get; init; }

    /// <summary>Optional rendered/formatted content, such as pretty-printed JSON.</summary>
    public string? RenderedContent { get; init; }

    /// <summary>Total source length observed when the read began.</summary>
    public long? SourceByteLength { get; init; }

    /// <summary>Detected text encoding, when this is a text-backed preview.</summary>
    public PreviewEncoding? DetectedEncoding { get; init; }

    /// <summary>The byte or row window represented by this result.</summary>
    public PreviewContentScope? Scope { get; init; }

    /// <summary>Non-fatal, structured presentation warnings.</summary>
    public IReadOnlyList<FilePreviewWarning> Warnings { get; init; } = [];

    /// <summary>A structured failure. Some recoverable failures, such as malformed JSON, may still carry source.</summary>
    public FilePreviewFailure? Failure { get; init; }

    /// <summary>The parsed table when <see cref="Kind"/> is <see cref="FilePreviewKind.Csv"/>.</summary>
    public CsvPreviewModel? Csv { get; init; }

    /// <summary>
    /// Compatibility presentation alias. New providers should set
    /// <see cref="SourceContent"/> and, when applicable, <see cref="RenderedContent"/>.
    /// </summary>
    public string? Text
    {
        get => RenderedContent ?? SourceContent ?? _legacyText;
        init => _legacyText = value;
    }

    /// <summary>Stable product copy for an error. Kept for existing consumers.</summary>
    public string? Error
    {
        get => Failure?.Message ?? _legacyError;
        init => _legacyError = value;
    }

    /// <summary>Content safe for the default "copy original" action.</summary>
    public string? CopyableSourceContent => SourceContent ?? _legacyText;

    /// <summary>Content used by the main presentation surface.</summary>
    public string? PresentationContent => RenderedContent ?? SourceContent ?? _legacyText;

    /// <summary>The full path to an image on disk. Core remains WPF-free.</summary>
    public string? ImagePath { get; init; }

    /// <summary>The source image width in pixels, when known.</summary>
    public int? ImagePixelWidth { get; init; }

    /// <summary>The source image height in pixels, when known.</summary>
    public int? ImagePixelHeight { get; init; }

    /// <summary>Builds a structured failure with stable product copy.</summary>
    public static FilePreviewResult Fail(
        string path,
        FilePreviewFailureKind kind,
        long? sourceByteLength = null,
        string? productMessage = null)
        => new()
        {
            Kind = FilePreviewKind.Error,
            FilePath = path,
            SourceByteLength = sourceByteLength,
            Failure = new FilePreviewFailure(kind, productMessage ?? FilePreviewFailureCopy.For(kind)),
        };

}

/// <summary>The kind of payload carried by a <see cref="FilePreviewResult"/>.</summary>
public enum FilePreviewKind
{
    Error = 0,
    Csv,
    PlainText,
    Image,
    FileInfo,
    Markdown,
    Loading,
}

/// <summary>Stable failure categories used for recovery actions and product copy.</summary>
public enum FilePreviewFailureKind
{
    None = 0,
    InvalidPath,
    NotFound,
    AccessDenied,
    Busy,
    Changed,
    Malformed,
    TooLarge,
    UnsupportedEncoding,
    CodecUnavailable,
    Unsupported,
    Empty,
    Cancelled,
    Unknown,
}

/// <summary>A classified preview failure with non-localized, product-owned copy.</summary>
public sealed record FilePreviewFailure(FilePreviewFailureKind Kind, string Message)
{
    public bool CanRetry => Kind is FilePreviewFailureKind.Busy or FilePreviewFailureKind.Changed or
        FilePreviewFailureKind.Cancelled or FilePreviewFailureKind.Unknown;

    public bool CanLocate => Kind == FilePreviewFailureKind.NotFound;
}

/// <summary>Canonical product copy for preview failures; raw exception messages never reach the card.</summary>
public static class FilePreviewFailureCopy
{
    public static string For(FilePreviewFailureKind kind) => kind switch
    {
        FilePreviewFailureKind.InvalidPath => "That file path is not valid.",
        FilePreviewFailureKind.NotFound => "This file is no longer at that location.",
        FilePreviewFailureKind.AccessDenied => "Octadock does not have permission to read this file.",
        FilePreviewFailureKind.Busy => "This file is in use by another app. Close it or try again.",
        FilePreviewFailureKind.Changed => "This file changed while Octadock was reading it. Try again.",
        FilePreviewFailureKind.Malformed => "This file is damaged or is not valid for its file type.",
        FilePreviewFailureKind.TooLarge => "This file exceeds Octadock's safe preview limits.",
        FilePreviewFailureKind.UnsupportedEncoding => "Octadock could not safely decode this file's text encoding.",
        FilePreviewFailureKind.CodecUnavailable => "Windows does not have the codec needed to preview this image.",
        FilePreviewFailureKind.Unsupported => "Octadock does not support a preview for this file type.",
        FilePreviewFailureKind.Empty => "0 bytes—nothing to preview",
        FilePreviewFailureKind.Cancelled => "Preview cancelled. You can try again.",
        _ => "Octadock could not preview this file.",
    };
}

/// <summary>The encoding identified by the bounded reader.</summary>
public sealed record PreviewEncoding(
    PreviewEncodingKind Kind,
    string DisplayName,
    bool HasByteOrderMark,
    PreviewEncodingConfidence Confidence);

public enum PreviewEncodingKind
{
    Unknown = 0,
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian,
    Utf32LittleEndian,
    Utf32BigEndian,
}

public enum PreviewEncodingConfidence
{
    Unknown = 0,
    Low,
    Medium,
    High,
}

/// <summary>Describes precisely which source window or row sample is shown.</summary>
public sealed record PreviewContentScope
{
    public long? StartByte { get; init; }

    public long? EndByteExclusive { get; init; }

    public long? StartRow { get; init; }

    public int? ShownRowCount { get; init; }

    public long? TotalRowCount { get; init; }

    public bool IsTruncated { get; init; }

    public bool IsSampled { get; init; }

    public string? Label { get; init; }
}

/// <summary>A non-fatal warning kept outside source/clipboard content.</summary>
public sealed record FilePreviewWarning(FilePreviewWarningKind Kind, string Message);

public enum FilePreviewWarningKind
{
    Truncated = 0,
    Sampled,
    LowEncodingConfidence,
    Malformed,
    RaggedRowsNormalized,
    ExtraColumnsAdded,
    Empty,
}

/// <summary>Parsed CSV with one visible, bounded schema shared by the table and clipboard.</summary>
public sealed class CsvPreviewModel
{
    public required IReadOnlyList<CsvColumn> Columns { get; init; }

    public required IReadOnlyList<string[]> Rows { get; init; }

    public long? TotalRowCount { get; init; }

    public required char Delimiter { get; init; }

    /// <summary>True only when a probe found at least one data row beyond <see cref="Rows"/>.</summary>
    public bool IsPartial { get; init; }

    public bool IsSampled => IsPartial;
}

public sealed record CsvColumn(string Name, CsvColumnType Type);

public enum CsvColumnType
{
    Text = 0,
    Number,
    DateTime,
    Boolean,
}

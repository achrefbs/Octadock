namespace Octadock.App.Ai;

/// <summary>Safety and sensitivity settings for one local raster comparison.</summary>
public sealed record VisualComparisonOptions
{
    /// <summary>
    /// A pixel is reported as changed when any decoded RGBA channel differs by more
    /// than this value. Coverage introduced by a dimension mismatch always counts as
    /// changed. Valid values are 0 through 255.
    /// </summary>
    public int ChannelDeltaThreshold { get; init; }

    /// <summary>Maximum width or height accepted. Callers may lower, but not raise, the hard safety cap.</summary>
    public int MaxDimension { get; init; } = 20_000;

    /// <summary>Maximum decoded pixels per image and in the normalized common canvas.</summary>
    public long MaxPixelCount { get; init; } = 16_000_000;

    /// <summary>Maximum encoded input size before decoding.</summary>
    public long MaxEncodedBytes { get; init; } = 128L * 1024 * 1024;
}

/// <summary>The smallest inclusive rectangle containing every threshold-exceeding pixel.</summary>
public readonly record struct VisualChangeBounds(int X, int Y, int Width, int Height);

/// <summary>Deterministic metrics and artifacts produced by a visual comparison.</summary>
public sealed record VisualComparisonResult
{
    public required string BaselinePath { get; init; }

    public required string CandidatePath { get; init; }

    /// <summary>SHA-256 of the exact locked baseline bytes used for the decoded comparison.</summary>
    public required string BaselineSha256 { get; init; }

    /// <summary>SHA-256 of the exact locked candidate bytes used for the decoded comparison.</summary>
    public required string CandidateSha256 { get; init; }

    public int BaselineWidth { get; init; }

    public int BaselineHeight { get; init; }

    public int CandidateWidth { get; init; }

    public int CandidateHeight { get; init; }

    /// <summary>
    /// Width of the top-left-aligned common canvas. Inputs remain at native size and
    /// are never stretched or resampled.
    /// </summary>
    public int CanvasWidth { get; init; }

    /// <summary>Height of the top-left-aligned common canvas.</summary>
    public int CanvasHeight { get; init; }

    public long TotalPixelCount { get; init; }

    /// <summary>Number of pixels whose maximum RGBA delta exceeds the configured threshold.</summary>
    public long ChangedPixelCount { get; init; }

    public double ChangedPixelRatio { get; init; }

    /// <summary>Mean absolute RGBA-channel delta across the normalized canvas, in the range 0–255.</summary>
    public double MeanChannelDelta { get; init; }

    /// <summary>Largest absolute decoded RGBA-channel delta, in the range 0–255.</summary>
    public int MaxChannelDelta { get; init; }

    public VisualChangeBounds? ChangeBounds { get; init; }

    /// <summary>True only when dimensions and every decoded RGBA channel match exactly, independent of threshold.</summary>
    public bool ExactMatch { get; init; }

    public int ChannelDeltaThreshold { get; init; }

    /// <summary>Unique local PNG created under Octadock's managed temporary export directory.</summary>
    public required string DiffImagePath { get; init; }

    /// <summary>Concise, display-ready Markdown summary of the metrics.</summary>
    public required string MarkdownSummary { get; init; }
}

/// <summary>Compares two bounded local raster images without modifying either input.</summary>
public interface IVisualComparisonService
{
    Task<VisualComparisonResult> CompareAsync(
        string baselinePath,
        string candidatePath,
        VisualComparisonOptions? options = null,
        CancellationToken cancellationToken = default);
}

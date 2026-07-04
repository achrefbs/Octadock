using Octadock.Core.Primitives;

namespace Octadock.Core.Ocr;

/// <summary>A single recognized line of text with its position and confidence.</summary>
public sealed record OcrLine(string Text, double Confidence, PointD Origin);

/// <summary>
/// The outcome of an OCR pass. <see cref="Text"/> is the composed output shaped
/// by the requested <see cref="Commands.OcrTextMode"/>; <see cref="Lines"/> keeps
/// the per-line detail for the optional low-confidence review sheet.
/// </summary>
public sealed record OcrResult
{
    public required string Text { get; init; }

    public IReadOnlyList<OcrLine> Lines { get; init; } = [];

    /// <summary>Detected/used language (BCP-47), when known.</summary>
    public string? Language { get; init; }

    /// <summary>Mean confidence across lines in the range 0..1.</summary>
    public double AverageConfidence { get; init; }

    /// <summary>True when no text was recognized.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Text);

    public static readonly OcrResult Empty = new() { Text = string.Empty };
}

/// <summary>Availability of an OCR provider on the current machine.</summary>
public sealed record OcrAvailability(bool IsAvailable, string? Reason = null)
{
    public static readonly OcrAvailability Available = new(true);

    public static OcrAvailability Unavailable(string reason) => new(false, reason);
}

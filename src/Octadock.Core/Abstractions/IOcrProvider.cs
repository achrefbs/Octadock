using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Ocr;
using Octadock.Core.Settings;

namespace Octadock.Core.Abstractions;

/// <summary>
/// A pluggable text-recognition backend. The default is Windows.Media.Ocr;
/// alternative providers (Windows AI Text Recognition, Tesseract) implement the
/// same contract so the app can fall back based on availability.
/// </summary>
public interface IOcrProvider
{
    /// <summary>Which provider this instance represents.</summary>
    OcrProvider Kind { get; }

    /// <summary>Reports whether this provider can run on the current machine.</summary>
    OcrAvailability CheckAvailability();

    /// <summary>Recognizes text in a captured frame.</summary>
    Task<OcrResult> RecognizeAsync(
        CapturedFrame frame,
        OcrTextMode mode,
        string? language = null,
        CancellationToken cancellationToken = default);

    /// <summary>Recognizes text in an image file.</summary>
    Task<OcrResult> RecognizeAsync(
        string imagePath,
        OcrTextMode mode,
        string? language = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the best available OCR provider given the user's preference and
/// runtime availability. Implemented in the platform layer.
/// </summary>
public interface IOcrProviderFactory
{
    /// <summary>Returns the preferred provider if available, else the first available fallback, else null.</summary>
    IOcrProvider? Resolve(OcrProvider preferred);

    /// <summary>All providers with their availability, for the settings UI.</summary>
    IReadOnlyList<(OcrProvider Provider, OcrAvailability Availability)> Describe();
}

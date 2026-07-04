using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Ocr;
using Octadock.Core.Primitives;
using Octadock.Core.Settings;
using OcrEngine = Windows.Media.Ocr.OcrEngine;
using WinRtOcrResult = Windows.Media.Ocr.OcrResult;

namespace Octadock.Platform.Windows.Ocr;

/// <summary>
/// OCR provider backed by <see cref="OcrEngine"/> (Windows.Media.Ocr). Recognizes
/// text from captured frames or image files. Windows OCR does not expose per-word
/// confidence, so <see cref="OcrLine.Confidence"/> and
/// <see cref="OcrResult.AverageConfidence"/> are reported as 1.0 (recognized).
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsMediaOcrProvider : IOcrProvider
{
    private readonly ILogger<WindowsMediaOcrProvider> _logger;

    /// <summary>Creates the Windows.Media.Ocr provider.</summary>
    public WindowsMediaOcrProvider(ILogger<WindowsMediaOcrProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public OcrProvider Kind => OcrProvider.WindowsMediaOcr;

    /// <inheritdoc />
    public OcrAvailability CheckAvailability()
    {
        try
        {
            IReadOnlyList<Language> languages = OcrEngine.AvailableRecognizerLanguages;
            if (languages.Count == 0)
            {
                return OcrAvailability.Unavailable("No OCR recognizer languages are installed.");
            }

            // Confirm an engine can actually be created.
            OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null && OcrEngine.TryCreateFromLanguage(languages[0]) is null)
            {
                return OcrAvailability.Unavailable("Windows OCR could not create an engine for any installed language.");
            }

            return OcrAvailability.Available;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Windows.Media.Ocr availability check failed.");
            return OcrAvailability.Unavailable($"Windows OCR is unavailable: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(
        CapturedFrame frame,
        OcrTextMode mode,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        using SoftwareBitmap bitmap = CreateSoftwareBitmap(frame);
        return await RecognizeBitmapAsync(bitmap, mode, language, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(
        string imagePath,
        OcrTextMode mode,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file not found.", imagePath);
        }

        using SoftwareBitmap bitmap = await LoadBitmapFromFileAsync(imagePath, cancellationToken).ConfigureAwait(false);
        return await RecognizeBitmapAsync(bitmap, mode, language, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OcrResult> RecognizeBitmapAsync(
        SoftwareBitmap bitmap,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        OcrEngine? engine = CreateEngine(language);
        if (engine is null)
        {
            _logger.LogWarning("No usable OCR engine; returning empty result.");
            return OcrResult.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        WinRtOcrResult recognized = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

        var lines = new List<OcrLine>(recognized.Lines.Count);
        foreach (var line in recognized.Lines)
        {
            double originX = line.Words.Count > 0 ? line.Words[0].BoundingRect.X : 0;
            double originY = line.Words.Count > 0 ? line.Words[0].BoundingRect.Y : 0;
            lines.Add(new OcrLine(line.Text, 1.0, new PointD(originX, originY)));
        }

        string text = Compose(recognized, mode);
        double averageConfidence = lines.Count > 0 ? 1.0 : 0.0;

        return new OcrResult
        {
            Text = text,
            Lines = lines,
            Language = engine.RecognizerLanguage?.LanguageTag,
            AverageConfidence = averageConfidence,
        };
    }

    private OcrEngine? CreateEngine(string? language)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(language) && Language.IsWellFormed(language))
            {
                OcrEngine? specific = OcrEngine.TryCreateFromLanguage(new Language(language));
                if (specific is not null)
                {
                    return specific;
                }

                _logger.LogDebug("OCR language '{Language}' unavailable; falling back to user profile languages.", language);
            }

            return OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create an OCR engine.");
            return null;
        }
    }

    private static string Compose(WinRtOcrResult recognized, OcrTextMode mode)
    {
        switch (mode)
        {
            case OcrTextMode.Compact:
            {
                // Join all words with single spaces, collapsing line breaks.
                var words = new List<string>();
                foreach (var line in recognized.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        if (!string.IsNullOrWhiteSpace(word.Text))
                        {
                            words.Add(word.Text);
                        }
                    }
                }

                return string.Join(' ', words);
            }

            case OcrTextMode.Layout:
            {
                // Approximate layout: indent each line proportionally to its left
                // offset and preserve line breaks. A full column reconstruction is
                // out of scope; this keeps relative structure readable.
                var sb = new StringBuilder();
                foreach (var line in recognized.Lines)
                {
                    double left = line.Words.Count > 0 ? line.Words[0].BoundingRect.X : 0;
                    int indent = (int)Math.Clamp(left / 12.0, 0, 40);
                    sb.Append(' ', indent);
                    sb.AppendLine(line.Text);
                }

                return sb.ToString().TrimEnd();
            }

            case OcrTextMode.Lines:
            default:
            {
                var sb = new StringBuilder();
                foreach (var line in recognized.Lines)
                {
                    sb.AppendLine(line.Text);
                }

                return sb.ToString().TrimEnd();
            }
        }
    }

    private static SoftwareBitmap CreateSoftwareBitmap(CapturedFrame frame)
    {
        // OCR expects BGRA8. Copy the frame into a tightly-packed buffer (stride ==
        // width*4) so SoftwareBitmap reads it correctly.
        int width = frame.Width;
        int height = frame.Height;
        int dstStride = width * 4;
        byte[] packed = new byte[dstStride * height];

        ReadOnlySpan<byte> src = frame.Pixels.Span;
        if (frame.Stride == dstStride)
        {
            src[..(dstStride * height)].CopyTo(packed);
        }
        else
        {
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> row = src.Slice(y * frame.Stride, dstStride);
                row.CopyTo(packed.AsSpan(y * dstStride, dstStride));
            }
        }

        BitmapAlphaMode alphaMode = frame.Format == FramePixelFormat.Pbgra32
            ? BitmapAlphaMode.Premultiplied
            : BitmapAlphaMode.Straight;

        IBuffer buffer = packed.AsBuffer();
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, width, height, alphaMode);
    }

    private static async Task<SoftwareBitmap> LoadBitmapFromFileAsync(string imagePath, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        SoftwareBitmap decoded = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);

        // Normalize to Bgra8 straight alpha for the OCR engine.
        if (decoded.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && decoded.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            return decoded;
        }

        SoftwareBitmap converted = SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
        decoded.Dispose();
        return converted;
    }
}

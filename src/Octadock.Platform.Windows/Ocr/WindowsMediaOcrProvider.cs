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
    /// <summary>Compressed input cap; keeps direct provider callers bounded too.</summary>
    internal const long MaxInputFileBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Reject implausibly large source canvases before a decoder can expand a tiny
    /// compressed file into hundreds of megabytes. Normal 8K captures remain valid.
    /// </summary>
    internal const long MaxSourcePixels = 40_000_000;

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

        using SoftwareBitmap bitmap = CreateSoftwareBitmap(frame, cancellationToken);
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
        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
        {
            throw new InvalidDataException("The decoded image exceeds the Windows OCR dimension limit.");
        }

        WinRtOcrResult recognized = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var lines = new List<OcrLine>(recognized.Lines.Count);
        foreach (var line in recognized.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private static SoftwareBitmap CreateSoftwareBitmap(CapturedFrame frame, CancellationToken cancellationToken)
    {
        // OCR expects BGRA8 and rejects either edge above MaxImageDimension. Copy
        // into a bounded, tightly packed buffer and downscale larger desktop frames
        // before handing them to WinRT.
        int width = frame.Width;
        int height = frame.Height;
        long sourcePixels = checked((long)width * height);
        if (sourcePixels > MaxSourcePixels)
        {
            throw new InvalidDataException("The captured image is too large to recognize safely.");
        }

        int maxDimension = checked((int)OcrEngine.MaxImageDimension);
        double scale = Math.Min(1.0, maxDimension / (double)Math.Max(width, height));
        int outputWidth = Math.Max(1, (int)Math.Floor(width * scale));
        int outputHeight = Math.Max(1, (int)Math.Floor(height * scale));
        int dstStride = checked(outputWidth * 4);
        byte[] packed = new byte[checked(dstStride * outputHeight)];

        ReadOnlySpan<byte> src = frame.Pixels.Span;
        if (outputWidth == width && outputHeight == height)
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                src.Slice(y * frame.Stride, dstStride).CopyTo(packed.AsSpan(y * dstStride, dstStride));
            }
        }
        else
        {
            // Bilinear resampling preserves small glyph edges better than nearest
            // neighbor while keeping the output allocation strictly bounded.
            for (int outputY = 0; outputY < outputHeight; outputY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double sourceY = ((outputY + 0.5) / scale) - 0.5;
                int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, height - 1);
                int y1 = Math.Min(y0 + 1, height - 1);
                double fy = Math.Clamp(sourceY - y0, 0.0, 1.0);

                for (int outputX = 0; outputX < outputWidth; outputX++)
                {
                    double sourceX = ((outputX + 0.5) / scale) - 0.5;
                    int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, width - 1);
                    int x1 = Math.Min(x0 + 1, width - 1);
                    double fx = Math.Clamp(sourceX - x0, 0.0, 1.0);

                    int p00 = (y0 * frame.Stride) + (x0 * 4);
                    int p10 = (y0 * frame.Stride) + (x1 * 4);
                    int p01 = (y1 * frame.Stride) + (x0 * 4);
                    int p11 = (y1 * frame.Stride) + (x1 * 4);
                    int destination = (outputY * dstStride) + (outputX * 4);

                    for (int channel = 0; channel < 4; channel++)
                    {
                        double top = src[p00 + channel] + ((src[p10 + channel] - src[p00 + channel]) * fx);
                        double bottom = src[p01 + channel] + ((src[p11 + channel] - src[p01 + channel]) * fx);
                        packed[destination + channel] = (byte)Math.Clamp(
                            (int)Math.Round(top + ((bottom - top) * fy)),
                            byte.MinValue,
                            byte.MaxValue);
                    }
                }
            }
        }

        BitmapAlphaMode alphaMode = frame.Format == FramePixelFormat.Pbgra32
            ? BitmapAlphaMode.Premultiplied
            : BitmapAlphaMode.Straight;

        IBuffer buffer = packed.AsBuffer();
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            outputWidth,
            outputHeight,
            alphaMode);
    }

    private static async Task<SoftwareBitmap> LoadBitmapFromFileAsync(string imagePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(imagePath);
        if (info.Length == 0)
        {
            throw new InvalidDataException("The image file is empty.");
        }

        if (info.Length > MaxInputFileBytes)
        {
            throw new InvalidDataException("The image file is too large to recognize safely.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        uint sourceWidth = decoder.PixelWidth;
        uint sourceHeight = decoder.PixelHeight;
        if (sourceWidth == 0 || sourceHeight == 0)
        {
            throw new InvalidDataException("The image has invalid dimensions.");
        }

        long sourcePixels = checked((long)sourceWidth * sourceHeight);
        if (sourcePixels > MaxSourcePixels)
        {
            throw new InvalidDataException("The image dimensions are too large to recognize safely.");
        }

        uint maxDimension = OcrEngine.MaxImageDimension;
        double scale = Math.Min(1.0, maxDimension / (double)Math.Max(sourceWidth, sourceHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1u, (uint)Math.Floor(sourceWidth * scale)),
            ScaledHeight = Math.Max(1u, (uint)Math.Floor(sourceHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        SoftwareBitmap decoded = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

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

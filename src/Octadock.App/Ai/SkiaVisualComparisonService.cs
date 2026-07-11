using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Octadock.Core.Abstractions;
using Octadock.Core.Io;
using SkiaSharp;

namespace Octadock.App.Ai;

/// <summary>
/// Bounded, local-only raster comparison powered by SkiaSharp. Images are decoded
/// to premultiplied RGBA, aligned at their top-left corners on a common canvas, and
/// compared without scaling. The generated heat-map is written with create-new
/// semantics under <see cref="IStoragePaths.TempExportsDirectory"/>.
/// </summary>
public sealed class SkiaVisualComparisonService : IVisualComparisonService
{
    private const int HardMaxDimension = 20_000;
    private const long HardMaxPixelCount = 16_000_000;
    private const long HardMaxEncodedBytes = 128L * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif",
    };

    private readonly IStoragePaths _paths;

    public SkiaVisualComparisonService(IStoragePaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<VisualComparisonResult> CompareAsync(
        string baselinePath,
        string candidatePath,
        VisualComparisonOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        VisualComparisonOptions effectiveOptions = options ?? new VisualComparisonOptions();
        ValidateOptions(effectiveOptions);
        cancellationToken.ThrowIfCancellationRequested();

        ComparisonAnalysis analysis = await Task.Run(
                () => CompareCore(baselinePath, candidatePath, effectiveOptions, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        string diffPath = await WriteUniqueDiffAsync(
                analysis.DiffPng,
                analysis.BaselinePath,
                analysis.CandidatePath,
                cancellationToken)
            .ConfigureAwait(false);

        string markdown = BuildMarkdownSummary(analysis, effectiveOptions.ChannelDeltaThreshold);
        return new VisualComparisonResult
        {
            BaselinePath = analysis.BaselinePath,
            CandidatePath = analysis.CandidatePath,
            BaselineSha256 = analysis.BaselineSha256,
            CandidateSha256 = analysis.CandidateSha256,
            BaselineWidth = analysis.BaselineWidth,
            BaselineHeight = analysis.BaselineHeight,
            CandidateWidth = analysis.CandidateWidth,
            CandidateHeight = analysis.CandidateHeight,
            CanvasWidth = analysis.CanvasWidth,
            CanvasHeight = analysis.CanvasHeight,
            TotalPixelCount = analysis.TotalPixelCount,
            ChangedPixelCount = analysis.ChangedPixelCount,
            ChangedPixelRatio = analysis.ChangedPixelRatio,
            MeanChannelDelta = analysis.MeanChannelDelta,
            MaxChannelDelta = analysis.MaxChannelDelta,
            ChangeBounds = analysis.ChangeBounds,
            ExactMatch = analysis.ExactMatch,
            ChannelDeltaThreshold = effectiveOptions.ChannelDeltaThreshold,
            DiffImagePath = diffPath,
            MarkdownSummary = markdown,
        };
    }

    private static ComparisonAnalysis CompareCore(
        string baselinePath,
        string candidatePath,
        VisualComparisonOptions options,
        CancellationToken cancellationToken)
    {
        string baselineFullPath = ValidateInputPath(
            baselinePath,
            nameof(baselinePath),
            options.MaxEncodedBytes,
            cancellationToken);
        string candidateFullPath = ValidateInputPath(
            candidatePath,
            nameof(candidatePath),
            options.MaxEncodedBytes,
            cancellationToken);

        DecodedImage baselineDecoded = DecodeBounded(baselineFullPath, options, cancellationToken);
        using SKBitmap baseline = baselineDecoded.Bitmap;
        DecodedImage candidateDecoded = DecodeBounded(candidateFullPath, options, cancellationToken);
        using SKBitmap candidate = candidateDecoded.Bitmap;

        int canvasWidth = Math.Max(baseline.Width, candidate.Width);
        int canvasHeight = Math.Max(baseline.Height, candidate.Height);
        long totalPixels = checked((long)canvasWidth * canvasHeight);
        if (totalPixels > options.MaxPixelCount)
        {
            throw new InvalidOperationException(
                $"The normalized comparison canvas has {totalPixels:N0} pixels; the safe limit is {options.MaxPixelCount:N0}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var diff = AllocateBitmap(canvasWidth, canvasHeight);
        PixelAnalysis pixels = AnalyzePixels(
            baseline,
            candidate,
            diff,
            options.ChannelDeltaThreshold,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        byte[] diffPng = EncodePng(diff);
        return new ComparisonAnalysis(
            baselineFullPath,
            candidateFullPath,
            baselineDecoded.Sha256,
            candidateDecoded.Sha256,
            baseline.Width,
            baseline.Height,
            candidate.Width,
            candidate.Height,
            canvasWidth,
            canvasHeight,
            totalPixels,
            pixels.ChangedPixelCount,
            pixels.ChangedPixelCount / (double)totalPixels,
            pixels.ChannelDeltaTotal / (double)(totalPixels * 4),
            pixels.MaxChannelDelta,
            pixels.ChangeBounds,
            pixels.ExactMatch,
            diffPng);
    }

    private static string ValidateInputPath(
        string path,
        string parameterName,
        long maxEncodedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Choose a local raster image.", parameterName);
        }

        string fullPath = AgentLocalPathGuard.ValidateExistingFile(path, "Visual comparison image");

        string extension = Path.GetExtension(fullPath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new NotSupportedException(
                $"'{extension}' is not a supported raster format. Use PNG, JPEG, WebP, BMP, or GIF.");
        }

        long encodedBytes = new FileInfo(fullPath).Length;
        if (encodedBytes <= 0)
        {
            throw new InvalidOperationException("The image is empty or corrupt.");
        }

        if (encodedBytes > maxEncodedBytes)
        {
            throw new InvalidOperationException(
                $"The encoded image is {encodedBytes:N0} bytes; the safe limit is {maxEncodedBytes:N0} bytes.");
        }

        return fullPath;
    }

    private static DecodedImage DecodeBounded(
        string path,
        VisualComparisonOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        byte[] digest = SHA256.HashData(stream);
        stream.Position = 0;
        using SKCodec? codec = SKCodec.Create(stream, out SKCodecResult codecResult);
        if (codec is null || codecResult != SKCodecResult.Success)
        {
            throw new InvalidOperationException("The image format is unsupported or the image is corrupt.");
        }

        SKImageInfo sourceInfo = codec.Info;
        ValidateDimensions(path, sourceInfo.Width, sourceInfo.Height, options);
        var decodedInfo = new SKImageInfo(
            sourceInfo.Width,
            sourceInfo.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        SKBitmap bitmap = AllocateBitmap(decodedInfo.Width, decodedInfo.Height);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SKCodecResult decodeResult = codec.GetPixels(
                decodedInfo,
                bitmap.GetPixels(),
                bitmap.RowBytes,
                new SKCodecOptions());
            if (decodeResult != SKCodecResult.Success)
            {
                throw new InvalidOperationException("The image is incomplete, unsupported, or corrupt.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new DecodedImage(
                bitmap,
                Convert.ToHexString(digest).ToLowerInvariant());
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void ValidateDimensions(
        string path,
        int width,
        int height,
        VisualComparisonOptions options)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' has invalid image dimensions.");
        }

        if (width > options.MaxDimension || height > options.MaxDimension)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(path)}' is {width:N0} × {height:N0}; each dimension is limited to {options.MaxDimension:N0} pixels.");
        }

        long pixels = checked((long)width * height);
        if (pixels > options.MaxPixelCount)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(path)}' has {pixels:N0} pixels; the safe limit is {options.MaxPixelCount:N0}.");
        }
    }

    private static SKBitmap AllocateBitmap(int width, int height)
    {
        var bitmap = new SKBitmap();
        try
        {
            if (!bitmap.TryAllocPixels(new SKImageInfo(
                    width,
                    height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul)))
            {
                throw new InvalidOperationException("The image is too large to compare safely in available memory.");
            }

            return bitmap;
        }
        catch (OutOfMemoryException ex)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("The image is too large to compare safely in available memory.", ex);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static unsafe PixelAnalysis AnalyzePixels(
        SKBitmap baseline,
        SKBitmap candidate,
        SKBitmap diff,
        int threshold,
        CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> baselinePixels = baseline.GetPixelSpan();
        ReadOnlySpan<byte> candidatePixels = candidate.GetPixelSpan();
        var diffPixels = new Span<byte>((void*)diff.GetPixels(), checked(diff.RowBytes * diff.Height));

        long changedPixels = 0;
        long channelDeltaTotal = 0;
        int maxChannelDelta = 0;
        bool exact = baseline.Width == candidate.Width && baseline.Height == candidate.Height;
        int minX = diff.Width;
        int minY = diff.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < diff.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < diff.Width; x++)
            {
                bool inBaseline = x < baseline.Width && y < baseline.Height;
                bool inCandidate = x < candidate.Width && y < candidate.Height;
                bool coverageMismatch = inBaseline != inCandidate;

                int baselineOffset = inBaseline ? (y * baseline.RowBytes) + (x * 4) : 0;
                int candidateOffset = inCandidate ? (y * candidate.RowBytes) + (x * 4) : 0;
                byte br = inBaseline ? baselinePixels[baselineOffset] : (byte)0;
                byte bg = inBaseline ? baselinePixels[baselineOffset + 1] : (byte)0;
                byte bb = inBaseline ? baselinePixels[baselineOffset + 2] : (byte)0;
                byte ba = inBaseline ? baselinePixels[baselineOffset + 3] : (byte)0;
                byte cr = inCandidate ? candidatePixels[candidateOffset] : (byte)0;
                byte cg = inCandidate ? candidatePixels[candidateOffset + 1] : (byte)0;
                byte cb = inCandidate ? candidatePixels[candidateOffset + 2] : (byte)0;
                byte ca = inCandidate ? candidatePixels[candidateOffset + 3] : (byte)0;

                int dr = coverageMismatch ? 255 : Math.Abs(br - cr);
                int dg = coverageMismatch ? 255 : Math.Abs(bg - cg);
                int db = coverageMismatch ? 255 : Math.Abs(bb - cb);
                int da = coverageMismatch ? 255 : Math.Abs(ba - ca);
                int pixelDelta = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
                channelDeltaTotal += dr + dg + db + da;
                maxChannelDelta = Math.Max(maxChannelDelta, pixelDelta);
                if (coverageMismatch || pixelDelta != 0)
                {
                    exact = false;
                }

                bool changed = coverageMismatch || pixelDelta > threshold;
                if (changed)
                {
                    changedPixels++;
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }

                int diffOffset = (y * diff.RowBytes) + (x * 4);
                WriteDiffPixel(
                    diffPixels,
                    diffOffset,
                    changed,
                    coverageMismatch,
                    pixelDelta,
                    br,
                    bg,
                    bb,
                    cr,
                    cg,
                    cb);
            }
        }

        VisualChangeBounds? bounds = changedPixels == 0
            ? null
            : new VisualChangeBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);
        return new PixelAnalysis(
            changedPixels,
            channelDeltaTotal,
            maxChannelDelta,
            bounds,
            exact);
    }

    private static void WriteDiffPixel(
        Span<byte> pixels,
        int offset,
        bool changed,
        bool coverageMismatch,
        int pixelDelta,
        byte br,
        byte bg,
        byte bb,
        byte cr,
        byte cg,
        byte cb)
    {
        if (coverageMismatch)
        {
            // Cyan marks canvas coverage introduced by differing dimensions.
            pixels[offset] = 0;
            pixels[offset + 1] = 200;
            pixels[offset + 2] = 255;
            pixels[offset + 3] = 255;
            return;
        }

        if (changed)
        {
            // Red heat increases with the largest channel delta.
            pixels[offset] = (byte)(96 + ((pixelDelta * 159) / 255));
            pixels[offset + 1] = (byte)(24 + ((255 - pixelDelta) / 12));
            pixels[offset + 2] = 72;
            pixels[offset + 3] = 255;
            return;
        }

        // Unchanged/within-threshold content remains visible as a quiet grayscale
        // reference so the colored changes retain spatial context.
        int averageLuma = (br + bg + bb + cr + cg + cb) / 6;
        byte gray = (byte)(24 + (averageLuma / 4));
        pixels[offset] = gray;
        pixels[offset + 1] = gray;
        pixels[offset + 2] = gray;
        pixels[offset + 3] = 255;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData? data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        if (data is null)
        {
            throw new InvalidOperationException("The visual diff could not be encoded as PNG.");
        }

        return data.ToArray();
    }

    private async Task<string> WriteUniqueDiffAsync(
        byte[] png,
        string baselinePath,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        string outputDirectory = GetSafeOutputDirectory();
        cancellationToken.ThrowIfCancellationRequested();

        string outputPath;
        do
        {
            outputPath = Path.Combine(outputDirectory, $"visual-diff-{Guid.NewGuid():N}.png");
        }
        while (PathEquals(outputPath, baselinePath) || PathEquals(outputPath, candidatePath));

        try
        {
            await using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await output.WriteAsync(png, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return outputPath;
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    private string GetSafeOutputDirectory()
    {
        string fullPath = AgentLocalPathGuard.ValidateDestinationDirectory(
            _paths.TempExportsDirectory,
            "Visual comparison temporary storage");
        Directory.CreateDirectory(fullPath);
        return AgentLocalPathGuard.ValidateExistingDirectory(
            fullPath,
            "Visual comparison temporary storage");
    }

    private static string BuildMarkdownSummary(
        ComparisonAnalysis analysis,
        int threshold)
    {
        string status = analysis.ExactMatch
            ? "Exact match"
            : analysis.ChangedPixelCount == 0
                ? "Within threshold (not exact)"
                : "Changes detected";
        string bounds = analysis.ChangeBounds is { } box
            ? $"x={box.X}, y={box.Y}, width={box.Width}, height={box.Height}"
            : "none";
        string percent = (analysis.ChangedPixelRatio * 100).ToString("0.####", CultureInfo.InvariantCulture);
        string mean = analysis.MeanChannelDelta.ToString("0.####", CultureInfo.InvariantCulture);

        var markdown = new StringBuilder(320);
        markdown.AppendLine("### Visual comparison");
        markdown.Append("- Result: **").Append(status).AppendLine("**");
        markdown.Append("- Size: ")
            .Append(analysis.BaselineWidth).Append('×').Append(analysis.BaselineHeight)
            .Append(" vs ")
            .Append(analysis.CandidateWidth).Append('×').Append(analysis.CandidateHeight)
            .Append(" (canvas ")
            .Append(analysis.CanvasWidth).Append('×').Append(analysis.CanvasHeight).AppendLine(")");
        markdown.Append("- Changed: ")
            .Append(analysis.ChangedPixelCount.ToString(CultureInfo.InvariantCulture))
            .Append('/')
            .Append(analysis.TotalPixelCount.ToString(CultureInfo.InvariantCulture))
            .Append(" pixels (").Append(percent).Append("%) at RGBA threshold > ")
            .Append(threshold.ToString(CultureInfo.InvariantCulture)).AppendLine();
        markdown.Append("- Delta: mean ").Append(mean).Append("/255; max ")
            .Append(analysis.MaxChannelDelta.ToString(CultureInfo.InvariantCulture)).AppendLine("/255");
        markdown.Append("- Bounds: ").AppendLine(bounds);
        markdown.Append("- Diff: local PNG generated (see `DiffImagePath`)");
        return markdown.ToString();
    }

    private static void ValidateOptions(VisualComparisonOptions options)
    {
        if (options.ChannelDeltaThreshold is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The channel delta threshold must be between 0 and 255.");
        }

        if (options.MaxDimension <= 0 || options.MaxDimension > HardMaxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MaxDimension must be between 1 and {HardMaxDimension:N0}.");
        }

        if (options.MaxPixelCount <= 0 || options.MaxPixelCount > HardMaxPixelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MaxPixelCount must be between 1 and {HardMaxPixelCount:N0}.");
        }

        if (options.MaxEncodedBytes <= 0 || options.MaxEncodedBytes > HardMaxEncodedBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MaxEncodedBytes must be between 1 and {HardMaxEncodedBytes:N0}.");
        }
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort: a failed/cancelled write must not hide its original error.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: a failed/cancelled write must not hide its original error.
        }
    }

    private sealed record ComparisonAnalysis(
        string BaselinePath,
        string CandidatePath,
        string BaselineSha256,
        string CandidateSha256,
        int BaselineWidth,
        int BaselineHeight,
        int CandidateWidth,
        int CandidateHeight,
        int CanvasWidth,
        int CanvasHeight,
        long TotalPixelCount,
        long ChangedPixelCount,
        double ChangedPixelRatio,
        double MeanChannelDelta,
        int MaxChannelDelta,
        VisualChangeBounds? ChangeBounds,
        bool ExactMatch,
        byte[] DiffPng);

    private sealed record DecodedImage(SKBitmap Bitmap, string Sha256);

    private readonly record struct PixelAnalysis(
        long ChangedPixelCount,
        long ChannelDeltaTotal,
        int MaxChannelDelta,
        VisualChangeBounds? ChangeBounds,
        bool ExactMatch);
}

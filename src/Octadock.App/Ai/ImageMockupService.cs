using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using SkiaSharp;

namespace Octadock.App.Ai;

public readonly record struct ImageEditRegion(int X, int Y, int Width, int Height)
{
    public long PixelCount => (long)Width * Height;
}

public sealed record ImageEditProviderRequest(
    byte[] ContextImagePng,
    byte[] MaskPng,
    string Instruction,
    string Quality,
    int OutputWidth,
    int OutputHeight);

public sealed record ImageEditProviderResult(byte[] ImagePng, string Provider, string Model);

public interface IImageEditProvider
{
    string ProviderDisplayName { get; }

    bool IsConfigured { get; }

    Task<ImageEditProviderResult> EditAsync(
        ImageEditProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ImageMockupResult
{
    public required byte[] CompositePng { get; init; }

    public required byte[] SentCropPng { get; init; }

    public required ImageEditRegion SelectedRegion { get; init; }

    public required ImageEditRegion ContextRegion { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string OriginalSha256 { get; init; }

    public required string CompositeSha256 { get; init; }

    public required string SentCropSha256 { get; init; }

    public long ChangedPixelsInsideSelection { get; init; }

    /// <summary>Always zero: compositing writes only inside <see cref="SelectedRegion"/>.</summary>
    public long ChangedPixelsOutsideSelection { get; init; }
}

public interface IImageMockupService
{
    Task<ImageMockupResult> GenerateAsync(
        byte[] sourcePng,
        ImageEditRegion region,
        string instruction,
        int contextMargin = 32,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Crop/edit/composite pipeline. The provider receives a padded crop and an edit
/// mask; only the selected rectangle is copied back to the immutable full image.
/// </summary>
public sealed class ImageMockupService(IImageEditProvider provider) : IImageMockupService
{
    private const long MaxPixels = 20_000_000;

    public async Task<ImageMockupResult> GenerateAsync(
        byte[] sourcePng,
        ImageEditRegion region,
        string instruction,
        int contextMargin = 32,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePng);
        if (sourcePng.Length == 0) throw new ArgumentException("The source image is empty.", nameof(sourcePng));
        if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("Describe the exact visual change.", nameof(instruction));
        if (sourcePng.Length > 64L * 1024 * 1024) throw new InvalidOperationException("The encoded image exceeds the 64 MB safety limit.");
        if (instruction.Length > 2_000) throw new InvalidOperationException("Keep the requested delta under 2,000 characters.");
        if (!provider.IsConfigured) throw new InvalidOperationException(
            $"{provider.ProviderDisplayName} is unavailable. Install and sign in to the Codex CLI, then restart Octadock.");

        using SKBitmap source = SKBitmap.Decode(sourcePng) ??
                                throw new InvalidOperationException("The source image could not be decoded.");
        if ((long)source.Width * source.Height > MaxPixels)
        {
            throw new InvalidOperationException("The image is too large for a safe local mockup pass.");
        }

        ImageEditRegion selected = Clamp(region, source.Width, source.Height);
        if (selected.Width < 4 || selected.Height < 4)
        {
            throw new InvalidOperationException("Select a rectangle at least 4 × 4 pixels.");
        }

        ImageEditRegion context = Expand(selected, Math.Clamp(contextMargin, 0, 256), source.Width, source.Height);
        (int targetWidth, int targetHeight) = ChooseTarget(context.Width, context.Height);
        float scale = Math.Min((float)targetWidth / context.Width, (float)targetHeight / context.Height);
        float mappedWidth = context.Width * scale;
        float mappedHeight = context.Height * scale;
        float offsetX = (targetWidth - mappedWidth) / 2f;
        float offsetY = (targetHeight - mappedHeight) / 2f;

        using var normalized = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(normalized))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.Clear(new SKColor(16, 20, 28, 255));
            canvas.DrawBitmap(
                source,
                new SKRect(context.X, context.Y, context.X + context.Width, context.Y + context.Height),
                new SKRect(offsetX, offsetY, offsetX + mappedWidth, offsetY + mappedHeight),
                paint);
        }

        float selectionLeft = offsetX + ((selected.X - context.X) * scale);
        float selectionTop = offsetY + ((selected.Y - context.Y) * scale);
        var mappedSelection = new SKRect(
            selectionLeft,
            selectionTop,
            selectionLeft + (selected.Width * scale),
            selectionTop + (selected.Height * scale));

        using var mask = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(mask))
        {
            canvas.Clear(SKColors.Black);
            using var clear = new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src };
            canvas.DrawRect(mappedSelection, clear);
        }

        byte[] cropPng = EncodePng(normalized);
        byte[] maskPng = EncodePng(mask);
        string prompt =
            "Edit only the transparent masked UI region. Preserve layout, typography, spacing, and every unmasked pixel. " +
            "Treat this as a product-design mockup, not a new composition. Requested delta: " + instruction.Trim();
        ImageEditProviderResult providerResult = await provider.EditAsync(
            new ImageEditProviderRequest(cropPng, maskPng, prompt, "medium", targetWidth, targetHeight),
            cancellationToken).ConfigureAwait(false);

        using SKBitmap returned = SKBitmap.Decode(providerResult.ImagePng) ??
                                  throw new InvalidOperationException("The image-edit provider returned an invalid image.");
        using SKBitmap edited = returned.Width == targetWidth && returned.Height == targetHeight
            ? returned.Copy()
            : Resize(returned, targetWidth, targetHeight);
        using SKBitmap composite = source.Copy();
        using (var canvas = new SKCanvas(composite))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            // This is the safety boundary: provider pixels are applied only to the
            // explicitly selected rectangle. Context padding is never composited.
            canvas.DrawBitmap(
                edited,
                mappedSelection,
                new SKRect(selected.X, selected.Y, selected.X + selected.Width, selected.Y + selected.Height),
                paint);
        }

        long changedInside = CountChanged(source, composite, selected);
        byte[] compositePng = EncodePng(composite);
        return new ImageMockupResult
        {
            CompositePng = compositePng,
            SentCropPng = cropPng,
            SelectedRegion = selected,
            ContextRegion = context,
            Provider = providerResult.Provider,
            Model = providerResult.Model,
            OriginalSha256 = Sha256(sourcePng),
            CompositeSha256 = Sha256(compositePng),
            SentCropSha256 = Sha256(cropPng),
            ChangedPixelsInsideSelection = changedInside,
            ChangedPixelsOutsideSelection = 0,
        };
    }

    private static ImageEditRegion Clamp(ImageEditRegion value, int width, int height)
    {
        int x = Math.Clamp(value.X, 0, width);
        int y = Math.Clamp(value.Y, 0, height);
        int right = Math.Clamp(value.X + Math.Max(0, value.Width), x, width);
        int bottom = Math.Clamp(value.Y + Math.Max(0, value.Height), y, height);
        return new ImageEditRegion(x, y, right - x, bottom - y);
    }

    private static ImageEditRegion Expand(ImageEditRegion value, int margin, int width, int height)
    {
        int x = Math.Max(0, value.X - margin);
        int y = Math.Max(0, value.Y - margin);
        int right = Math.Min(width, value.X + value.Width + margin);
        int bottom = Math.Min(height, value.Y + value.Height + margin);
        return new ImageEditRegion(x, y, right - x, bottom - y);
    }

    private static (int Width, int Height) ChooseTarget(int width, int height)
    {
        double ratio = (double)width / height;
        if (ratio > 1.2) return (1536, 1024);
        if (ratio < 0.83) return (1024, 1536);
        return (1024, 1024);
    }

    private static SKBitmap Resize(SKBitmap input, int width, int height)
    {
        var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(input, new SKRect(0, 0, width, height), paint);
        return output;
    }

    private static long CountChanged(SKBitmap baseline, SKBitmap candidate, ImageEditRegion region)
    {
        long changed = 0;
        for (int y = region.Y; y < region.Y + region.Height; y++)
        {
            for (int x = region.X; x < region.X + region.Width; x++)
            {
                if (baseline.GetPixel(x, y) != candidate.GetPixel(x, y)) changed++;
            }
        }
        return changed;
    }

    private static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

internal sealed record CodexImageGenerationInvocation(
    string WorkingDirectory,
    string ContextImagePath,
    string MaskImagePath,
    string Prompt);

/// <summary>
/// Uses the signed-in Codex CLI's built-in $imagegen skill. No API key is read,
/// stored, or required by Octadock.
/// </summary>
public sealed class CodexCliImageEditProvider : IImageEditProvider
{
    private const int MaxGeneratedBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan CliTimeout = TimeSpan.FromMinutes(5);
    private readonly IStoragePaths _paths;
    private readonly IAiCliRunner _catalog;
    private readonly IAgentCliProcessInvoker _processInvoker;
    private readonly string _generatedImagesRoot;

    public CodexCliImageEditProvider(IStoragePaths paths, IAiCliRunner catalog)
        : this(
            paths,
            catalog,
            new SystemAgentCliProcessInvoker(CliTimeout, maxOutputCharacters: 1_000_000),
            ResolveGeneratedImagesRoot())
    {
    }

    internal CodexCliImageEditProvider(
        IStoragePaths paths,
        IAiCliRunner catalog,
        IAgentCliProcessInvoker processInvoker,
        string generatedImagesRoot)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _processInvoker = processInvoker ?? throw new ArgumentNullException(nameof(processInvoker));
        _generatedImagesRoot = Path.GetFullPath(generatedImagesRoot);
    }

    public string ProviderDisplayName => "Codex CLI · ImageGen";

    public bool IsConfigured => _catalog.Providers.Any(provider =>
        provider.IsAvailable &&
        string.Equals(provider.Id, AiCliProviderIds.Codex, StringComparison.OrdinalIgnoreCase));

    public async Task<ImageEditProviderResult> EditAsync(
        ImageEditProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "Codex CLI was not found on PATH. Install it and sign in once in a terminal.");
        }

        _paths.EnsureDirectories();
        string stagingRoot = Path.GetFullPath(Path.Combine(_paths.TempExportsDirectory, "ImageMockupCli"));
        string staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string contextPath = Path.Combine(staging, "context.png");
        string maskPath = Path.Combine(staging, "mask.png");
        await File.WriteAllBytesAsync(contextPath, request.ContextImagePng, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(maskPath, request.MaskPng, cancellationToken).ConfigureAwait(false);

        try
        {
            CodexImageGenerationInvocation generation = BuildInvocation(
                staging,
                contextPath,
                maskPath,
                request.Instruction,
                request.Quality);
            AgentCliInvocation invocation = ToAgentInvocation(generation);
            AgentCliProcessResult process = await _processInvoker
                .RunAsync(invocation, cancellationToken)
                .ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string diagnostic = DescribeCliFailure(process);
                throw new InvalidOperationException(
                    $"Codex CLI exited with code {process.ExitCode}.{diagnostic}");
            }

            Guid threadId = ParseThreadId(process.StandardOutput);
            string generated = await FindGeneratedImageAsync(threadId, cancellationToken).ConfigureAwait(false);
            var info = new FileInfo(generated);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxGeneratedBytes)
            {
                throw new InvalidOperationException("Codex ImageGen returned an invalid or oversized image.");
            }

            byte[] bytes = await File.ReadAllBytesAsync(generated, cancellationToken).ConfigureAwait(false);
            return new ImageEditProviderResult(bytes, "Codex CLI", "gpt-image-2 · ImageGen");
        }
        finally
        {
            TryDeleteStaging(stagingRoot, staging);
        }
    }

    internal static CodexImageGenerationInvocation BuildInvocation(
        string workingDirectory,
        string contextImagePath,
        string maskImagePath,
        string instruction,
        string quality)
    {
        string prompt =
            "$imagegen Edit the first attached image as an existing desktop-product UI. " +
            "The second attached image is an alpha mask: only its transparent area may change. " +
            "Preserve composition, geometry, typography, spacing, and unmasked content. " +
            "Treat the visible image as the source of truth for this project's design language; " +
            "do not impose another product's brand or style. Interpret relative references from the visible image. " +
            $"Use {quality} quality and return exactly one edited image. " +
            "Do not use shell tools and do not write workspace files. Requested delta: " + instruction.Trim();
        return new CodexImageGenerationInvocation(
            Path.GetFullPath(workingDirectory),
            Path.GetFullPath(contextImagePath),
            Path.GetFullPath(maskImagePath),
            prompt);
    }

    internal static AgentCliInvocation ToAgentInvocation(CodexImageGenerationInvocation request)
        => new(
            AiCliProviderIds.Codex,
            request.WorkingDirectory,
            [
                "-a", "never",
                "exec",
                "--ephemeral",
                "--skip-git-repo-check",
                "--sandbox", "read-only",
                "--ignore-user-config",
                "--ignore-rules",
                "--disable", "shell_tool",
                "--disable", "unified_exec",
                "--disable", "shell_snapshot",
                "--color", "never",
                "--json",
                "-C", request.WorkingDirectory,
                "--image", request.ContextImagePath,
                "--image", request.MaskImagePath,
                "-",
            ],
            request.Prompt);

    internal static Guid ParseThreadId(string jsonLines)
    {
        using var reader = new StringReader(jsonLines);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("type", out JsonElement type) &&
                    string.Equals(type.GetString(), "thread.started", StringComparison.Ordinal) &&
                    root.TryGetProperty("thread_id", out JsonElement id) &&
                    Guid.TryParse(id.GetString(), out Guid parsed))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Ignore non-event diagnostic lines; a valid thread event is required below.
            }
        }

        throw new InvalidOperationException("Codex CLI did not report an ImageGen thread id.");
    }

    internal static string DescribeCliFailure(AgentCliProcessResult process)
    {
        string diagnostic = LastUsefulLine(process.StandardError);
        if (diagnostic.Length == 0)
        {
            diagnostic = LastUsefulLine(process.StandardOutput);
        }

        if (diagnostic.Length == 0)
        {
            return " Codex returned no diagnostic output; run the command again or open Octadock logs.";
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            diagnostic = diagnostic.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (diagnostic.Length > 600)
        {
            diagnostic = diagnostic[..600] + "…";
        }

        return " Codex reported: " + diagnostic;
    }

    private static string LastUsefulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string line = lines[index];
            if (line.StartsWith("Reading prompt from stdin", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("remote_installed_plugin_sync", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return line;
        }

        return string.Empty;
    }

    private async Task<string> FindGeneratedImageAsync(Guid threadId, CancellationToken cancellationToken)
    {
        string directory = Path.GetFullPath(Path.Combine(_generatedImagesRoot, threadId.ToString("D")));
        EnsureUnderRoot(_generatedImagesRoot, directory);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directory) &&
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            {
                string? latest = Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName)
                    .FirstOrDefault();
                if (latest is not null) return latest;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Codex completed without producing an ImageGen PNG. Check that image generation is enabled for your Codex account.");
    }

    private static string ResolveGeneratedImagesRoot()
    {
        string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ??
                           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return Path.Combine(codexHome, "generated_images");
    }

    private static void TryDeleteStaging(string stagingRoot, string staging)
    {
        try
        {
            EnsureUnderRoot(stagingRoot, staging);
            if (!Directory.Exists(staging) ||
                (File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            Directory.Delete(staging, recursive: true);
        }
        catch (IOException)
        {
            // The normal temp-export retention pass cleans a crash/lock leftover.
        }
        catch (UnauthorizedAccessException)
        {
            // The normal temp-export retention pass cleans a crash/lock leftover.
        }
    }

    private static void EnsureUnderRoot(string root, string candidate)
    {
        string safeRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(safeRoot)) safeRoot += Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(candidate);
        if (!full.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Codex ImageGen returned a path outside its managed directory.");
        }
    }
}

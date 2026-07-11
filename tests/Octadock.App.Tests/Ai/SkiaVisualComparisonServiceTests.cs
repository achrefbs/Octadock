using System.IO;
using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Services;
using SkiaSharp;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class SkiaVisualComparisonServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "octadock-visual-comparison-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Exact_match_reports_zero_deltas_and_writes_a_new_png_without_touching_inputs()
    {
        string baseline = WritePng("baseline.png", 3, 2, (_, _) => new SKColor(12, 34, 56, 255));
        string candidate = WritePng("candidate.png", 3, 2, (_, _) => new SKColor(12, 34, 56, 255));
        byte[] baselineBefore = await File.ReadAllBytesAsync(baseline);
        byte[] candidateBefore = await File.ReadAllBytesAsync(candidate);
        var service = CreateService();

        VisualComparisonResult result = await service.CompareAsync(baseline, candidate);
        VisualComparisonResult repeated = await service.CompareAsync(baseline, candidate);

        result.ExactMatch.Should().BeTrue();
        result.ChangedPixelCount.Should().Be(0);
        result.ChangedPixelRatio.Should().Be(0);
        result.MeanChannelDelta.Should().Be(0);
        result.MaxChannelDelta.Should().Be(0);
        result.ChangeBounds.Should().BeNull();
        result.CanvasWidth.Should().Be(3);
        result.CanvasHeight.Should().Be(2);
        result.MarkdownSummary.Should().Contain("Exact match").And.Contain("0/6 pixels");
        File.Exists(result.DiffImagePath).Should().BeTrue();
        Path.GetExtension(result.DiffImagePath).Should().Be(".png");
        Path.GetDirectoryName(result.DiffImagePath).Should().Be(
            Path.Combine(_root, StoragePaths.TempExportsFolder));
        result.DiffImagePath.Should().NotBe(baseline).And.NotBe(candidate);
        repeated.DiffImagePath.Should().NotBe(result.DiffImagePath, "outputs use create-new paths");
        repeated.MarkdownSummary.Should().Be(result.MarkdownSummary, "the summary excludes the random temp filename");
        (await File.ReadAllBytesAsync(repeated.DiffImagePath)).Should().Equal(
            await File.ReadAllBytesAsync(result.DiffImagePath),
            "the same decoded inputs and options produce the same diff pixels");
        (await File.ReadAllBytesAsync(baseline)).Should().Equal(baselineBefore);
        (await File.ReadAllBytesAsync(candidate)).Should().Equal(candidateBefore);

        using SKBitmap diff = SKBitmap.Decode(result.DiffImagePath);
        diff.Width.Should().Be(3);
        diff.Height.Should().Be(2);
    }

    [Fact]
    public async Task Changed_region_reports_count_ratio_delta_and_tight_bounding_box()
    {
        string baseline = WritePng("black.png", 4, 4, (_, _) => SKColors.Black);
        string candidate = WritePng(
            "region.png",
            4,
            4,
            (x, y) => x is 1 or 2 && y is 1 or 2 ? SKColors.White : SKColors.Black);

        VisualComparisonResult result = await CreateService().CompareAsync(baseline, candidate);

        result.ExactMatch.Should().BeFalse();
        result.TotalPixelCount.Should().Be(16);
        result.ChangedPixelCount.Should().Be(4);
        result.ChangedPixelRatio.Should().BeApproximately(0.25, 0.000001);
        result.MeanChannelDelta.Should().BeApproximately(47.8125, 0.000001);
        result.MaxChannelDelta.Should().Be(255);
        result.ChangeBounds.Should().Be(new VisualChangeBounds(1, 1, 2, 2));
        result.MarkdownSummary.Should().Contain("Changes detected").And.Contain("width=2, height=2");
        File.Exists(result.DiffImagePath).Should().BeTrue();
    }

    [Fact]
    public async Task Different_dimensions_use_a_top_left_common_canvas_without_distortion()
    {
        string baseline = WritePng("wide.png", 2, 1, (_, _) => SKColors.Transparent);
        string candidate = WritePng("tall.png", 1, 2, (_, _) => SKColors.Transparent);

        VisualComparisonResult result = await CreateService().CompareAsync(baseline, candidate);

        result.CanvasWidth.Should().Be(2);
        result.CanvasHeight.Should().Be(2);
        result.TotalPixelCount.Should().Be(4);
        result.ChangedPixelCount.Should().Be(2, "coverage changes count even when uncovered pixels are transparent");
        result.ChangedPixelRatio.Should().BeApproximately(0.5, 0.000001);
        result.MeanChannelDelta.Should().BeApproximately(127.5, 0.000001);
        result.MaxChannelDelta.Should().Be(255);
        result.ChangeBounds.Should().Be(new VisualChangeBounds(0, 0, 2, 2));
        result.ExactMatch.Should().BeFalse();
    }

    [Fact]
    public async Task Channel_threshold_filters_small_deltas_but_does_not_claim_an_exact_match()
    {
        string baseline = WritePng("threshold-base.png", 1, 1, (_, _) => new SKColor(10, 10, 10, 255));
        string candidate = WritePng("threshold-candidate.png", 1, 1, (_, _) => new SKColor(15, 10, 10, 255));
        var service = CreateService();

        VisualComparisonResult within = await service.CompareAsync(
            baseline,
            candidate,
            new VisualComparisonOptions { ChannelDeltaThreshold = 5 });
        VisualComparisonResult changed = await service.CompareAsync(
            baseline,
            candidate,
            new VisualComparisonOptions { ChannelDeltaThreshold = 4 });

        within.ExactMatch.Should().BeFalse();
        within.ChangedPixelCount.Should().Be(0);
        within.ChangeBounds.Should().BeNull();
        within.MeanChannelDelta.Should().BeApproximately(1.25, 0.000001);
        within.MaxChannelDelta.Should().Be(5);
        within.MarkdownSummary.Should().Contain("Within threshold");
        changed.ChangedPixelCount.Should().Be(1);
        changed.ChangeBounds.Should().Be(new VisualChangeBounds(0, 0, 1, 1));
        changed.DiffImagePath.Should().NotBe(within.DiffImagePath, "every result uses a create-new artifact");
    }

    [Fact]
    public async Task Rejects_missing_unsupported_corrupt_and_unc_inputs()
    {
        Directory.CreateDirectory(_root);
        string valid = WritePng("valid.png", 1, 1, (_, _) => SKColors.Black);
        string unsupported = Path.Combine(_root, "image.txt");
        await File.WriteAllBytesAsync(unsupported, await File.ReadAllBytesAsync(valid));
        string corrupt = Path.Combine(_root, "corrupt.png");
        await File.WriteAllTextAsync(corrupt, "not a png");
        var service = CreateService();

        Func<Task> missing = () => service.CompareAsync(Path.Combine(_root, "missing.png"), valid);
        Func<Task> unsupportedFormat = () => service.CompareAsync(unsupported, valid);
        Func<Task> corruptImage = () => service.CompareAsync(corrupt, valid);
        Func<Task> unc = () => service.CompareAsync(@"\\server\share\image.png", valid);

        await missing.Should().ThrowAsync<FileNotFoundException>();
        await unsupportedFormat.Should().ThrowAsync<NotSupportedException>().WithMessage("*supported raster format*");
        await corruptImage.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unsupported*corrupt*");
        await unc.Should().ThrowAsync<InvalidOperationException>().WithMessage("*local drive*UNC*");
    }

    [Fact]
    public async Task Rejects_images_or_common_canvases_above_configured_safe_limits()
    {
        string threeByThree = WritePng("three.png", 3, 3, (_, _) => SKColors.Black);
        string threeByOne = WritePng("wide-limit.png", 3, 1, (_, _) => SKColors.Black);
        string oneByThree = WritePng("tall-limit.png", 1, 3, (_, _) => SKColors.Black);
        var service = CreateService();

        Func<Task> dimension = () => service.CompareAsync(
            threeByThree,
            threeByThree,
            new VisualComparisonOptions { MaxDimension = 2 });
        Func<Task> pixels = () => service.CompareAsync(
            threeByThree,
            threeByThree,
            new VisualComparisonOptions { MaxPixelCount = 8 });
        Func<Task> canvas = () => service.CompareAsync(
            threeByOne,
            oneByThree,
            new VisualComparisonOptions { MaxPixelCount = 8 });

        await dimension.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dimension*limited*");
        await pixels.Should().ThrowAsync<InvalidOperationException>().WithMessage("*9 pixels*safe limit is 8*");
        await canvas.Should().ThrowAsync<InvalidOperationException>().WithMessage("*comparison canvas*9 pixels*safe limit is 8*");
    }

    [Fact]
    public async Task Honors_pre_cancelled_requests_without_creating_an_output()
    {
        string baseline = WritePng("cancel-base.png", 2, 2, (_, _) => SKColors.Black);
        string candidate = WritePng("cancel-candidate.png", 2, 2, (_, _) => SKColors.White);
        var service = CreateService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> compare = () => service.CompareAsync(
            baseline,
            candidate,
            cancellationToken: cancellation.Token);

        await compare.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Path.Combine(_root, StoragePaths.TempExportsFolder)).Should().BeFalse();
    }

    private SkiaVisualComparisonService CreateService()
        => new(new StoragePaths(_root));

    private string WritePng(
        string fileName,
        int width,
        int height,
        Func<int, int, SKColor> colorAt)
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, fileName);
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, colorAt(x, y));
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        using FileStream output = File.Create(path);
        data.SaveTo(output);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort test cleanup.
        }
    }
}

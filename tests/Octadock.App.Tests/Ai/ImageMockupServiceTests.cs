using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Services;
using SkiaSharp;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class ImageMockupServiceTests
{
    [Fact]
    public async Task Provider_pixels_are_composited_only_inside_selected_region()
    {
        byte[] source = CreateSource(100, 80);
        var provider = new PaintEverythingProvider();
        var service = new ImageMockupService(provider);
        var region = new ImageEditRegion(20, 15, 30, 20);

        ImageMockupResult result = await service.GenerateAsync(
            source,
            region,
            "Make the selected card green.",
            contextMargin: 12);

        result.ChangedPixelsOutsideSelection.Should().Be(0);
        result.ChangedPixelsInsideSelection.Should().Be(region.PixelCount);
        result.ContextRegion.Should().Be(new ImageEditRegion(8, 3, 54, 44));
        provider.Request.Should().NotBeNull();
        provider.Request!.MaskPng.Should().NotBeEmpty();
        provider.Request.Instruction.Should().Contain("Make the selected card green");

        using SKBitmap before = SKBitmap.Decode(source)!;
        using SKBitmap after = SKBitmap.Decode(result.CompositePng)!;
        for (int y = 0; y < before.Height; y++)
        {
            for (int x = 0; x < before.Width; x++)
            {
                bool inside = x >= region.X && x < region.X + region.Width &&
                              y >= region.Y && y < region.Y + region.Height;
                if (inside)
                {
                    after.GetPixel(x, y).Green.Should().BeGreaterThan((byte)200);
                }
                else
                {
                    after.GetPixel(x, y).Should().Be(before.GetPixel(x, y));
                }
            }
        }
    }

    [Fact]
    public async Task Missing_provider_configuration_fails_before_egress()
    {
        var provider = new PaintEverythingProvider { IsConfigured = false };
        var service = new ImageMockupService(provider);

        Func<Task> act = () => service.GenerateAsync(
            CreateSource(20, 20),
            new ImageEditRegion(2, 2, 10, 10),
            "Change it");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unavailable*");
        provider.Request.Should().BeNull();
    }

    [Fact]
    public async Task Codex_cli_provider_uses_reported_thread_image_without_an_api_key()
    {
        string root = Path.Combine(Path.GetTempPath(), "OctadockCliImageTests", Guid.NewGuid().ToString("N"));
        string generatedRoot = Path.Combine(root, "generated_images");
        var paths = new StoragePaths(root);
        var catalog = new FakeCatalog(available: true);
        Guid threadId = Guid.NewGuid();
        byte[] generated = CreateSource(64, 64);
        var invoker = new FakeProcessInvoker(invocation =>
        {
            string outputDirectory = Path.Combine(generatedRoot, threadId.ToString("D"));
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "result.png"), generated);
            return new AgentCliProcessResult(
                0,
                $"{{\"type\":\"thread.started\",\"thread_id\":\"{threadId:D}\"}}\n" +
                "{\"type\":\"turn.completed\"}");
        });
        var provider = new CodexCliImageEditProvider(paths, catalog, invoker, generatedRoot);

        try
        {
            ImageEditProviderResult result = await provider.EditAsync(new ImageEditProviderRequest(
                CreateSource(32, 32),
                CreateSource(32, 32),
                "Make the card quieter.",
                "medium",
                1024,
                1024));

            result.ImagePng.Should().Equal(generated);
            result.Provider.Should().Be("Codex CLI");
            invoker.Invocation.Should().NotBeNull();
            invoker.Invocation!.Arguments.Should().ContainInOrder("--sandbox", "read-only");
            invoker.Invocation.Arguments.Count(value => value == "--image").Should().Be(2);
            invoker.Invocation.StandardInput.Should().Contain("$imagegen");
            invoker.Invocation.StandardInput.Should().Contain("Make the card quieter");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Codex_cli_provider_rejects_output_without_a_structured_thread_id()
    {
        Action act = () => CodexCliImageEditProvider.ParseThreadId("not-json\n{\"type\":\"turn.completed\"}");

        act.Should().Throw<InvalidOperationException>().WithMessage("*thread id*");
    }

    [Fact]
    public void Codex_cli_failure_uses_the_real_bounded_diagnostic_instead_of_guessing_about_sign_in()
    {
        var process = new AgentCliProcessResult(
            1,
            "{\"type\":\"turn.failed\"}",
            "Reading prompt from stdin...\nERROR: Image generation is temporarily unavailable.");

        string diagnostic = CodexCliImageEditProvider.DescribeCliFailure(process);

        diagnostic.Should().Contain("Image generation is temporarily unavailable")
            .And.NotContain("sign-in");
    }

    private static byte[] CreateSource(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(x % 200), (byte)(y % 200), 90, 255));
            }
        }
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class PaintEverythingProvider : IImageEditProvider
    {
        public string ProviderDisplayName => "Test provider";

        public bool IsConfigured { get; init; } = true;

        public ImageEditProviderRequest? Request { get; private set; }

        public Task<ImageEditProviderResult> EditAsync(
            ImageEditProviderRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            using SKBitmap bitmap = SKBitmap.Decode(request.ContextImagePng)!;
            using (var canvas = new SKCanvas(bitmap)) canvas.Clear(new SKColor(0, 255, 0, 255));
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            return Task.FromResult(new ImageEditProviderResult(data.ToArray(), "Test", "paint-all"));
        }
    }

    private sealed class FakeCatalog(bool available) : IAiCliRunner
    {
        public IReadOnlyList<AiCliProviderDescriptor> Providers { get; } =
        [
            new(
                AiCliProviderIds.Codex,
                "Codex",
                "signed-in Codex CLI",
                available,
                available ? null : "Codex CLI unavailable"),
        ];

        public Task<string> RunAsync(
            string providerId,
            string outboundText,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeProcessInvoker(
        Func<AgentCliInvocation, AgentCliProcessResult> run) : IAgentCliProcessInvoker
    {
        public AgentCliInvocation? Invocation { get; private set; }

        public Task<AgentCliProcessResult> RunAsync(
            AgentCliInvocation invocation,
            CancellationToken cancellationToken)
        {
            Invocation = invocation;
            return Task.FromResult(run(invocation));
        }
    }
}

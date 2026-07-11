using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Commands;
using Octadock.Platform.Windows.Ocr;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Ocr;

public sealed class WindowsMediaOcrProviderSafetyTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "Octadock-WindowsOcrTests",
        Guid.NewGuid().ToString("N"));

    public WindowsMediaOcrProviderSafetyTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task Empty_file_is_rejected_before_decoder_or_engine_creation()
    {
        string path = Path.Combine(_tempDirectory, "empty.png");
        await File.WriteAllBytesAsync(path, []);
        var provider = new WindowsMediaOcrProvider(NullLogger<WindowsMediaOcrProvider>.Instance);

        Func<Task> act = () => provider.RecognizeAsync(path, OcrTextMode.Lines);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_allocation_or_decode()
    {
        string path = Path.Combine(_tempDirectory, "oversized.png");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((64L * 1024 * 1024) + 1);
        }

        var provider = new WindowsMediaOcrProvider(NullLogger<WindowsMediaOcrProvider>.Instance);

        Func<Task> act = () => provider.RecognizeAsync(path, OcrTextMode.Lines);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Pre_cancelled_request_does_not_touch_the_path()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = new WindowsMediaOcrProvider(NullLogger<WindowsMediaOcrProvider>.Instance);

        Func<Task> act = () => provider.RecognizeAsync(
            @"\\server\share\never-touch.png",
            OcrTextMode.Lines,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

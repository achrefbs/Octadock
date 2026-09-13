using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Services;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

public sealed class LocalModelImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "octadock-import-test", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_requires_the_exact_digest_before_publishing_the_model(bool corrupt)
    {
        string source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        byte[] expected = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(Path.Combine(source, "tokens.txt"), corrupt ? [4, 3, 2, 1] : expected);
        using var store = new ParakeetModelStore(new StoragePaths(Path.Combine(_root, "data")),
            NullLogger<ParakeetModelStore>.Instance,
            [new("tokens.txt", expected.Length, Convert.ToHexString(SHA256.HashData(expected)))]);
        Func<Task> import = () => store.ImportAsync(null, source, null, CancellationToken.None);
        if (corrupt)
        {
            await import.Should().ThrowAsync<InvalidDataException>();
            store.IsComplete(null).Should().BeFalse();
        }
        else
        {
            await import();
            store.IsComplete(null).Should().BeTrue();
        }
        Directory.EnumerateDirectories(Path.Combine(_root, "data", "models"), "*.import-*").Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_model_is_reported_without_attempting_a_download()
    {
        using var store = new ParakeetModelStore(new StoragePaths(_root), NullLogger<ParakeetModelStore>.Instance);
        await store.Invoking(s => s.EnsureAsync(null, null, CancellationToken.None)).Should().ThrowAsync<FileNotFoundException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelled_import_never_publishes_partial_files()
    {
        using var store = new ParakeetModelStore(new StoragePaths(_root), NullLogger<ParakeetModelStore>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await store.Invoking(s => s.ImportAsync(null, _root, null, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    [Fact]
    public void Providers_tolerate_duplicate_disposal_from_di_aliases()
    {
        using var store = new ParakeetModelStore(new StoragePaths(_root), NullLogger<ParakeetModelStore>.Instance);
        var parakeet = new ParakeetSttProvider(store, NullLogger<ParakeetSttProvider>.Instance);
        var whisper = new WhisperSttProvider(new StoragePaths(_root), NullLogger<WhisperSttProvider>.Instance);
        parakeet.Dispose(); whisper.Dispose();
        parakeet.Invoking(p => p.Dispose()).Should().NotThrow();
        whisper.Invoking(p => p.Dispose()).Should().NotThrow();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

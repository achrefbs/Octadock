using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

public sealed class ParakeetModelStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "octadock-tests", Path.GetRandomFileName());
    private readonly List<ParakeetModelStore> _stores = [];

    private ParakeetModelStore CreateStore()
    {
        var store = new ParakeetModelStore(
            new FakeStoragePaths(_root), NullLogger<ParakeetModelStore>.Instance);
        _stores.Add(store);
        return store;
    }

    [Fact]
    public void NormalizeModel_maps_everything_onto_the_single_supported_variant()
    {
        ParakeetModelStore.NormalizeModel(null).Should().Be(SpeechSettings.DefaultParakeetModel);
        ParakeetModelStore.NormalizeModel("anything").Should().Be(SpeechSettings.DefaultParakeetModel);
    }

    [Fact]
    public void PathsFor_places_the_four_files_under_the_models_directory()
    {
        ParakeetModelPaths paths = CreateStore().PathsFor(null);

        string expectedDirectory = Path.Combine(_root, "models", SpeechSettings.DefaultParakeetModel);
        paths.Encoder.Should().Be(Path.Combine(expectedDirectory, "encoder.int8.onnx"));
        paths.Decoder.Should().Be(Path.Combine(expectedDirectory, "decoder.int8.onnx"));
        paths.Joiner.Should().Be(Path.Combine(expectedDirectory, "joiner.int8.onnx"));
        paths.Tokens.Should().Be(Path.Combine(expectedDirectory, "tokens.txt"));
    }

    [Fact]
    public void IsComplete_is_false_when_files_are_missing_or_truncated()
    {
        ParakeetModelStore store = CreateStore();
        store.IsComplete(null).Should().BeFalse();

        // Right names but wrong sizes must still read as incomplete: a
        // truncated encoder would otherwise crash the recognizer build.
        ParakeetModelPaths paths = store.PathsFor(null);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Encoder)!);
        File.WriteAllText(paths.Encoder, "stub");
        File.WriteAllText(paths.Decoder, "stub");
        File.WriteAllText(paths.Joiner, "stub");
        File.WriteAllText(paths.Tokens, "stub");

        store.IsComplete(null).Should().BeFalse();
    }

    [Fact]
    public void Delete_removes_the_model_directory()
    {
        ParakeetModelStore store = CreateStore();
        ParakeetModelPaths paths = store.PathsFor(null);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Encoder)!);
        File.WriteAllText(paths.Tokens, "stub");

        store.Delete(null);

        Directory.Exists(Path.GetDirectoryName(paths.Encoder)!).Should().BeFalse();
        store.Invoking(s => s.Delete(null)).Should().NotThrow("deleting twice must be harmless");
    }

    [Fact]
    public void TotalBytes_reports_the_full_manifest_size()
    {
        // Guards against a manifest edit accidentally dropping a file: the
        // pinned model is ~640 MB across four files.
        CreateStore().TotalBytes.Should().BeGreaterThan(600L * 1024 * 1024);
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_rejects_new_downloads()
    {
        ParakeetModelStore store = CreateStore();

        store.Dispose();
        store.Dispose();

        Func<Task> ensure = () => store.EnsureAsync(null, null, CancellationToken.None);
        await ensure.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Streaming_copy_rejects_a_chunked_response_before_exceeding_the_pinned_limit()
    {
        byte[] body = Enumerable.Range(0, 33).Select(index => (byte)index).ToArray();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(body),
        };
        await using var destination = new MemoryStream();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        Func<Task> copy = () => ParakeetModelStore.CopyWithStallWatchdogAsync(
            response,
            destination,
            hash,
            alreadyCopied: 0,
            maximumBytes: 32,
            _ => { },
            CancellationToken.None);

        await copy.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*exceeds the pinned 32-byte limit*");
        destination.Length.Should().Be(0, "an oversized first chunk must not reach disk");
    }

    [Fact]
    public void Digest_check_rejects_same_length_tampering()
    {
        string path = Path.Combine(_root, "tokens.txt");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "trusted");
        string digest = Convert.ToHexString(SHA256.HashData("trusted"u8.ToArray())).ToLowerInvariant();

        ParakeetModelStore.FileHasExpectedDigest(path, digest).Should().BeTrue();

        File.WriteAllText(path, "untrust");
        new FileInfo(path).Length.Should().Be("trusted"u8.Length);
        ParakeetModelStore.FileHasExpectedDigest(path, digest).Should().BeFalse();
    }

    [Fact]
    public async Task Streaming_copy_preserves_a_response_that_exactly_matches_the_pinned_limit()
    {
        byte[] body = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(body)),
        };
        await using var destination = new MemoryStream();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await ParakeetModelStore.CopyWithStallWatchdogAsync(
            response,
            destination,
            hash,
            alreadyCopied: 0,
            maximumBytes: body.Length,
            _ => { },
            CancellationToken.None);

        destination.ToArray().Should().Equal(body);
    }

    public void Dispose()
    {
        foreach (ParakeetModelStore store in _stores)
        {
            store.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeStoragePaths(string root) : IStoragePaths
    {
        public string RootDirectory => root;

        public string CapturesDirectory => Path.Combine(root, "Captures");

        public string ProjectsDirectory => Path.Combine(root, "Projects");

        public string RecordingsDirectory => Path.Combine(root, "Recordings");

        public string ThumbnailsDirectory => Path.Combine(root, "Thumbnails");

        public string TempExportsDirectory => Path.Combine(root, "TempExports");

        public string LogsDirectory => Path.Combine(root, "Logs");

        public string DatabasePath => Path.Combine(root, "octadock.db");

        public void EnsureDirectories() => Directory.CreateDirectory(root);

        public string ToAbsolute(string relativePath) => Path.Combine(root, relativePath);

        public string ToRelative(string absolutePath) => absolutePath;

        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Captures", $"{id}{extension}");

        public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id}.jpg");

        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Recordings", $"{id}{extension}");

        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => Path.Combine("Clipboard", $"{id}.png");
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

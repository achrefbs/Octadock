using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// Download-pipeline hardening through the store's internal seam (injectable
/// HTTP + tiny manifest): corrupt payloads are deleted and retryable, partial
/// downloads resume with HTTP ranges, and tampered on-disk models are verified
/// and quarantined instead of crashing the recognizer forever.
/// </summary>
public sealed class ParakeetModelStoreDownloadTests : IDisposable
{
    private const string TestBaseUrl = "https://stt-tests.invalid/";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "octadock-tests", Path.GetRandomFileName());
    private readonly List<ParakeetModelStore> _stores = [];

    private static byte[] EncoderBytes { get; } = Pattern(300, seed: 11);

    private static byte[] DecoderBytes { get; } = Pattern(64, seed: 22);

    private static byte[] TokensBytes { get; } = Pattern(32, seed: 33);

    private static ParakeetModelStore.ManifestFile[] TinyManifest() =>
    [
        new("encoder.int8.onnx", EncoderBytes.Length, Sha256(EncoderBytes)),
        new("decoder.int8.onnx", DecoderBytes.Length, Sha256(DecoderBytes)),
        new("tokens.txt", TokensBytes.Length, Sha256(TokensBytes)),
    ];

    private static byte[] Pattern(int length, int seed)
    {
        var bytes = new byte[length];
        var random = new Random(seed);
        random.NextBytes(bytes);
        return bytes;
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private ParakeetModelStore CreateStore(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var store = new ParakeetModelStore(
            new FakeStoragePaths(_root),
            NullLogger<ParakeetModelStore>.Instance,
            new StubHandler(responder),
            TestBaseUrl,
            TinyManifest());
        _stores.Add(store);
        return store;
    }

    private static HttpResponseMessage Serve(HttpRequestMessage request, byte[] bytes)
    {
        // Honors an HTTP range request like the real CDN does.
        if (request.Headers.Range is { } range && range.Ranges.Count == 1)
        {
            long from = range.Ranges.Single().From!.Value;
            var remainder = bytes[(int)from..];
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(remainder),
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
    }

    private static HttpResponseMessage ServeByName(HttpRequestMessage request)
    {
        string name = request.RequestUri!.AbsolutePath.Split('/').Last();
        return name switch
        {
            "encoder.int8.onnx" => Serve(request, EncoderBytes),
            "decoder.int8.onnx" => Serve(request, DecoderBytes),
            "tokens.txt" => Serve(request, TokensBytes),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    [Fact]
    public async Task Corrupt_download_is_deleted_and_the_retry_succeeds()
    {
        // First attempt serves right-sized garbage for the encoder: verification
        // must reject it, remove the partial, and leave nothing behind.
        int encoderAttempts = 0;
        ParakeetModelStore store = CreateStore(request =>
        {
            string name = request.RequestUri!.AbsolutePath.Split('/').Last();
            if (name == "encoder.int8.onnx")
            {
                encoderAttempts++;
                if (encoderAttempts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Pattern(EncoderBytes.Length, seed: 99)),
                    };
                }
            }

            return ServeByName(request);
        });

        Func<Task> first = () => store.EnsureAsync(null, null, CancellationToken.None);
        await first.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*failed verification*");

        ParakeetModelPaths paths = store.PathsFor(null);
        File.Exists(paths.Encoder).Should().BeFalse("a corrupt file must never land as a final model file");
        File.Exists(paths.Encoder + ".partial").Should().BeFalse("the rejected payload must be deleted");
        store.IsComplete(null).Should().BeFalse();

        // The retry serves honest bytes and completes — corrupt state did not
        // poison the store.
        await store.EnsureAsync(null, null, CancellationToken.None);
        store.IsComplete(null).Should().BeTrue();
        encoderAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Interrupted_download_resumes_from_the_partial_file()
    {
        const int prefixLength = 100;
        bool connectionDropped = true;
        var rangesSeen = new List<long>();
        ParakeetModelStore store = CreateStore(request =>
        {
            string name = request.RequestUri!.AbsolutePath.Split('/').Last();
            if (name == "encoder.int8.onnx")
            {
                if (request.Headers.Range is { } range)
                {
                    rangesSeen.Add(range.Ranges.Single().From!.Value);
                }

                if (connectionDropped && request.Headers.Range is null)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new FailAfterPrefixStream(EncoderBytes[..prefixLength])),
                    };
                }
            }

            return ServeByName(request);
        });

        Func<Task> interrupted = () => store.EnsureAsync(null, null, CancellationToken.None);
        await interrupted.Should().ThrowAsync<IOException>("the dropped connection propagates");
        File.Exists(store.PathsFor(null).Encoder + ".partial")
            .Should().BeTrue("the fetched prefix survives for resume");

        connectionDropped = false;
        await store.EnsureAsync(null, null, CancellationToken.None);

        rangesSeen.Should().ContainSingle().Which.Should().Be(prefixLength,
            "the retry must resume at the partial prefix instead of restarting the fetch");
        store.IsComplete(null).Should().BeTrue();
    }

    [Fact]
    public async Task Server_ignoring_the_range_header_restarts_the_file_from_scratch()
    {
        // A stale/garbage partial is on disk; the server answers the range
        // request with a plain 200, so the store must restart the file (and its
        // hash) instead of appending to garbage.
        bool rangeRequested = false;
        ParakeetModelStore store = CreateStore(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("encoder.int8.onnx", StringComparison.Ordinal) &&
                request.Headers.Range is not null)
            {
                rangeRequested = true;
                return new HttpResponseMessage(HttpStatusCode.OK) // Range ignored.
                {
                    Content = new ByteArrayContent(EncoderBytes),
                };
            }

            return ServeByName(request);
        });
        ParakeetModelPaths paths = store.PathsFor(null);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Encoder)!);
        await File.WriteAllBytesAsync(paths.Encoder + ".partial", Pattern(50, seed: 77));

        await store.EnsureAsync(null, null, CancellationToken.None);

        rangeRequested.Should().BeTrue("the resume was attempted before the restart");
        store.IsComplete(null).Should().BeTrue();
        (await File.ReadAllBytesAsync(paths.Encoder)).Should().Equal(EncoderBytes);
    }

    [Fact]
    public async Task Tampered_model_is_verified_quarantined_and_redownloaded()
    {
        ParakeetModelStore store = CreateStore(ServeByName);
        await store.EnsureAsync(null, null, CancellationToken.None);
        store.IsComplete(null).Should().BeTrue();

        // Same-length tampering passes the size-only hot path but not a full
        // verification — this is exactly the post-download disk-corruption case.
        ParakeetModelPaths paths = store.PathsFor(null);
        await File.WriteAllBytesAsync(paths.Encoder, Pattern(EncoderBytes.Length, seed: 55));
        store.VerifyIntegrity(null).Should().BeFalse();

        store.QuarantineIfCorrupt(null).Should().BeTrue();
        string modelDirectory = Path.GetDirectoryName(paths.Encoder)!;
        Directory.Exists(modelDirectory).Should().BeFalse("quarantine frees the path for a clean retry");
        Directory.GetDirectories(
                Path.GetDirectoryName(modelDirectory)!,
                Path.GetFileName(modelDirectory) + ".corrupt-*")
            .Should().ContainSingle("corrupt state is preserved as evidence, not silently deleted");
        store.IsComplete(null).Should().BeFalse();

        await store.EnsureAsync(null, null, CancellationToken.None);
        store.VerifyIntegrity(null).Should().BeTrue();
    }

    [Fact]
    public async Task Quarantine_is_a_no_op_for_healthy_or_missing_models()
    {
        ParakeetModelStore store = CreateStore(ServeByName);

        store.QuarantineIfCorrupt(null).Should().BeFalse("nothing on disk yet");

        await store.EnsureAsync(null, null, CancellationToken.None);
        store.QuarantineIfCorrupt(null).Should().BeFalse("a healthy model stays put");
        store.IsComplete(null).Should().BeTrue();
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }

    /// <summary>Yields the prefix bytes, then fails like a dropped connection.</summary>
    private sealed class FailAfterPrefixStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= prefix.Length)
            {
                throw new IOException("Simulated connection drop mid-download.");
            }

            int copied = Math.Min(count, prefix.Length - _position);
            Array.Copy(prefix, _position, buffer, offset, copied);
            _position += copied;
            return copied;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
}

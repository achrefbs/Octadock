using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;

namespace Octadock.Platform.Windows.Stt;

/// <summary>
/// Downloads and verifies the Parakeet model files (encoder/decoder/joiner
/// int8 ONNX + tokens) into <see cref="IStoragePaths.RootDirectory"/>\models.
/// The manifest is pinned — exact byte sizes and SHA-256 per file — so a
/// completed download is trustworthy and an interrupted one resumes from the
/// <c>.partial</c> file instead of restarting a ~640 MB fetch.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ParakeetModelStore : IDisposable
{
    private const string BaseUrl =
        "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/";

    // Pinned against the upstream repository; sizes are exact, hashes are the
    // upstream LFS SHA-256 values. tokens.txt is a regular Git object upstream,
    // but its raw bytes are pinned here just like the LFS-backed model files.
    private static readonly ManifestFile[] Manifest =
    [
        new("encoder.int8.onnx", 652_184_281, "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247"),
        new("decoder.int8.onnx", 11_845_275, "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e"),
        new("joiner.int8.onnx", 6_355_277, "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3"),
        new("tokens.txt", 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
    ];

    private static readonly HttpClient Client = new()
    {
        // A 640 MB fetch on a slow line can legitimately take a long time; the
        // per-read watchdog below catches stalled connections instead.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly IStoragePaths _paths;
    private readonly ILogger<ParakeetModelStore> _logger;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly object _lifetimeGate = new();
    private bool _disposed;

    /// <summary>Creates the model store.</summary>
    public ParakeetModelStore(IStoragePaths paths, ILogger<ParakeetModelStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <summary>Total bytes of all files in the model (shown before download).</summary>
    public long TotalBytes => Manifest.Sum(f => f.Bytes);

    /// <summary>Maps any requested variant onto the single supported model id.</summary>
    public static string NormalizeModel(string? model)
        => SpeechSettings.DefaultParakeetModel;

    /// <summary>Absolute paths of the four model files.</summary>
    public ParakeetModelPaths PathsFor(string? model)
    {
        string directory = ModelDirectory(model);
        return new ParakeetModelPaths(
            Path.Combine(directory, "encoder.int8.onnx"),
            Path.Combine(directory, "decoder.int8.onnx"),
            Path.Combine(directory, "joiner.int8.onnx"),
            Path.Combine(directory, "tokens.txt"));
    }

    /// <summary>
    /// True when every model file exists with its exact pinned size and the
    /// small native token mapping still has its pinned digest.
    /// </summary>
    public bool IsComplete(string? model)
    {
        string directory = ModelDirectory(model);
        foreach (ManifestFile file in Manifest)
        {
            var info = new FileInfo(Path.Combine(directory, file.Name));
            if (!info.Exists || info.Length != file.Bytes)
            {
                return false;
            }

            // Hashing the ~640 MB ONNX files on every availability query would
            // stall the UI. They are verified while streaming below. The token
            // mapping is small, is consumed directly by the native recognizer,
            // and is cheap to verify again before every native load.
            if (file.Name == "tokens.txt" && !FileHasExpectedDigest(info.FullName, file.Sha256!))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Deletes the model directory (used by the settings model manager).</summary>
    public void Delete(string? model)
    {
        string directory = ModelDirectory(model);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Ensures every model file is present and verified, resuming partial
    /// downloads. Progress is a single 0..1 fraction across all files.
    /// </summary>
    public async Task EnsureAsync(
        string? model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        CancellationTokenSource operationCts;
        Task waitForDownload;
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            operationCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token);
            waitForDownload = _downloadGate.WaitAsync(operationCts.Token);
        }

        using (operationCts)
        {
            await waitForDownload.ConfigureAwait(false);
            try
            {
                string directory = ModelDirectory(model);
                Directory.CreateDirectory(directory);

                long totalBytes = TotalBytes;
                long doneBytes = 0;
                foreach (ManifestFile file in Manifest)
                {
                    string path = Path.Combine(directory, file.Name);
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length == file.Bytes)
                    {
                        doneBytes += file.Bytes;
                        progress?.Report(doneBytes / (double)totalBytes);
                        continue;
                    }

                    long baseBytes = doneBytes;
                    await DownloadFileAsync(
                        file,
                        path,
                        copied => progress?.Report((baseBytes + copied) / (double)totalBytes),
                        operationCts.Token).ConfigureAwait(false);
                    doneBytes += file.Bytes;
                    progress?.Report(doneBytes / (double)totalBytes);
                }
            }
            finally
            {
                _downloadGate.Release();
            }
        }
    }

    private string ModelDirectory(string? model)
        => Path.Combine(_paths.RootDirectory, "models", NormalizeModel(model));

    /// <summary>
    /// Downloads one file to <c>path + ".partial"</c> with HTTP range resume,
    /// hashing the stream as it lands, then verifies and atomically moves it
    /// into place. Any verification failure deletes the partial and throws, so
    /// a corrupt file can never masquerade as a complete model.
    /// </summary>
    private async Task DownloadFileAsync(
        ManifestFile file, string path, Action<long> reportCopied, CancellationToken cancellationToken)
    {
        string partialPath = path + ".partial";
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var destination = new FileStream(
            partialPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        // Feed any previously downloaded prefix into the hash so a resumed
        // download still produces the full-file digest. An oversized partial is
        // corrupt by definition — start over.
        if (destination.Length > file.Bytes)
        {
            destination.SetLength(0);
        }

        long resumeFrom = await HashExistingPrefixAsync(destination, hash, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Downloading Parakeet file {File} ({TotalMb:0} MB){Resume}…",
            file.Name,
            file.Bytes / 1024.0 / 1024.0,
            resumeFrom > 0 ? $" resuming at {resumeFrom / 1024.0 / 1024.0:0} MB" : string.Empty);

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + file.Name);
        if (resumeFrom > 0)
        {
            request.Headers.Range = new global::System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
        }

        using HttpResponseMessage response = await Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (resumeFrom > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            // The server ignored the range (or the cached partial is stale):
            // restart the file from scratch, including the hash.
            response.EnsureSuccessStatusCode();
            destination.SetLength(0);
            destination.Position = 0;
            hash.GetHashAndReset();
            resumeFrom = 0;
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        try
        {
            await CopyWithStallWatchdogAsync(
                response,
                destination,
                hash,
                resumeFrom,
                file.Bytes,
                reportCopied,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            await destination.DisposeAsync().ConfigureAwait(false);
            File.Delete(partialPath);
            throw;
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        long downloadedBytes = destination.Length;
        string digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        await destination.DisposeAsync().ConfigureAwait(false);

        if (downloadedBytes != file.Bytes ||
            (file.Sha256 is not null && !string.Equals(digest, file.Sha256, StringComparison.OrdinalIgnoreCase)))
        {
            File.Delete(partialPath);
            throw new InvalidOperationException(
                $"The downloaded Parakeet file '{file.Name}' failed verification " +
                $"({downloadedBytes} of {file.Bytes} bytes). Please try again.");
        }

        File.Move(partialPath, path, overwrite: true);
    }

    private static async Task<long> HashExistingPrefixAsync(
        FileStream destination, IncrementalHash hash, CancellationToken cancellationToken)
    {
        destination.Position = 0;
        var buffer = new byte[81920];
        long hashed = 0;
        int read;
        while ((read = await destination.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            hashed += read;
        }

        return hashed;
    }

    /// <summary>
    /// Streams the response body to disk. Each read re-arms a 90-second
    /// watchdog, so a stalled connection fails fast (and resumes on retry)
    /// instead of hanging a background download forever.
    /// </summary>
    internal static async Task CopyWithStallWatchdogAsync(
        HttpResponseMessage response,
        Stream destination,
        IncrementalHash hash,
        long alreadyCopied,
        long maximumBytes,
        Action<long> reportCopied,
        CancellationToken cancellationToken)
    {
        long remaining = maximumBytes - alreadyCopied;
        if (remaining < 0 || response.Content.Headers.ContentLength is long contentLength && contentLength > remaining)
        {
            throw new InvalidDataException(
                $"The Parakeet model response exceeds the pinned {maximumBytes:N0}-byte limit.");
        }

        await using Stream source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = new byte[81920];
        long copied = alreadyCopied;
        while (true)
        {
            watchdog.CancelAfter(TimeSpan.FromSeconds(90));
            int read;
            try
            {
                read = await source.ReadAsync(buffer, watchdog.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Parakeet model download stalled (no data for 90 seconds).");
            }

            if (read <= 0)
            {
                break;
            }

            if (read > maximumBytes - copied)
            {
                throw new InvalidDataException(
                    $"The Parakeet model response exceeds the pinned {maximumBytes:N0}-byte limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            copied += read;
            reportCopied(copied);
        }
    }

    internal static bool FileHasExpectedDigest(string path, string expectedSha256)
    {
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _lifetimeCts.Cancel();

        // Model downloads may still be flushing a verified file during host
        // shutdown. Cancellation makes the wait bounded by the current I/O step.
        _downloadGate.Wait();
        _downloadGate.Release();
        _downloadGate.Dispose();
        _lifetimeCts.Dispose();
    }

    private readonly record struct ManifestFile(string Name, long Bytes, string? Sha256);
}

/// <summary>Absolute paths of the four files a Parakeet transducer model needs.</summary>
public readonly record struct ParakeetModelPaths(
    string Encoder, string Decoder, string Joiner, string Tokens);

using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;

namespace Octadock.Platform.Windows.Stt;

/// <summary>
/// Imports and verifies the Parakeet model files (encoder/decoder/joiner
/// int8 ONNX + tokens) into <see cref="IStoragePaths.RootDirectory"/>\models.
/// The manifest is pinned — exact byte sizes and SHA-256 per file — so a
/// completed import is verified before it replaces the installed model.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ParakeetModelStore : IDisposable
{
    // Pinned against the upstream repository; sizes are exact, hashes are the
    // upstream LFS SHA-256 values. tokens.txt is a regular Git object upstream,
    // but its raw bytes are pinned here just like the LFS-backed model files.
    private static readonly ManifestFile[] PinnedManifest =
    [
        new("encoder.int8.onnx", 652_184_281, "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247"),
        new("decoder.int8.onnx", 11_845_275, "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e"),
        new("joiner.int8.onnx", 6_355_277, "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3"),
        new("tokens.txt", 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
    ];

    private readonly IStoragePaths _paths;
    private readonly ILogger<ParakeetModelStore> _logger;
    private readonly ManifestFile[] _manifest;
    private readonly SemaphoreSlim _importGate = new(1, 1);
    private bool _disposed;

    public ParakeetModelStore(IStoragePaths paths, ILogger<ParakeetModelStore> logger)
        : this(paths, logger, PinnedManifest) { }

    internal ParakeetModelStore(IStoragePaths paths, ILogger<ParakeetModelStore> logger, ManifestFile[] manifest)
    {
        _paths = paths;
        _logger = logger;
        _manifest = manifest;
    }

    /// <summary>Total bytes of all files in the model (shown before import).</summary>
    public long TotalBytes => _manifest.Sum(f => f.Bytes);

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
        foreach (ManifestFile file in _manifest)
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
    /// Checks local model availability without making network requests.
    /// </summary>
    public Task EnsureAsync(string? model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsComplete(model)) throw new FileNotFoundException(
            "Import the Parakeet model folder in Settings → Voice → Advanced. Octadock does not download models.");
        progress?.Report(1);
        return Task.CompletedTask;
    }

    private string ModelDirectory(string? model)
        => Path.Combine(_paths.RootDirectory, "models", NormalizeModel(model));

    /// <summary>Copies a user-selected local model folder, verifies every digest, then installs it.</summary>
    public async Task ImportAsync(string? model, string sourceDirectory, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string sourceRoot = Path.GetFullPath(sourceDirectory);
        if (sourceRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose a folder on a local drive.", nameof(sourceDirectory));
        await _importGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string destination = ModelDirectory(model);
        string staging = destination + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            if (IsComplete(model)) return;
            Directory.CreateDirectory(staging);
            long done = 0;
            foreach (ManifestFile file in _manifest)
            {
                string source = Path.Combine(sourceRoot, file.Name);
                var info = new FileInfo(source);
                if (!info.Exists || info.Length != file.Bytes)
                    throw new InvalidDataException($"{file.Name} is missing or has an unexpected size.");
                string target = Path.Combine(staging, file.Name);
                await using (FileStream input = File.OpenRead(source))
                await using (FileStream output = File.Create(target))
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                if (file.Sha256 is not null && !FileHasExpectedDigest(target, file.Sha256))
                    throw new InvalidDataException($"{file.Name} failed SHA-256 verification.");
                done += file.Bytes;
                progress?.Report(done / (double)TotalBytes);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string? previous = null;
            if (Directory.Exists(destination))
            {
                previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
                Directory.Move(destination, previous);
            }
            try { Directory.Move(staging, destination); }
            catch
            {
                if (previous is not null && !Directory.Exists(destination)) Directory.Move(previous, destination);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            _importGate.Release();
        }
    }

    internal static bool FileHasExpectedDigest(string path, string expectedSha256)
    {
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Full SHA-256 verification of every model file (~640 MB of reads, so this
    /// belongs on recovery paths — never on the availability hot path, which
    /// stays size-based via <see cref="IsComplete"/>).
    /// </summary>
    internal bool VerifyIntegrity(string? model)
    {
        string directory = ModelDirectory(model);
        foreach (ManifestFile file in _manifest)
        {
            string path = Path.Combine(directory, file.Name);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Bytes)
            {
                return false;
            }

            if (file.Sha256 is not null && !FileHasExpectedDigest(path, file.Sha256))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Recovery for a model that passes the size check but fails at native load:
    /// when full verification shows the on-disk files are corrupt, the model
    /// directory is moved aside (never silently deleted) so the next
    /// local import can install into a clean path. Returns true
    /// when corrupt state was found and quarantined.
    /// </summary>
    internal bool QuarantineIfCorrupt(string? model)
    {
        string directory = ModelDirectory(model);
        if (!Directory.Exists(directory) || VerifyIntegrity(model))
        {
            return false;
        }

        string quarantinePath = directory +
            ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss");
        try
        {
            Directory.Move(directory, quarantinePath);
            _logger.LogWarning(
                "Parakeet model files failed integrity verification and were quarantined to {Path}.",
                quarantinePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked file must not mask the original load failure; leave the
            // state in place and let the caller surface the honest error.
            _logger.LogWarning(ex, "Could not quarantine the corrupt Parakeet model directory.");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    internal readonly record struct ManifestFile(string Name, long Bytes, string? Sha256);
}

/// <summary>Absolute paths of the four files a Parakeet transducer model needs.</summary>
public readonly record struct ParakeetModelPaths(
    string Encoder, string Decoder, string Joiner, string Tokens);

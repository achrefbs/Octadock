namespace Octadock.Core.Io;

/// <summary>
/// Atomic, reversible user-file writes (WS9, R23). Every write goes to a temp file
/// in the destination's own directory, is flushed to disk, then atomically swapped
/// into place. When it overwrites an existing file, the prior contents are captured
/// as a revision first, so the original is always intact OR restorable — a process
/// killed at any point never leaves a half-written original.
/// </summary>
public interface ISafeFileWriter
{
    /// <summary>Atomically writes <paramref name="content"/> to <paramref name="destinationPath"/>.</summary>
    Task WriteAsync(string destinationPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically writes whatever <paramref name="writeContent"/> streams, to
    /// <paramref name="destinationPath"/>. Use for encoders that write to a stream.
    /// </summary>
    Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically copies <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.</summary>
    Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);

    /// <summary>Restores the most recent revision of <paramref name="originalPath"/>, if any.</summary>
    Task<bool> RestoreLatestAsync(string originalPath, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class SafeFileWriter : ISafeFileWriter
{
    private readonly IFileRevisionStore _revisions;

    public SafeFileWriter(IFileRevisionStore revisions)
        => _revisions = revisions ?? throw new ArgumentNullException(nameof(revisions));

    /// <inheritdoc />
    public Task WriteAsync(
        string destinationPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        => WriteAsync(
            destinationPath,
            async (stream, ct) => await stream.WriteAsync(content, ct).ConfigureAwait(false),
            cancellationToken);

    /// <inheritdoc />
    public async Task WriteAsync(
        string destinationPath,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writeContent);
        string full = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException($"Destination '{destinationPath}' has no directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, $".octadock-tmp-{Guid.NewGuid():N}");
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await writeContent(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                // Force the OS to persist the temp file before the swap, so a crash
                // during the swap can never expose a half-written file.
                stream.Flush(flushToDisk: true);
            }

            await CommitAsync(tempPath, full, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CopyAsync(
        string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(sourcePath);
        await WriteAsync(
            destinationPath,
            async (stream, ct) =>
            {
                await using var input = new FileStream(
                    source, FileMode.Open, FileAccess.Read, FileShare.Read);
                await input.CopyToAsync(stream, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RestoreLatestAsync(string originalPath, CancellationToken cancellationToken = default)
    {
        FileRevision? latest = _revisions.GetLatest(originalPath);
        if (latest is null || !File.Exists(latest.BackupPath))
        {
            return false;
        }

        // Restoring is itself a safe write, so the current (post-overwrite) content
        // is captured as a new revision — restore is undoable too.
        await CopyAsync(latest.BackupPath, originalPath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task CommitAsync(string tempPath, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            // File.Replace is atomic and writes the prior contents to a backup on the
            // SAME volume; we then file that backup into the central revision store.
            string sameVolumeBackup = Path.Combine(
                Path.GetDirectoryName(destination)!, $".octadock-bak-{Guid.NewGuid():N}");
            File.Replace(tempPath, destination, sameVolumeBackup, ignoreMetadataErrors: true);
            await _revisions.StoreAsync(destination, sameVolumeBackup, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

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
            // Best effort; an orphaned temp never corrupts the original.
        }
    }
}

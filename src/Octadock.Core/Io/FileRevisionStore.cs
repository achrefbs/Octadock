using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Octadock.Core.Io;

/// <summary>One backed-up prior version of a user file.</summary>
public sealed record FileRevision(string OriginalPath, string BackupPath, DateTimeOffset CreatedAt);

/// <summary>
/// Keeps prior versions of user files so an overwrite is always reversible
/// (WS9, R23). Backups live under one central directory, grouped by a hash of the
/// original path, newest-last. This is the minimal store behind
/// <see cref="SafeFileWriter"/>; the visible restore UI + retention are WS9.
/// </summary>
public interface IFileRevisionStore
{
    /// <summary>
    /// Takes ownership of <paramref name="backupFilePath"/> (which already holds the
    /// prior contents of <paramref name="originalPath"/>) and files it as a revision.
    /// </summary>
    Task<FileRevision> StoreAsync(string originalPath, string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>Revisions for a file, oldest first.</summary>
    IReadOnlyList<FileRevision> GetRevisions(string originalPath);

    /// <summary>Most recent revision for a file, or null.</summary>
    FileRevision? GetLatest(string originalPath);
}

/// <inheritdoc />
public sealed class FileRevisionStore : IFileRevisionStore
{
    private readonly string _root;

    public FileRevisionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = rootDirectory;
    }

    /// <inheritdoc />
    public async Task<FileRevision> StoreAsync(
        string originalPath, string backupFilePath, CancellationToken cancellationToken = default)
    {
        string bucket = BucketDirectory(originalPath);
        Directory.CreateDirectory(bucket);

        // Sortable, unique revision name: UTC ticks + short guid.
        string stamp = DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture);
        string revisionPath = Path.Combine(bucket, $"{stamp}-{Guid.NewGuid():N}.bak");

        // Move if same volume, else copy+delete (backupFilePath may be on the file's volume).
        try
        {
            File.Move(backupFilePath, revisionPath);
        }
        catch (IOException)
        {
            File.Copy(backupFilePath, revisionPath, overwrite: true);
            TryDelete(backupFilePath);
        }

        // Record the original path alongside the bucket for a future restore UI.
        await File.WriteAllTextAsync(
            Path.Combine(bucket, "original.txt"), Path.GetFullPath(originalPath), cancellationToken)
            .ConfigureAwait(false);

        return new FileRevision(Path.GetFullPath(originalPath), revisionPath, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public IReadOnlyList<FileRevision> GetRevisions(string originalPath)
    {
        string bucket = BucketDirectory(originalPath);
        if (!Directory.Exists(bucket))
        {
            return Array.Empty<FileRevision>();
        }

        string full = Path.GetFullPath(originalPath);
        return Directory.EnumerateFiles(bucket, "*.bak")
            .OrderBy(p => p, StringComparer.Ordinal) // ticks prefix ⇒ chronological
            .Select(p => new FileRevision(full, p, File.GetLastWriteTimeUtc(p)))
            .ToArray();
    }

    /// <inheritdoc />
    public FileRevision? GetLatest(string originalPath)
    {
        IReadOnlyList<FileRevision> revisions = GetRevisions(originalPath);
        return revisions.Count == 0 ? null : revisions[^1];
    }

    private string BucketDirectory(string originalPath)
    {
        string full = Path.GetFullPath(originalPath).ToLowerInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).ToLowerInvariant();
        return Path.Combine(_root, key[..2], key);
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
            // Best effort.
        }
    }
}

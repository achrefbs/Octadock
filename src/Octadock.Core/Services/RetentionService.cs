using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Io;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="IRetentionService"/>. Pulls retention candidates from the
/// capture repository, applies the pure <see cref="RetentionPolicy"/>, then (for a
/// full run) hard-deletes the expired/purged rows and best-effort removes their
/// original, thumbnail and project files.
/// </summary>
public sealed partial class RetentionService : IRetentionService
{
    private static readonly TimeSpan TempExportRetention = TimeSpan.FromDays(1);

    private readonly ICaptureRepository _repository;
    private readonly IStoragePaths _storagePaths;
    private readonly IClock _clock;
    private readonly ILogger<RetentionService> _logger;

    /// <summary>Creates the retention service.</summary>
    public RetentionService(
        ICaptureRepository repository,
        IStoragePaths storagePaths,
        IClock? clock = null,
        ILogger<RetentionService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storagePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<RetentionService>.Instance;
    }

    /// <inheritdoc />
    public async Task<RetentionPlan> PlanAsync(
        OctadockSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IReadOnlyList<CaptureRecord> candidates = await GatherCandidatesAsync(settings, now, cancellationToken)
            .ConfigureAwait(false);
        return RetentionPolicy.Evaluate(candidates, settings, now);
    }

    /// <inheritdoc />
    public async Task<RetentionResult> RunAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        DateTimeOffset now = _clock.UtcNow;
        IReadOnlyList<CaptureRecord> candidates = await GatherCandidatesAsync(settings, now, cancellationToken)
            .ConfigureAwait(false);
        RetentionPlan plan = RetentionPolicy.Evaluate(candidates, settings, now);
        if (plan.IsEmpty)
        {
            (int tempFiles, long tempBytes) = DeleteExpiredTempExports(now, cancellationToken);
            return tempFiles == 0
                ? RetentionResult.Nothing
                : new RetentionResult(0, tempFiles, tempBytes);
        }

        // Index candidates so we can locate the files for each id being removed.
        var byId = new Dictionary<Guid, CaptureRecord>();
        foreach (CaptureRecord capture in candidates)
        {
            byId[capture.Id] = capture;
        }

        int capturesDeleted = 0;
        int filesDeleted = 0;
        long bytesReclaimed = 0;

        foreach (Guid id in plan.ExpiredToDelete.Concat(plan.SoftDeletedToPurge))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Delete the DB row first: only once the record is gone do we remove
            // its files. If the hard-delete fails we leave the files in place, so
            // a surviving row never points at a missing file.
            try
            {
                await _repository.HardDeleteAsync(id, cancellationToken).ConfigureAwait(false);
                capturesDeleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogHardDeleteFailed(ex, id);
                continue;
            }

            if (byId.TryGetValue(id, out CaptureRecord? capture))
            {
                (int files, long bytes) = DeleteFiles(capture);
                filesDeleted += files;
                bytesReclaimed += bytes;
            }
        }

        (int tempFilesDeleted, long tempBytesReclaimed) = DeleteExpiredTempExports(now, cancellationToken);
        filesDeleted += tempFilesDeleted;
        bytesReclaimed += tempBytesReclaimed;

        return new RetentionResult(capturesDeleted, filesDeleted, bytesReclaimed);
    }

    private async Task<IReadOnlyList<CaptureRecord>> GatherCandidatesAsync(
        OctadockSettings settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var result = new List<CaptureRecord>();
        var seen = new HashSet<Guid>();

        // Live captures that have aged past the retention window. When history is
        // disabled, everything live is a candidate, so use "now" as the cutoff to
        // fetch all live captures.
        bool historyEnabled = settings.History.Enabled;
        DateTimeOffset? retentionCutoff = RetentionPolicy.ExpiryCutoff(settings, now);
        DateTimeOffset expiryCutoff = historyEnabled
            ? retentionCutoff ?? DateTimeOffset.MaxValue
            : DateTimeOffset.MaxValue;
        bool fetchLive = !historyEnabled || retentionCutoff is not null;
        if (fetchLive)
        {
            IReadOnlyList<CaptureRecord> live = await _repository
                .GetOlderThanAsync(expiryCutoff, cancellationToken).ConfigureAwait(false);
            foreach (CaptureRecord capture in live)
            {
                if (seen.Add(capture.Id))
                {
                    result.Add(capture);
                }
            }
        }

        // Soft-deleted captures whose grace period has elapsed.
        DateTimeOffset purgeCutoff = RetentionPolicy.PurgeCutoff(settings, now);
        IReadOnlyList<CaptureRecord> softDeleted = await _repository
            .GetSoftDeletedBeforeAsync(purgeCutoff, cancellationToken).ConfigureAwait(false);
        foreach (CaptureRecord capture in softDeleted)
        {
            if (seen.Add(capture.Id))
            {
                result.Add(capture);
            }
        }

        return result;
    }

    private (int Files, long Bytes) DeleteFiles(CaptureRecord capture)
    {
        int files = 0;
        long bytes = 0;

        foreach (string? relative in new[]
                 { capture.OriginalPath, capture.ThumbnailPath, capture.ProjectPath, capture.ApprovedMockupPath })
        {
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            string absolute;
            try
            {
                absolute = _storagePaths.ToAbsolute(relative);
            }
            catch (ArgumentException)
            {
                // Legacy/untrusted records can contain an absolute external path.
                // Leave that file alone and continue processing managed artifacts.
                continue;
            }
            if (!IsUnderStorageRoot(absolute))
            {
                continue;
            }

            try
            {
                if (!File.Exists(absolute))
                {
                    continue;
                }

                long size = 0;
                try
                {
                    size = new FileInfo(absolute).Length;
                }
                catch (IOException)
                {
                    // Length is best-effort; proceed with the delete regardless.
                }

                File.Delete(absolute);
                files++;
                bytes += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogFileDeleteFailed(ex, absolute);
            }
        }

        return (files, bytes);
    }

    private (int Files, long Bytes) DeleteExpiredTempExports(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string? tempDirectory = GetSafeTempExportDirectory();
        if (tempDirectory is null)
        {
            return (0, 0);
        }

        DateTime cutoffUtc = now.UtcDateTime - TempExportRetention;
        int files = 0;
        long bytes = 0;

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            List<(string Path, DateTime LastWriteUtc)> directories = Directory
                .EnumerateDirectories(tempDirectory, "*", options)
                .Select(path => (path, new DirectoryInfo(path).LastWriteTimeUtc))
                .ToList();
            foreach (string path in Directory.EnumerateFiles(tempDirectory, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.LastWriteTimeUtc > cutoffUtc)
                    {
                        continue;
                    }

                    long size = info.Length;
                    info.Delete();
                    files++;
                    bytes += size;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogFileDeleteFailed(ex, path);
                }
            }

            // Crash leftovers include nested AgentWorkspace clipboard files and
            // whole octadock-agent/staging directories. Remove only old, empty,
            // non-reparse directories and walk deepest-first.
            foreach ((string directory, DateTime lastWriteUtc) in directories
                         .OrderByDescending(item => item.Path.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new DirectoryInfo(directory);
                    if (!info.Exists ||
                        (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        lastWriteUtc > cutoffUtc ||
                        Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        continue;
                    }

                    info.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogFileDeleteFailed(ex, directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogFileDeleteFailed(ex, tempDirectory);
        }

        return (files, bytes);
    }

    private string? GetSafeTempExportDirectory()
    {
        string configured = _storagePaths.TempExportsDirectory;
        string storageRoot = _storagePaths.RootDirectory;
        if (string.IsNullOrWhiteSpace(configured) ||
            string.IsNullOrWhiteSpace(storageRoot) ||
            PathSafety.IsUncPath(configured) ||
            PathSafety.IsUncPath(storageRoot))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(configured);
            string fullStorageRoot = Path.GetFullPath(storageRoot);
            string relative = Path.GetRelativePath(fullStorageRoot, full);
            if (Path.IsPathRooted(relative) ||
                relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                return null;
            }

            string driveRoot = Path.GetPathRoot(full)!;
            if (new DriveInfo(driveRoot).DriveType == DriveType.Network)
            {
                return null;
            }

            // EnumerationOptions.AttributesToSkip does not protect an
            // enumeration whose root itself is a reparse point. Inspect every
            // existing ancestor before the first enumeration and fail closed.
            string current = driveRoot;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            foreach (string segment in Path.GetRelativePath(driveRoot, full).Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }
            }

            return (File.GetAttributes(full) & FileAttributes.Directory) != 0 ? full : null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private bool IsUnderStorageRoot(string path)
    {
        string root = EnsureTrailingSeparator(Path.GetFullPath(_storagePaths.RootDirectory));
        string full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
        => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Failed to hard-delete capture {CaptureId} during retention.")]
    private partial void LogHardDeleteFailed(Exception exception, Guid captureId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Failed to delete retention file '{Path}'.")]
    private partial void LogFileDeleteFailed(Exception exception, string path);
}

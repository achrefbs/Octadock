using Octadock.Core.Models;

namespace Octadock.Core.Persistence;

/// <summary>Initializes and migrates the SQLite database schema.</summary>
public interface IOctadockDatabase
{
    /// <summary>Ensures the database file and schema exist and are at the current version.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Folds the write-ahead log into the main database file (best-effort).
    /// Run at startup, periodically, and on clean shutdown so an unexpected
    /// process kill can only lose moments of data instead of everything since
    /// launch.
    /// </summary>
    Task CheckpointAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persists and queries capture metadata (the <c>captures</c> table).</summary>
public interface ICaptureRepository
{
    Task AddAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Runs a filtered, paged history query.</summary>
    Task<IReadOnlyList<CaptureRecord>> QueryAsync(CaptureFilter filter, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CaptureFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent non-deleted captures, newest first.</summary>
    Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored record (used to attach thumbnails/projects, etc.).</summary>
    Task UpdateAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live (non-deleted) captures created strictly before <paramref name="cutoff"/> — retention purge input.</summary>
    Task<IReadOnlyList<CaptureRecord>> GetOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>Soft-deleted captures whose deletion happened at or before <paramref name="cutoff"/> — cleanup input.</summary>
    Task<IReadOnlyList<CaptureRecord>> GetSoftDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

/// <summary>Persists the action timeline (the <c>actions</c> table).</summary>
public interface IActionRepository
{
    Task AddAsync(ActionRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActionRecord>> GetForCaptureAsync(Guid captureId, CancellationToken cancellationToken = default);
}

/// <summary>Persists floating-pin state (the <c>pins</c> table).</summary>
public interface IPinRepository
{
    Task UpsertAsync(PinRecord record, CancellationToken cancellationToken = default);

    Task<PinRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PinRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Persists clipboard-history items (the <c>clipboard_clips</c> table).</summary>
public interface IClipboardClipRepository
{
    Task AddAsync(ClipboardClipRecord record, CancellationToken cancellationToken = default);

    Task UpdateAsync(ClipboardClipRecord record, CancellationToken cancellationToken = default);

    Task<ClipboardClipRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Runs a filtered, paged clipboard-history query.</summary>
    Task<IReadOnlyList<ClipboardClipRecord>> QueryAsync(
        ClipboardClipFilter filter,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(ClipboardClipFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recently observed non-deleted clips, newest first.</summary>
    Task<IReadOnlyList<ClipboardClipRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Returns the newest live clip for a content fingerprint, for future de-duplication.</summary>
    Task<ClipboardClipRecord?> GetLatestByContentHashAsync(
        ClipboardClipKind kind,
        string contentHash,
        CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Live clips last observed strictly before <paramref name="cutoff"/> — retention purge input.</summary>
    Task<IReadOnlyList<ClipboardClipRecord>> GetOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deleted clips whose deletion happened at or before <paramref name="cutoff"/> — cleanup input.</summary>
    Task<IReadOnlyList<ClipboardClipRecord>> GetSoftDeletedBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Low-level key/value persistence for settings (the <c>settings</c> table).
/// The typed <see cref="Settings.OctadockSettings"/> view is layered on top by
/// the settings service.
/// </summary>
public interface ISettingsStore
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

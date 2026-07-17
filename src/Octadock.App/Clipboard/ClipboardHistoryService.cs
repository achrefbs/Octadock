using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Imaging;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.Clipboard;

/// <summary>
/// The clipboard-history pipeline: listens to the clipboard monitor, snapshots
/// new content, de-duplicates by content hash (bumping seen count/time), stores
/// text inline and images as managed PNG + thumbnail files, and trims the
/// history to the configured cap (oldest non-favorites first). Reacts live to
/// the Settings toggle. Everything stays on the local machine.
/// </summary>
public sealed partial class ClipboardHistoryService : IDisposable
{
    /// <summary>Clips larger than this are not recorded (protects the local DB).</summary>
    internal const int MaxTextLength = 1_000_000;

    /// <summary>Images larger than this (encoded PNG bytes) are not recorded.</summary>
    internal const int MaxImageBytes = 32 * 1024 * 1024;

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IClipboardMonitor _monitor;
    private readonly IClipboardSnapshotSource _snapshots;
    private readonly IClipboardClipRepository _repository;
    private readonly ISettingsService _settings;
    private readonly IStoragePaths _paths;
    private readonly IThumbnailGenerator _thumbnails;
    private readonly IClock _clock;
    private readonly ILicenseGate _licenseGate;
    private readonly ILogger<ClipboardHistoryService> _logger;
    private readonly SemaphoreSlim _processGate = new(1, 1);

    private CancellationTokenSource? _debounceCts;
    private bool _started;
    private bool _monitorSubscribed;
    private bool _disposed;

    /// <summary>Creates the clipboard history service.</summary>
    public ClipboardHistoryService(
        IClipboardMonitor monitor,
        IClipboardSnapshotSource snapshots,
        IClipboardClipRepository repository,
        ISettingsService settings,
        IStoragePaths paths,
        IThumbnailGenerator thumbnails,
        IClock clock,
        ILicenseGate licenseGate,
        ILogger<ClipboardHistoryService> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Raised after a clip is added, updated, or trimmed.</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>Subscribes to the monitor and settings and starts listening when enabled.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        _settings.Changed += OnSettingsChanged;
        ApplyEnabledState(_settings.Current.Clipboard.MonitorEnabled);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_monitorSubscribed)
        {
            _monitor.ClipboardChanged -= OnClipboardChanged;
            _monitorSubscribed = false;
        }

        _settings.Changed -= OnSettingsChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _monitor.Dispose();
        _processGate.Dispose();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        => ApplyEnabledState(e.Settings.Clipboard.MonitorEnabled);

    private void ApplyEnabledState(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        if (enabled)
        {
            if (!_monitorSubscribed)
            {
                _monitor.ClipboardChanged += OnClipboardChanged;
                _monitorSubscribed = true;
            }

            _monitor.Start();
        }
        else
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            if (_monitorSubscribed)
            {
                _monitor.ClipboardChanged -= OnClipboardChanged;
                _monitorSubscribed = false;
            }

            _monitor.Stop();
        }
    }

    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        // Coalesce bursts: sources often set several formats in quick succession.
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedProcessAsync(cts.Token);
    }

    private async Task DebouncedProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, cancellationToken).ConfigureAwait(false);
            await ProcessCurrentClipboardAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer clipboard change or shutdown.
        }
        catch (Exception ex)
        {
            LogClipProcessingFailed(ex);
        }
    }

    /// <summary>Reads the current clipboard and records it. Exposed for tests.</summary>
    internal async Task ProcessCurrentClipboardAsync(CancellationToken cancellationToken = default)
    {
        ClipboardSettings settings = _settings.Current.Clipboard;
        if (!settings.MonitorEnabled || _disposed)
        {
            return;
        }

        // Trial/license gate (WS5/WS10, R17): the monitor PAUSES at expiry — no new
        // clips are recorded once the trial ends or a license is revoked. Existing
        // clips stay viewable; the clipboard window shows a "paused" banner. Read the
        // state silently (no toast) so ordinary copying isn't interrupted per clip.
        if (!_licenseGate.AllowsFullUse)
        {
            return;
        }

        ClipboardSnapshot? snapshot = _snapshots.TryRead(settings.IncludeImages);
        if (snapshot is null)
        {
            return;
        }

        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool changed = snapshot switch
            {
                { Text: not null } => await RecordTextAsync(snapshot, cancellationToken).ConfigureAwait(false),
                { Image: not null } => await RecordImageAsync(snapshot, cancellationToken).ConfigureAwait(false),
                _ => false,
            };

            if (changed)
            {
                await TrimToCapAsync(settings.MaxItems, cancellationToken).ConfigureAwait(false);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _processGate.Release();
        }
    }

    private async Task<bool> RecordTextAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        string text = snapshot.Text!;
        if (text.Length > MaxTextLength)
        {
            LogOversizeTextSkipped(text.Length);
            return false;
        }

        string hash = Sha256Hex(Encoding.UTF8.GetBytes(text));
        DateTimeOffset now = _clock.UtcNow;

        ClipboardClipRecord? existing = await _repository
            .GetLatestByContentHashAsync(ClipboardClipKind.Text, hash, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null && string.Equals(existing.Text, text, StringComparison.Ordinal))
        {
            await _repository.UpdateAsync(
                existing with { LastSeenAt = now, SeenCount = existing.SeenCount + 1 },
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        var record = new ClipboardClipRecord
        {
            Id = Guid.NewGuid(),
            Kind = ClipboardClipKind.Text,
            CreatedAt = now,
            LastSeenAt = now,
            SeenCount = 1,
            SourceProcess = snapshot.SourceProcess,
            SourceWindow = snapshot.SourceWindow,
            FormatName = "text/plain",
            Text = text,
            ContentHash = hash,
            SizeBytes = Encoding.UTF8.GetByteCount(text),
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> RecordImageAsync(ClipboardSnapshot snapshot, CancellationToken cancellationToken)
    {
        EncodedImage image = snapshot.Image!;
        byte[] bytes = image.Bytes.ToArray();
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes)
        {
            LogOversizeImageSkipped(bytes.Length);
            return false;
        }

        string hash = Sha256Hex(bytes);
        DateTimeOffset now = _clock.UtcNow;

        ClipboardClipRecord? existing = await _repository
            .GetLatestByContentHashAsync(ClipboardClipKind.Image, hash, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            await _repository.UpdateAsync(
                existing with { LastSeenAt = now, SeenCount = existing.SeenCount + 1 },
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        var id = Guid.NewGuid();
        string imageRelative = _paths.BuildClipboardImageRelativePath(id, now);
        string imageAbsolute = _paths.ToAbsolute(imageRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(imageAbsolute)!);
        await File.WriteAllBytesAsync(imageAbsolute, bytes, cancellationToken).ConfigureAwait(false);

        string? thumbnailRelative = _paths.BuildThumbnailRelativePath(id);
        try
        {
            await _thumbnails
                .GenerateToFileAsync(imageAbsolute, _paths.ToAbsolute(thumbnailRelative), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogThumbnailFailed(ex);
            thumbnailRelative = null;
        }

        var record = new ClipboardClipRecord
        {
            Id = id,
            Kind = ClipboardClipKind.Image,
            CreatedAt = now,
            LastSeenAt = now,
            SeenCount = 1,
            SourceProcess = snapshot.SourceProcess,
            SourceWindow = snapshot.SourceWindow,
            FormatName = "image/png",
            ImagePath = imageRelative,
            ThumbnailPath = thumbnailRelative,
            ContentHash = hash,
            SizeBytes = bytes.Length,
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Hard-deletes the oldest non-favorite clips (and files) beyond the cap.</summary>
    internal async Task TrimToCapAsync(int maxItems, CancellationToken cancellationToken)
    {
        int total = await _repository.CountAsync(new ClipboardClipFilter(), cancellationToken).ConfigureAwait(false);
        int overflow = total - maxItems;
        if (overflow <= 0)
        {
            return;
        }

        IReadOnlyList<ClipboardClipRecord> oldest = await _repository.QueryAsync(
            new ClipboardClipFilter
            {
                IsFavorite = false,
                SortOrder = ClipboardClipSortOrder.OldestFirst,
                Limit = overflow,
            },
            cancellationToken).ConfigureAwait(false);

        foreach (ClipboardClipRecord clip in oldest)
        {
            await _repository.HardDeleteAsync(clip.Id, cancellationToken).ConfigureAwait(false);
            DeleteClipFiles(clip);
        }

        if (oldest.Count > 0)
        {
            LogTrimmedClips(oldest.Count);
        }
    }

    /// <summary>Deletes a clip's managed image/thumbnail files, best-effort.</summary>
    internal void DeleteClipFiles(ClipboardClipRecord clip)
    {
        TryDeleteFile(clip.ImagePath);
        TryDeleteFile(clip.ThumbnailPath);
    }

    private void TryDeleteFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            string absolute = _paths.ToAbsolute(relativePath);
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch (Exception ex)
        {
            LogFileDeleteFailed(ex, relativePath);
        }
    }

    /// <summary>Raises <see cref="HistoryChanged"/> after external mutations (UI deletes, clears).</summary>
    internal void NotifyHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Recording the clipboard change failed.")]
    private partial void LogClipProcessingFailed(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Skipped an oversize text clip ({Length} chars).")]
    private partial void LogOversizeTextSkipped(int length);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Skipped an oversize or empty image clip ({Bytes} bytes).")]
    private partial void LogOversizeImageSkipped(int bytes);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Could not generate a clipboard image thumbnail.")]
    private partial void LogThumbnailFailed(Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information,
        Message = "Trimmed {Count} old clipboard clips beyond the configured cap.")]
    private partial void LogTrimmedClips(int count);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
        Message = "Could not delete clip file '{Path}'.")]
    private partial void LogFileDeleteFailed(Exception exception, string path);
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Clipboard;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Common;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.Clipboard;

public sealed class ClipboardHistoryServiceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root;
    private readonly FakeClipRepository _repository = new();
    private readonly FakeSnapshotSource _snapshots = new();
    private readonly FakeSettingsService _settings = new();
    private readonly FakeClock _clock = new() { UtcNow = T0 };

    public ClipboardHistoryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "octadock-clip-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ClipboardHistoryService CreateService(FakeMonitor? monitor = null)
        => new(
            monitor ?? new FakeMonitor(),
            _snapshots,
            _repository,
            _settings,
            new TestStoragePaths(_root),
            new FakeThumbnailGenerator(),
            _clock,
            new Octadock.App.Tests.Fakes.AllowAllLicenseGate(),
            NullLogger<ClipboardHistoryService>.Instance);

    [Fact]
    public async Task Start_with_fresh_defaults_does_not_subscribe_or_start_the_clipboard_monitor()
    {
        await _settings.SaveAsync(OctadockSettings.Defaults);
        var monitor = new FakeMonitor();
        using ClipboardHistoryService service = CreateService(monitor);

        service.Start();

        monitor.SubscriberCount.Should().Be(0);
        monitor.StartCalls.Should().Be(0);
        _snapshots.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Explicit_consent_after_start_subscribes_and_starts_without_an_early_read()
    {
        await _settings.SaveAsync(OctadockSettings.Defaults);
        var monitor = new FakeMonitor();
        using ClipboardHistoryService service = CreateService(monitor);
        service.Start();

        await _settings.UpdateAsync(settings => settings with
        {
            Clipboard = settings.Clipboard with { MonitorEnabled = true },
        });

        monitor.SubscriberCount.Should().Be(1);
        monitor.StartCalls.Should().Be(1);
        _snapshots.Reads.Should().Be(0, "consent may attach monitoring but must not read historical clipboard content");

        await _settings.UpdateAsync(settings => settings with
        {
            Clipboard = settings.Clipboard with { MonitorEnabled = false },
        });

        monitor.SubscriberCount.Should().Be(0);
        monitor.StopCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Text_clip_is_recorded_with_hash_size_and_provenance()
    {
        using ClipboardHistoryService service = CreateService();
        _snapshots.Next = new ClipboardSnapshot
        {
            Text = "SELECT * FROM users;",
            SourceProcess = "code.exe",
            SourceWindow = "query.sql - VS Code",
        };

        await service.ProcessCurrentClipboardAsync();

        ClipboardClipRecord clip = _repository.Clips.Should().ContainSingle().Subject;
        clip.Kind.Should().Be(ClipboardClipKind.Text);
        clip.Text.Should().Be("SELECT * FROM users;");
        clip.ContentHash.Should().NotBeNullOrEmpty();
        clip.SizeBytes.Should().Be(20);
        clip.SourceProcess.Should().Be("code.exe");
        clip.SourceWindow.Should().Be("query.sql - VS Code");
        clip.CreatedAt.Should().Be(T0);
        clip.SeenCount.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_text_bumps_seen_count_instead_of_duplicating()
    {
        using ClipboardHistoryService service = CreateService();
        _snapshots.Next = new ClipboardSnapshot { Text = "hello" };
        await service.ProcessCurrentClipboardAsync();

        _clock.UtcNow = T0.AddMinutes(5);
        _snapshots.Next = new ClipboardSnapshot { Text = "hello" };
        await service.ProcessCurrentClipboardAsync();

        ClipboardClipRecord clip = _repository.Clips.Should().ContainSingle().Subject;
        clip.SeenCount.Should().Be(2);
        clip.CreatedAt.Should().Be(T0);
        clip.LastSeenAt.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public async Task Nothing_is_recorded_when_monitoring_is_disabled()
    {
        using ClipboardHistoryService service = CreateService();
        await _settings.SaveAsync(OctadockSettings.Defaults with
        {
            Clipboard = new ClipboardSettings { MonitorEnabled = false },
        });
        _snapshots.Next = new ClipboardSnapshot { Text = "secret" };

        await service.ProcessCurrentClipboardAsync();

        _repository.Clips.Should().BeEmpty();
        _snapshots.Reads.Should().Be(0, "a disabled monitor must not even read the clipboard");
    }

    [Fact]
    public async Task Oversize_text_is_skipped()
    {
        using ClipboardHistoryService service = CreateService();
        _snapshots.Next = new ClipboardSnapshot
        {
            Text = new string('x', ClipboardHistoryService.MaxTextLength + 1),
        };

        await service.ProcessCurrentClipboardAsync();

        _repository.Clips.Should().BeEmpty();
    }

    [Fact]
    public async Task Image_clip_writes_png_and_thumbnail_files()
    {
        using ClipboardHistoryService service = CreateService();
        byte[] png = [1, 2, 3, 4, 5];
        _snapshots.Next = new ClipboardSnapshot
        {
            Image = new EncodedImage(png, ExportImageFormat.Png),
            SourceProcess = "mspaint.exe",
        };

        await service.ProcessCurrentClipboardAsync();

        ClipboardClipRecord clip = _repository.Clips.Should().ContainSingle().Subject;
        clip.Kind.Should().Be(ClipboardClipKind.Image);
        clip.ImagePath.Should().NotBeNullOrEmpty();
        clip.ThumbnailPath.Should().NotBeNullOrEmpty();
        clip.SizeBytes.Should().Be(png.Length);

        string imageAbsolute = Path.Combine(_root, clip.ImagePath!.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(imageAbsolute).Should().BeTrue();
        File.ReadAllBytes(imageAbsolute).Should().Equal(png);
        File.Exists(Path.Combine(_root, clip.ThumbnailPath!.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();
    }

    [Fact]
    public async Task Repeated_image_bumps_seen_count()
    {
        using ClipboardHistoryService service = CreateService();
        byte[] png = [9, 9, 9];
        _snapshots.Next = new ClipboardSnapshot { Image = new EncodedImage(png, ExportImageFormat.Png) };
        await service.ProcessCurrentClipboardAsync();
        _snapshots.Next = new ClipboardSnapshot { Image = new EncodedImage(png, ExportImageFormat.Png) };

        await service.ProcessCurrentClipboardAsync();

        _repository.Clips.Should().ContainSingle().Which.SeenCount.Should().Be(2);
    }

    [Fact]
    public async Task Trim_removes_oldest_non_favorites_beyond_cap_and_their_files()
    {
        await _settings.SaveAsync(OctadockSettings.Defaults with
        {
            Clipboard = new ClipboardSettings { MonitorEnabled = true, MaxItems = 2 },
        });
        using ClipboardHistoryService service = CreateService();

        _snapshots.Next = new ClipboardSnapshot { Text = "oldest" };
        await service.ProcessCurrentClipboardAsync();

        _clock.UtcNow = T0.AddMinutes(1);
        _snapshots.Next = new ClipboardSnapshot { Text = "middle" };
        await service.ProcessCurrentClipboardAsync();

        _clock.UtcNow = T0.AddMinutes(2);
        _snapshots.Next = new ClipboardSnapshot { Text = "newest" };
        await service.ProcessCurrentClipboardAsync();

        _repository.Clips.Select(c => c.Text).Should().BeEquivalentTo(["middle", "newest"]);
    }

    [Fact]
    public async Task Trim_never_removes_favorites()
    {
        await _settings.SaveAsync(OctadockSettings.Defaults with
        {
            Clipboard = new ClipboardSettings { MonitorEnabled = true, MaxItems = 1 },
        });
        using ClipboardHistoryService service = CreateService();

        _snapshots.Next = new ClipboardSnapshot { Text = "keep me" };
        await service.ProcessCurrentClipboardAsync();
        ClipboardClipRecord favorite = _repository.Clips.Single();
        await _repository.UpdateAsync(favorite with { IsFavorite = true });

        _clock.UtcNow = T0.AddMinutes(1);
        _snapshots.Next = new ClipboardSnapshot { Text = "newer" };
        await service.ProcessCurrentClipboardAsync();

        _repository.Clips.Select(c => c.Text).Should().Contain("keep me");
    }

    [Fact]
    public async Task Empty_snapshot_records_nothing()
    {
        using ClipboardHistoryService service = CreateService();
        _snapshots.Next = null;

        await service.ProcessCurrentClipboardAsync();

        _repository.Clips.Should().BeEmpty();
    }

    // ---- Fakes -----------------------------------------------------------

    private sealed class FakeMonitor : IClipboardMonitor
    {
        private EventHandler? _clipboardChanged;

        public event EventHandler? ClipboardChanged
        {
            add => _clipboardChanged += value;
            remove => _clipboardChanged -= value;
        }

        public int SubscriberCount => _clipboardChanged?.GetInvocationList().Length ?? 0;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public void Start()
        {
            StartCalls++;
        }

        public void Stop()
        {
            StopCalls++;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeSnapshotSource : IClipboardSnapshotSource
    {
        public ClipboardSnapshot? Next { get; set; }

        public int Reads { get; private set; }

        public ClipboardSnapshot? TryRead(bool includeImages)
        {
            Reads++;
            return Next;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }

        public DateTimeOffset LocalNow => UtcNow.ToLocalTime();
    }

    private sealed class FakeThumbnailGenerator : IThumbnailGenerator
    {
        public int MaxEdge => 480;

        public EncodedImage Generate(CapturedFrame frame)
            => throw new NotSupportedException("Not used by these tests.");

        public Task GenerateToFileAsync(string sourceImagePath, string thumbnailPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            File.WriteAllBytes(thumbnailPath, [0xFF]);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public OctadockSettings Current { get; private set; } = OctadockSettings.Defaults with
        {
            Clipboard = OctadockSettings.Defaults.Clipboard with { MonitorEnabled = true },
        };

        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, new SettingsChangedEventArgs(settings));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            Func<OctadockSettings, OctadockSettings> mutate,
            CancellationToken cancellationToken = default)
            => SaveAsync(mutate(Current), cancellationToken);
    }

    private sealed class TestStoragePaths : IStoragePaths
    {
        public TestStoragePaths(string root)
        {
            RootDirectory = root;
            DatabasePath = Path.Combine(root, "octadock.db");
        }

        public string RootDirectory { get; }

        public string CapturesDirectory => Path.Combine(RootDirectory, "Captures");

        public string ProjectsDirectory => Path.Combine(RootDirectory, "Projects");

        public string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");

        public string ThumbnailsDirectory => Path.Combine(RootDirectory, "Thumbnails");

        public string TempExportsDirectory => Path.Combine(RootDirectory, "TempExports");

        public string LogsDirectory => Path.Combine(RootDirectory, "Logs");

        public string DatabasePath { get; }

        public void EnsureDirectories() => Directory.CreateDirectory(RootDirectory);

        public string ToAbsolute(string relativePath)
            => Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.GetFullPath(Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        public string ToRelative(string absolutePath) => Path.GetRelativePath(RootDirectory, absolutePath);

        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => $"Captures/{id:D}{extension}";

        public string BuildThumbnailRelativePath(Guid id) => $"Thumbnails/{id:D}.jpg";

        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => $"Recordings/{id:D}{extension}";

        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => $"Clipboard/{id:D}.png";
    }

    private sealed class FakeClipRepository : IClipboardClipRepository
    {
        public List<ClipboardClipRecord> Clips { get; } = [];

        public Task AddAsync(ClipboardClipRecord record, CancellationToken cancellationToken = default)
        {
            Clips.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ClipboardClipRecord record, CancellationToken cancellationToken = default)
        {
            int index = Clips.FindIndex(c => c.Id == record.Id);
            if (index >= 0)
            {
                Clips[index] = record;
            }

            return Task.CompletedTask;
        }

        public Task<ClipboardClipRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Clips.FirstOrDefault(c => c.Id == id));

        public Task<IReadOnlyList<ClipboardClipRecord>> QueryAsync(
            ClipboardClipFilter filter,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<ClipboardClipRecord> query = Clips;
            if (!filter.IncludeDeleted)
            {
                query = query.Where(c => !c.IsDeleted);
            }

            if (filter.Kinds is { Count: > 0 })
            {
                query = query.Where(c => filter.Kinds.Contains(c.Kind));
            }

            if (filter.IsFavorite is bool favorite)
            {
                query = query.Where(c => c.IsFavorite == favorite);
            }

            query = filter.SortOrder switch
            {
                ClipboardClipSortOrder.OldestFirst => query.OrderBy(c => c.LastSeenAt),
                ClipboardClipSortOrder.CreatedNewestFirst => query.OrderByDescending(c => c.CreatedAt),
                _ => query.OrderByDescending(c => c.LastSeenAt),
            };

            return Task.FromResult<IReadOnlyList<ClipboardClipRecord>>(
                query.Skip(filter.Offset).Take(filter.Limit).ToList());
        }

        public Task<int> CountAsync(ClipboardClipFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(filter.IncludeDeleted ? Clips.Count : Clips.Count(c => !c.IsDeleted));

        public Task<IReadOnlyList<ClipboardClipRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClipboardClipRecord>>(
                Clips.Where(c => !c.IsDeleted).OrderByDescending(c => c.LastSeenAt).Take(count).ToList());

        public Task<ClipboardClipRecord?> GetLatestByContentHashAsync(
            ClipboardClipKind kind,
            string contentHash,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Clips
                .Where(c => !c.IsDeleted && c.Kind == kind && c.ContentHash == contentHash)
                .OrderByDescending(c => c.LastSeenAt)
                .FirstOrDefault());

        public Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default)
        {
            int index = Clips.FindIndex(c => c.Id == id);
            if (index >= 0)
            {
                Clips[index] = Clips[index] with { DeletedAt = deletedAt };
            }

            return Task.CompletedTask;
        }

        public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
        {
            int index = Clips.FindIndex(c => c.Id == id);
            if (index >= 0)
            {
                Clips[index] = Clips[index] with { DeletedAt = null };
            }

            return Task.CompletedTask;
        }

        public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Clips.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClipboardClipRecord>> GetOlderThanAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClipboardClipRecord>>(
                Clips.Where(c => !c.IsDeleted && c.CreatedAt < cutoff).ToList());

        public Task<IReadOnlyList<ClipboardClipRecord>> GetSoftDeletedBeforeAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClipboardClipRecord>>(
                Clips.Where(c => c.DeletedAt is { } d && d < cutoff).ToList());
    }
}

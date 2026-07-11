using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.CaptureUx;
using Octadock.App.Services;
using Octadock.App.Tests.Fakes;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Common;
using Octadock.Core.Context;
using Octadock.Core.Imaging;
using Octadock.Core.Io;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

public sealed class ShelfItemViewModelTests
{
    [Theory]
    [InlineData(1200, 200, 228, 38, true)]
    [InlineData(1600, 900, 220, 124, false)]
    public void Display_metrics_preserve_capture_aspect_ratio_and_compact_short_overlays(
        int pixelWidth,
        int pixelHeight,
        double expectedWidth,
        double expectedHeight,
        bool expectedCompactOverlay)
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        using ServiceProvider services = BuildServices(paths, new CountingImageLoadService());
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = System.IO.Path.Combine("Captures", "aspect.png"),
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
        };
        var viewModel = new ShelfItemViewModel(record, services, _ => Task.CompletedTask, _ => { });

        viewModel.ApplyDisplayMetrics(228, 124);

        viewModel.DisplayWidth.Should().Be(expectedWidth);
        viewModel.DisplayHeight.Should().Be(expectedHeight);
        viewModel.UseCompactOverlay.Should().Be(expectedCompactOverlay);
    }

    [Fact]
    public void Row_metadata_keeps_the_extension_visible_and_describes_the_capture()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        using ServiceProvider services = BuildServices(paths, new CountingImageLoadService());
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Window,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = System.IO.Path.Combine(
                "Captures",
                "A very long product launch review screenshot that must be ellipsized.png"),
            PixelWidth = 3440,
            PixelHeight = 1440,
            Source = new CaptureSource("Figma.exe", "Launch review", null),
        };

        var viewModel = new ShelfItemViewModel(record, services, _ => Task.CompletedTask, _ => { });

        viewModel.FileStem.Should().Be("A very long product launch review screenshot that must be ellipsized");
        viewModel.FileExtension.Should().Be(".png");
        viewModel.ArtifactTypeLabel.Should().Be("Window capture");
        viewModel.MetadataLine.Should().Be("3440 × 1440  •  Window capture  •  Figma");
        viewModel.CopyActionLabel.Should().Be("Copy image");
    }

    [Fact]
    public void Constructor_does_not_decode_recording_file_as_image_when_thumbnail_is_missing()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var images = new CountingImageLoadService();
        string relativeRecording = System.IO.Path.Combine("Recordings", "clip.mp4");
        string absoluteRecording = paths.ToAbsolute(relativeRecording);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absoluteRecording)!);
        File.WriteAllBytes(absoluteRecording, [0, 0, 0, 0]);

        using ServiceProvider services = BuildServices(paths, images);
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Recording,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = relativeRecording,
            PixelWidth = 1920,
            PixelHeight = 1080,
            DurationMs = 1250,
        };

        var viewModel = new ShelfItemViewModel(record, services, _ => Task.CompletedTask, _ => { });

        images.LoadFromFileCalls.Should().Be(0);
        viewModel.Thumbnail.Should().BeNull();
        viewModel.IsRecording.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_uses_a_collision_safe_name_in_the_configured_directory()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var settings = new FakeSettingsService();
        string saveDirectory = System.IO.Path.Combine(temp.Path, "Exports");
        settings.Current = OctadockSettings.Defaults with
        {
            Capture = OctadockSettings.Defaults.Capture with { SaveDirectory = saveDirectory },
        };

        string relativeSource = System.IO.Path.Combine("Captures", "Screenshot.png");
        string absoluteSource = paths.ToAbsolute(relativeSource);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absoluteSource)!);
        await File.WriteAllTextAsync(absoluteSource, "NEW CAPTURE");
        Directory.CreateDirectory(saveDirectory);
        string occupiedDestination = System.IO.Path.Combine(saveDirectory, "Screenshot.png");
        await File.WriteAllTextAsync(occupiedDestination, "EXISTING FILE");

        using ServiceProvider services = BuildServices(paths, new CountingImageLoadService(), settings: settings);
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = relativeSource,
            PixelWidth = 100,
            PixelHeight = 100,
        };
        int completed = 0;
        var viewModel = new ShelfItemViewModel(record, services, _ => Task.CompletedTask, _ => completed++);

        await viewModel.SaveCommand.ExecuteAsync(null);

        File.ReadAllText(occupiedDestination).Should().Be("EXISTING FILE");
        File.ReadAllText(System.IO.Path.Combine(saveDirectory, "Screenshot (2).png"))
            .Should().Be("NEW CAPTURE");
        completed.Should().Be(1);
    }

    [Fact]
    public async Task DiscardCommand_removes_the_card_without_deleting_history()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var captures = new TestCaptureRepository();
        var notifications = new RecordingNotificationService();
        using ServiceProvider services = BuildServices(
            paths,
            new CountingImageLoadService(),
            captures,
            notifications: notifications);
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = System.IO.Path.Combine("Captures", "capture.png"),
        };
        int removed = 0;
        var viewModel = new ShelfItemViewModel(record, services, _ =>
        {
            removed++;
            return Task.CompletedTask;
        }, _ => { });

        await viewModel.DiscardCommand.ExecuteAsync(null);

        captures.SoftDeleteCalls.Should().Be(0);
        removed.Should().Be(1);
        notifications.LastTitle.Should().Be("Removed from Shelf");
    }

    [Fact]
    public async Task Shelf_restore_readds_the_card_without_touching_history()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var captures = new TestCaptureRepository();
        var settings = new FakeSettingsService();
        using ServiceProvider services = BuildServices(
            paths,
            new CountingImageLoadService(),
            captures,
            settings);
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = System.IO.Path.Combine("Captures", "capture.png"),
        };
        var shelf = new ShelfViewModel(services, settings, NullLoggerFactory.Instance);
        shelf.Add(record);

        await shelf.Items.Single().DiscardCommand.ExecuteAsync(null);

        captures.IsSoftDeleted.Should().BeFalse();
        shelf.Items.Should().BeEmpty();

        (await shelf.RestoreRecentlyClosedAsync()).Should().BeTrue();
        captures.RestoreCalls.Should().Be(0);
        captures.IsSoftDeleted.Should().BeFalse();
        shelf.Items.Should().ContainSingle().Which.Record.Id.Should().Be(record.Id);
    }

    [Fact]
    public async Task Clear_all_empties_the_shelf_and_restores_the_newest_capture_without_database_restore()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var captures = new TestCaptureRepository();
        var settings = new FakeSettingsService();
        using ServiceProvider services = BuildServices(
            paths,
            new CountingImageLoadService(),
            captures,
            settings);
        var older = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now.AddMinutes(-1),
            OriginalPath = System.IO.Path.Combine("Captures", "older.png"),
        };
        var newest = older with
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = System.IO.Path.Combine("Captures", "newest.png"),
        };
        var shelf = new ShelfViewModel(services, settings, NullLoggerFactory.Instance);
        int emptied = 0;
        shelf.Emptied += (_, _) => emptied++;
        shelf.Add(older);
        shelf.Add(newest);

        shelf.CloseAll();

        shelf.Items.Should().BeEmpty();
        emptied.Should().Be(1);
        captures.RestoreCalls.Should().Be(0, "clearing the shelf does not soft-delete history");

        (await shelf.RestoreRecentlyClosedAsync()).Should().BeTrue();
        shelf.Items.Should().ContainSingle().Which.Record.Id.Should().Be(newest.Id);
        captures.RestoreCalls.Should().Be(0);
    }

    [Fact]
    public async Task Add_to_context_snapshots_the_capture_into_the_newest_package()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var notifications = new RecordingNotificationService();
        var contextRepository = new InMemoryContextRepository();
        string relativeSource = System.IO.Path.Combine("Captures", "context-source.png");
        string absoluteSource = paths.ToAbsolute(relativeSource);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absoluteSource)!);
        await File.WriteAllBytesAsync(absoluteSource, [1, 2, 3, 4]);

        using ServiceProvider services = BuildServices(
            paths,
            new CountingImageLoadService(),
            notifications: notifications,
            contextRepository: contextRepository);
        ContextPackage existing = await contextRepository.CreatePackageAsync(
            "Launch handoff",
            DateTimeOffset.Now);
        var record = new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = relativeSource,
            PixelWidth = 1280,
            PixelHeight = 720,
        };
        int completed = 0;
        var viewModel = new ShelfItemViewModel(record, services, _ => Task.CompletedTask, _ => completed++);

        await viewModel.AddToContextCommand.ExecuteAsync(null);

        ContextPackage package = (await contextRepository.GetPackageAsync(existing.Id))!;
        package.Items.Should().ContainSingle().Which.SourceCaptureId.Should().Be(record.Id);
        completed.Should().Be(1);
        notifications.LastTitle.Should().Be("Added to Context");
    }

    private static ServiceProvider BuildServices(
        IStoragePaths paths,
        IImageLoadService images,
        ICaptureRepository? captures = null,
        ISettingsService? settings = null,
        INotificationService? notifications = null,
        IContextRepository? contextRepository = null)
    {
        var services = new ServiceCollection();
        INotificationService notificationService = notifications ?? new NoopNotificationService();
        ISafeFileWriter safeFileWriter = new SafeFileWriter(
            new FileRevisionStore(System.IO.Path.Combine(paths.RootDirectory, "Revisions")));
        services.AddSingleton(images);
        services.AddSingleton(paths);
        services.AddSingleton<IClipboardService, NoopClipboardService>();
        services.AddSingleton(captures ?? new TestCaptureRepository());
        services.AddSingleton<IActionRepository, NoopActionRepository>();
        services.AddSingleton(settings ?? new FakeSettingsService());
        services.AddSingleton(notificationService);
        services.AddSingleton(safeFileWriter);
        if (contextRepository is not null)
        {
            services.AddSingleton(new ContextService(
                contextRepository,
                paths,
                safeFileWriter,
                new AllowAllLicenseGate(),
                notificationService,
                new FixedClock(),
                NullLogger<ContextService>.Instance));
        }

        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        return services.BuildServiceProvider();
    }

    private sealed class CountingImageLoadService : IImageLoadService
    {
        public int LoadFromFileCalls { get; private set; }

        public System.Windows.Media.Imaging.BitmapSource LoadFromFile(string absolutePath)
        {
            LoadFromFileCalls++;
            throw new InvalidOperationException("The recording file should not be decoded as an image.");
        }

        public System.Windows.Media.Imaging.BitmapSource ToBitmapSource(CapturedFrame frame)
            => throw new InvalidOperationException("This test does not convert captured frames.");

        public byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource image)
            => throw new InvalidOperationException("This test does not encode images.");
    }

    private sealed class FakeStoragePaths(string root) : IStoragePaths
    {
        public string RootDirectory { get; } = root;

        public string CapturesDirectory => System.IO.Path.Combine(RootDirectory, "Captures");

        public string ProjectsDirectory => System.IO.Path.Combine(RootDirectory, "Projects");

        public string RecordingsDirectory => System.IO.Path.Combine(RootDirectory, "Recordings");

        public string ThumbnailsDirectory => System.IO.Path.Combine(RootDirectory, "Thumbnails");

        public string TempExportsDirectory => System.IO.Path.Combine(RootDirectory, "TempExports");

        public string LogsDirectory => System.IO.Path.Combine(RootDirectory, "Logs");

        public string DatabasePath => System.IO.Path.Combine(RootDirectory, "octadock.db");

        public void EnsureDirectories()
        {
        }

        public string ToAbsolute(string relativePath)
            => System.IO.Path.IsPathRooted(relativePath)
                ? relativePath
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(RootDirectory, relativePath));

        public string ToRelative(string absolutePath)
            => System.IO.Path.GetRelativePath(RootDirectory, absolutePath);

        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => System.IO.Path.Combine("Captures", $"{id}{extension}");

        public string BuildThumbnailRelativePath(Guid id)
            => System.IO.Path.Combine("Thumbnails", $"{id}.jpg");

        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => System.IO.Path.Combine("Recordings", $"{id}{extension}");

        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => System.IO.Path.Combine("Clipboard", $"{id}.png");
    }

    private sealed class NoopClipboardService : IClipboardService
    {
        public bool ContainsImage() => false;

        public void SetImage(EncodedImage image)
        {
        }

        public void SetImageFromFile(string filePath)
        {
        }

        public void SetFileDropList(IEnumerable<string> filePaths)
        {
        }

        public void SetText(string text)
        {
        }

        public string? TryGetText() => null;

        public EncodedImage? TryGetImage() => null;
    }

    private sealed class TestCaptureRepository : ICaptureRepository
    {
        public Exception? SoftDeleteFailure { get; set; }

        public Exception? RestoreFailure { get; set; }

        public int SoftDeleteCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public bool IsSoftDeleted { get; private set; }

        public Task AddAsync(CaptureRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<CaptureRecord?>(null);

        public Task<IReadOnlyList<CaptureRecord>> QueryAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);

        public Task<int> CountAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);

        public Task UpdateAsync(CaptureRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default)
        {
            SoftDeleteCalls++;
            if (SoftDeleteFailure is not null)
            {
                return Task.FromException(SoftDeleteFailure);
            }

            IsSoftDeleted = true;
            return Task.CompletedTask;
        }

        public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
        {
            RestoreCalls++;
            if (RestoreFailure is not null)
            {
                return Task.FromException(RestoreFailure);
            }

            IsSoftDeleted = false;
            return Task.CompletedTask;
        }

        public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<CaptureRecord>> GetOlderThanAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);

        public Task<IReadOnlyList<CaptureRecord>> GetSoftDeletedBeforeAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);
    }

    private sealed class NoopActionRepository : IActionRepository
    {
        public Task AddAsync(ActionRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ActionRecord>> GetForCaptureAsync(Guid captureId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ActionRecord>>([]);
    }

    private sealed class InMemoryContextRepository : IContextRepository
    {
        private readonly Dictionary<Guid, ContextPackage> _packages = new();

        public Task<ContextPackage> CreatePackageAsync(
            string name,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            var package = new ContextPackage { Id = Guid.NewGuid(), Name = name, CreatedAt = now };
            _packages[package.Id] = package;
            return Task.FromResult(package);
        }

        public Task RenamePackageAsync(
            Guid packageId,
            string name,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            if (_packages.TryGetValue(packageId, out ContextPackage? package))
            {
                _packages[packageId] = package with { Name = name };
            }

            return Task.CompletedTask;
        }

        public Task DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            _packages.Remove(packageId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContextPackage>>(_packages.Values.ToList());

        public Task<ContextPackage?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
            => Task.FromResult(_packages.GetValueOrDefault(packageId));

        public Task AddItemAsync(
            Guid packageId,
            ContextItem item,
            CancellationToken cancellationToken = default)
        {
            ContextPackage package = _packages[packageId];
            _packages[packageId] = package with { Items = package.Items.Append(item).ToList() };
            return Task.CompletedTask;
        }

        public Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        {
            foreach (Guid packageId in _packages.Keys.ToList())
            {
                ContextPackage package = _packages[packageId];
                _packages[packageId] = package with
                {
                    Items = package.Items.Where(item => item.Id != itemId).ToList(),
                };
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset LocalNow => UtcNow;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public OctadockSettings Current { get; set; } = OctadockSettings.Defaults;

        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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

    private sealed class NoopNotificationService : INotificationService
    {
        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
        {
        }
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public string? LastTitle { get; private set; }

        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
            => LastTitle = title;
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "octadock-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

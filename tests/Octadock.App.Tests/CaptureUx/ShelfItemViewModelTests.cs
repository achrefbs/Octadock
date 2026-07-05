using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.CaptureUx;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

public sealed class ShelfItemViewModelTests
{
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

    private static ServiceProvider BuildServices(
        IStoragePaths paths,
        IImageLoadService images)
    {
        var services = new ServiceCollection();
        services.AddSingleton(images);
        services.AddSingleton(paths);
        services.AddSingleton<IClipboardService, NoopClipboardService>();
        services.AddSingleton<ICaptureRepository, NoopCaptureRepository>();
        services.AddSingleton<IActionRepository, NoopActionRepository>();
        services.AddSingleton<ISettingsService, FakeSettingsService>();
        services.AddSingleton<INotificationService, NoopNotificationService>();
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

    private sealed class NoopCaptureRepository : ICaptureRepository
    {
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
            => Task.CompletedTask;

        public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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

    private sealed class FakeSettingsService : ISettingsService
    {
        public OctadockSettings Current { get; private set; } = OctadockSettings.Defaults;

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

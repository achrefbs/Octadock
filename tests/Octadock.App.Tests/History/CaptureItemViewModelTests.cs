using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.History;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests.History;

public sealed class CaptureItemViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"octadock-history-thumbnail-{Guid.NewGuid():N}");

    public CaptureItemViewModelTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Missing_thumbnail_remains_retryable_when_generation_finishes_later()
    {
        var paths = new TestStoragePaths(_root);
        var images = new RecordingImages();
        string relative = Path.Combine("Thumbnails", "late.png");
        var item = new CaptureItemViewModel(new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.UtcNow,
            OriginalPath = Path.Combine("Captures", "capture.png"),
            ThumbnailPath = relative,
        }, images, paths);

        await item.EnsureThumbnailAsync();
        item.Thumbnail.Should().BeNull();

        string thumbnail = paths.ToAbsolute(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        await File.WriteAllBytesAsync(thumbnail, [1]);
        await item.EnsureThumbnailAsync();

        images.Paths.Should().ContainSingle().Which.Should().Be(thumbnail);
        item.Thumbnail.Should().NotBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData(CaptureType.Recording, ".mp4")]
    [InlineData(CaptureType.Area, ".png")]
    public void Copy_uses_file_drop_for_recordings_and_bitmap_for_images(CaptureType type, string extension)
    {
        var clipboard = new RecordingClipboard();
        var notifications = new RecordingNotifications();
        HistoryViewModel history = CreateHistoryForCopy(type, extension, clipboard, notifications);
        string expectedPath = history.SelectedItem!.AbsolutePath;
        File.WriteAllBytes(expectedPath, [1]);

        history.CopyCommand.Execute(null);

        if (type == CaptureType.Recording)
        {
            clipboard.FilePaths.Should().Equal(expectedPath);
            clipboard.ImagePath.Should().BeNull();
            history.StatusMessage.Should().Be("Recording file copied.");
        }
        else
        {
            clipboard.ImagePath.Should().Be(expectedPath);
            clipboard.FilePaths.Should().BeEmpty();
            history.StatusMessage.Should().Be("Capture copied.");
        }
        notifications.LastKind.Should().Be(NotificationKind.Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_file_or_clipboard_failure_is_visible(bool clipboardFails)
    {
        var clipboard = new RecordingClipboard { ThrowOnWrite = clipboardFails };
        var notifications = new RecordingNotifications();
        HistoryViewModel history = CreateHistoryForCopy(CaptureType.Recording, ".mp4", clipboard, notifications);
        if (clipboardFails) File.WriteAllBytes(history.SelectedItem!.AbsolutePath, [1]);

        history.CopyCommand.Execute(null);

        history.StatusMessage.Should().StartWith("Copy failed.");
        notifications.LastTitle.Should().Be("Copy failed");
        notifications.LastKind.Should().Be(clipboardFails ? NotificationKind.Error : NotificationKind.Warning);
        clipboard.ImagePath.Should().BeNull();
        clipboard.FilePaths.Should().BeEmpty();
    }

    private HistoryViewModel CreateHistoryForCopy(CaptureType type, string extension,
        IClipboardService clipboard, INotificationService notifications)
    {
        var paths = new TestStoragePaths(_root);
        var images = new RecordingImages();
        return new HistoryViewModel(null!, images, paths, clipboard, null!, notifications, null!, null!,
            NullLogger<HistoryViewModel>.Instance)
        {
            SelectedItem = new CaptureItemViewModel(new CaptureRecord
            {
                Id = Guid.NewGuid(), Type = type, CreatedAt = DateTimeOffset.UtcNow,
                OriginalPath = "history-copy" + extension,
            }, images, paths),
        };
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public string? LastTitle { get; private set; }
        public NotificationKind LastKind { get; private set; }
        public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info, Action? clickAction = null)
        {
            LastTitle = title;
            LastKind = kind;
        }
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public bool ThrowOnWrite { get; init; }
        public string? ImagePath { get; private set; }
        public List<string> FilePaths { get; } = [];
        public void SetImageFromFile(string filePath)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("Clipboard unavailable");
            ImagePath = filePath;
        }
        public void SetFileDropList(IEnumerable<string> filePaths)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("Clipboard unavailable");
            FilePaths.AddRange(filePaths);
        }
        public bool ContainsImage() => false;
        public void SetImage(EncodedImage image) => throw new NotSupportedException();
        public void SetText(string text) => throw new NotSupportedException();
        public string? TryGetText() => null;
        public EncodedImage? TryGetImage() => null;
    }

    private sealed class RecordingImages : IImageLoadService
    {
        public List<string> Paths { get; } = [];

        public System.Windows.Media.Imaging.BitmapSource LoadFromFile(string absolutePath)
        {
            Paths.Add(absolutePath);
            System.Windows.Media.Imaging.BitmapSource bitmap =
                System.Windows.Media.Imaging.BitmapSource.Create(
                    1,
                    1,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    new byte[] { 0, 0, 0, 255 },
                    4);
            bitmap.Freeze();
            return bitmap;
        }

        public System.Windows.Media.Imaging.BitmapSource ToBitmapSource(CapturedFrame frame)
            => throw new NotSupportedException();

        public byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource image)
            => throw new NotSupportedException();
    }

    private sealed class TestStoragePaths(string root) : IStoragePaths
    {
        public string RootDirectory { get; } = root;
        public string CapturesDirectory => Path.Combine(RootDirectory, "Captures");
        public string ProjectsDirectory => Path.Combine(RootDirectory, "Projects");
        public string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");
        public string ThumbnailsDirectory => Path.Combine(RootDirectory, "Thumbnails");
        public string TempExportsDirectory => Path.Combine(RootDirectory, "TempExports");
        public string LogsDirectory => Path.Combine(RootDirectory, "Logs");
        public string DatabasePath => Path.Combine(RootDirectory, "octadock.db");
        public void EnsureDirectories() => Directory.CreateDirectory(RootDirectory);
        public string ToAbsolute(string relativePath) => Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        public string ToRelative(string absolutePath) => Path.GetRelativePath(RootDirectory, absolutePath);
        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Captures", $"{id:N}{extension}");
        public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id:N}.jpg");
        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Recordings", $"{id:N}{extension}");
        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => Path.Combine("Clipboard", $"{id:N}.png");
    }
}

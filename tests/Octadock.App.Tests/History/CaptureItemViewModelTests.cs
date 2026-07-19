using FluentAssertions;
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

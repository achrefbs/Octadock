using System.IO;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Editing;
using Octadock.App.Services;
using Octadock.App.Tests.Fakes;
using Octadock.Core.Abstractions;
using Octadock.Core.Annotations;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Projects;
using Xunit;

namespace Octadock.App.Tests.Editing;

/// <summary>
/// Annotation ingress after the preview/pin removal: the editor opens only for
/// registered Octadock captures and capture-derived .octadock projects. Arbitrary
/// files are refused visibly; missing/corrupt sources fail truthfully; a real
/// editor launch records exactly one honest Annotated action.
/// </summary>
public sealed class AnnotationServiceTests
{
    [Fact]
    public async Task OpenAsync_opens_the_editor_for_a_registered_capture_and_records_Annotated()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var record = NewRecord(paths, "capture.png", writeFile: true);
        var images = new FakeImageLoadService();
        var actions = new RecordingActionRepository();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, images, actions: actions, launches: launches);

        bool opened = await service.OpenAsync(record);

        opened.Should().BeTrue();
        launches.Should().ContainSingle();
        images.LoadFromFileCalls.Should().Equal(paths.ToAbsolute(record.OriginalPath));
        actions.Rows.Should().ContainSingle().Which.ActionType.Should().Be(ActionType.Annotated);
    }

    [Fact]
    public async Task OpenAsync_prefers_an_existing_project_over_the_original_raster()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var record = NewRecord(paths, "capture.png", writeFile: true);
        string projectRelative = Path.Combine("Projects", "capture.octadock");
        string projectAbsolute = paths.ToAbsolute(projectRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(projectAbsolute)!);
        await File.WriteAllBytesAsync(projectAbsolute, [1]);
        record = record with { ProjectPath = projectRelative };
        var images = new FakeImageLoadService();
        var projects = new FakeProjectSerializer();
        var actions = new RecordingActionRepository();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, images, projects, actions: actions, launches: launches);

        bool opened = await service.OpenAsync(record);

        opened.Should().BeTrue();
        projects.LoadCalls.Should().Equal(projectAbsolute);
        images.LoadFromFileCalls.Should().BeEmpty("an existing project loads its packaged base image, not the raster");
        launches.Should().ContainSingle();
        actions.Rows.Should().ContainSingle().Which.ActionType.Should().Be(ActionType.Annotated);
    }

    [Fact]
    public async Task OpenAsync_missing_source_fails_visibly_without_recording_an_action()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var record = NewRecord(paths, "gone.png", writeFile: false);
        var notifications = new RecordingNotificationService();
        var actions = new RecordingActionRepository();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, new FakeImageLoadService(), actions: actions, notifications: notifications, launches: launches);

        bool opened = await service.OpenAsync(record);

        opened.Should().BeFalse();
        launches.Should().BeEmpty();
        actions.Rows.Should().BeEmpty("a failed open is not an annotation");
        notifications.LastTitle.Should().Be("Annotate failed");
        notifications.LastMessage.Should().Contain("missing");
    }

    [Fact]
    public async Task OpenAsync_license_denial_opens_nothing_and_records_nothing()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var record = NewRecord(paths, "capture.png", writeFile: true);
        var actions = new RecordingActionRepository();
        var launches = new List<string>();
        AnnotationService service = CreateService(
            paths,
            new FakeImageLoadService(),
            actions: actions,
            launches: launches,
            gate: new DenyLicenseGate());

        bool opened = await service.OpenAsync(record);

        opened.Should().BeFalse();
        launches.Should().BeEmpty();
        actions.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenFileAsync_rejects_an_arbitrary_image_file()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        string arbitrary = Path.Combine(temp.Path, "Downloads", "photo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(arbitrary)!);
        await File.WriteAllBytesAsync(arbitrary, [1]);
        var notifications = new RecordingNotificationService();
        var actions = new RecordingActionRepository();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, new FakeImageLoadService(), actions: actions, notifications: notifications, launches: launches);

        bool opened = await service.OpenFileAsync(arbitrary);

        opened.Should().BeFalse();
        launches.Should().BeEmpty();
        actions.Rows.Should().BeEmpty();
        notifications.LastMessage.Should().Contain("Only Octadock captures");
    }

    [Fact]
    public async Task OpenFileAsync_rejects_a_registered_recording_file()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var record = NewRecord(paths, "clip.mp4", writeFile: true) with { Type = CaptureType.Recording };
        var captures = new FakeCaptureRepository(record);
        var notifications = new RecordingNotificationService();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, new FakeImageLoadService(), captures: captures, notifications: notifications, launches: launches);

        bool opened = await service.OpenFileAsync(paths.ToAbsolute(record.OriginalPath));

        opened.Should().BeFalse();
        launches.Should().BeEmpty();
        notifications.LastMessage.Should().Contain("Only Octadock captures");
    }

    [Fact]
    public async Task OpenFileAsync_accepts_a_registered_capture_image()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        var record = NewRecord(paths, "capture.png", writeFile: true);
        var captures = new FakeCaptureRepository(record);
        var actions = new RecordingActionRepository();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, new FakeImageLoadService(), captures: captures, actions: actions, launches: launches);

        bool opened = await service.OpenFileAsync(paths.ToAbsolute(record.OriginalPath));

        opened.Should().BeTrue();
        launches.Should().ContainSingle();
        actions.Rows.Should().ContainSingle().Which.ActionType.Should().Be(ActionType.Annotated);
    }

    [Fact]
    public async Task OpenFileAsync_corrupt_project_fails_visibly_instead_of_throwing()
    {
        using var temp = new TempRoot();
        var paths = new FakeStoragePaths(temp.Path);
        string projectPath = Path.Combine(temp.Path, "damaged.octadock");
        await File.WriteAllBytesAsync(projectPath, [1, 2, 3]);
        var projects = new FakeProjectSerializer { LoadFailure = new InvalidDataException("not a zip") };
        var notifications = new RecordingNotificationService();
        var launches = new List<string>();
        AnnotationService service = CreateService(paths, new FakeImageLoadService(), projects, notifications: notifications, launches: launches);

        bool opened = await service.OpenFileAsync(projectPath);

        opened.Should().BeFalse();
        launches.Should().BeEmpty();
        notifications.LastTitle.Should().Be("Annotate failed");
        notifications.LastMessage.Should().Contain("could not be opened");
    }

    private static AnnotationService CreateService(
        IStoragePaths paths,
        IImageLoadService images,
        FakeProjectSerializer? projects = null,
        RecordingActionRepository? actions = null,
        RecordingNotificationService? notifications = null,
        List<string>? launches = null,
        FakeCaptureRepository? captures = null,
        ILicenseGate? gate = null)
    {
        var service = new AnnotationService(
            images,
            projects ?? new FakeProjectSerializer(),
            paths,
            gate ?? new AllowAllLicenseGate(),
            captures ?? new FakeCaptureRepository(),
            actions ?? new RecordingActionRepository(),
            notifications ?? new RecordingNotificationService(),
            NullLogger<AnnotationService>.Instance);
        service.EditorLauncher = (_, _, _, _, title) =>
        {
            launches?.Add(title);
            return Task.CompletedTask;
        };
        return service;
    }

    private static CaptureRecord NewRecord(FakeStoragePaths paths, string fileName, bool writeFile)
    {
        string relative = Path.Combine("Captures", fileName);
        string absolute = paths.ToAbsolute(relative);
        if (writeFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllBytes(absolute, [1]);
        }

        return new CaptureRecord
        {
            Id = Guid.NewGuid(),
            Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.Now,
            OriginalPath = relative,
            PixelWidth = 100,
            PixelHeight = 100,
        };
    }

    private sealed class FakeImageLoadService : IImageLoadService
    {
        public List<string> LoadFromFileCalls { get; } = [];

        public System.Windows.Media.Imaging.BitmapSource LoadFromFile(string absolutePath)
        {
            LoadFromFileCalls.Add(absolutePath);
            System.Windows.Media.Imaging.BitmapSource bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
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

    private sealed class FakeProjectSerializer : IProjectSerializer
    {
        public Exception? LoadFailure { get; set; }

        public List<string> LoadCalls { get; } = [];

        public Task SaveAsync(
            string projectPath,
            AnnotationDocument document,
            ReadOnlyMemory<byte> baseImagePng,
            ReadOnlyMemory<byte>? previewPng,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ProjectLoadResult> LoadAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            LoadCalls.Add(projectPath);
            if (LoadFailure is not null)
            {
                return Task.FromException<ProjectLoadResult>(LoadFailure);
            }

            return Task.FromResult(new ProjectLoadResult(
                new ProjectManifest(),
                new AnnotationDocument(new PixelSize(1, 1)),
                OnePixelPng()));
        }
    }

    private static byte[] OnePixelPng()
    {
        System.Windows.Media.Imaging.BitmapSource bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
            1,
            1,
            96,
            96,
            System.Windows.Media.PixelFormats.Bgra32,
            null,
            new byte[] { 0, 0, 0, 255 },
            4);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class FakeCaptureRepository : ICaptureRepository
    {
        private readonly IReadOnlyList<CaptureRecord> _recent;

        public FakeCaptureRepository(params CaptureRecord[] recent) => _recent = recent;

        public Task AddAsync(CaptureRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_recent.FirstOrDefault(r => r.Id == id));
        public Task<IReadOnlyList<CaptureRecord>> QueryAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(_recent);
        public Task<int> CountAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(_recent.Count);
        public Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
            => Task.FromResult(_recent);
        public Task UpdateAsync(CaptureRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CaptureRecord>> GetOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);
        public Task<IReadOnlyList<CaptureRecord>> GetSoftDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);
    }

    private sealed class RecordingActionRepository : IActionRepository
    {
        public List<ActionRecord> Rows { get; } = [];

        public Task AddAsync(ActionRecord record, CancellationToken cancellationToken = default)
        {
            Rows.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActionRecord>> GetForCaptureAsync(Guid captureId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ActionRecord>>(Rows.Where(r => r.CaptureId == captureId).ToList());
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public string? LastTitle { get; private set; }

        public string? LastMessage { get; private set; }

        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
        {
            LastTitle = title;
            LastMessage = message;
        }
    }

    private sealed class DenyLicenseGate : ILicenseGate
    {
        public LicenseState State { get; } = new(LicenseMode.TrialExpired, null, null, false, false, "expired");

        public bool AllowsFullUse => false;

        public event EventHandler<LicenseState>? Refused { add { } remove { } }

        public bool Allow(GatedFeature feature) => false;
    }

    private sealed class FakeStoragePaths(string root) : IStoragePaths
    {
        public string RootDirectory { get; } = root;
        public string CapturesDirectory => Path.Combine(RootDirectory, "Captures");
        public string ProjectsDirectory => Path.Combine(RootDirectory, "Projects");
        public string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");
        public string ThumbnailsDirectory => Path.Combine(RootDirectory, "Thumbnails");
        public string TempExportsDirectory => Path.Combine(RootDirectory, "TempExports");
        public string LogsDirectory => Path.Combine(RootDirectory, "Logs");
        public string DatabasePath => Path.Combine(RootDirectory, "octadock.db");
        public void EnsureDirectories() { }
        public string ToAbsolute(string relativePath)
            => Path.IsPathRooted(relativePath) ? relativePath : Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        public string ToRelative(string absolutePath) => Path.GetRelativePath(RootDirectory, absolutePath);
        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Captures", $"{id}{extension}");
        public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id}.jpg");
        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Recordings", $"{id}{extension}");
        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => Path.Combine("Clipboard", $"{id}.png");
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

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Recording;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class RecordingControllerTests
{
    [Fact]
    public async Task ToggleAsync_deletes_partial_output_when_start_fails()
    {
        using var temp = new TempRoot();
        var engine = new FakeRecordingEngine
        {
            StartException = new InvalidOperationException("encoder refused the output"),
        };
        var notifications = new RecordingNotifications();
        RecordingController controller = CreateController(temp.Path, engine, notifications);

        await controller.ToggleAsync();

        engine.LastOptions.Should().NotBeNull();
        File.Exists(engine.LastOptions!.OutputPath).Should().BeFalse();
        notifications.Last.Should().Be(
            new RecordingNotification("Recording failed", "encoder refused the output", NotificationKind.Error));
    }

    [Fact]
    public async Task ToggleAsync_deletes_partial_output_when_stop_fails()
    {
        using var temp = new TempRoot();
        var engine = new FakeRecordingEngine();
        var notifications = new RecordingNotifications();
        var captures = new RecordingCaptureRepository();
        RecordingController controller = CreateController(temp.Path, engine, notifications, captures);

        await controller.ToggleAsync();
        string output = engine.LastOptions!.OutputPath;
        File.Exists(output).Should().BeTrue();

        engine.StopException = new IOException("disk full");
        await controller.ToggleAsync();

        File.Exists(output).Should().BeFalse();
        captures.Added.Should().BeEmpty();
        notifications.Last.Should().Be(new RecordingNotification("Recording failed", "disk full", NotificationKind.Error));
    }

    private static RecordingController CreateController(
        string root,
        FakeRecordingEngine engine,
        RecordingNotifications notifications,
        RecordingCaptureRepository? captures = null)
        => new(
            engine,
            new RecordingSettingsService(),
            notifications,
            new RecordingMonitorService(),
            new NoopRegionSelectionService(),
            new RecordingStoragePaths(root),
            captures ?? new RecordingCaptureRepository(),
            new NoopActionRepository(),
            new NoopShelfService(),
            new Octadock.App.Tests.Fakes.AllowAllLicenseGate(),
            NullLogger<RecordingController>.Instance);

    private sealed class FakeRecordingEngine : IRecordingEngine
    {
        public bool IsSupported { get; init; } = true;

        public RecordingState State { get; private set; } = RecordingState.Idle;

        public RecordingOptions? LastOptions { get; private set; }

        public Exception? StartException { get; init; }

        public Exception? StopException { get; set; }

        public event EventHandler<RecordingProgress>? ProgressChanged;

        public Task StartAsync(RecordingOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(options.OutputPath)!);
            File.WriteAllBytes(options.OutputPath, [0, 0, 0, 24]);

            if (StartException is not null)
            {
                State = RecordingState.Failed;
                throw StartException;
            }

            State = RecordingState.Recording;
            ProgressChanged?.Invoke(this, new RecordingProgress(RecordingState.Recording, 0));
            return Task.CompletedTask;
        }

        public Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
        {
            if (StopException is not null)
            {
                State = RecordingState.Failed;
                throw StopException;
            }

            State = RecordingState.Completed;
            return Task.FromResult(new RecordingResult
            {
                OutputPath = LastOptions?.OutputPath ?? throw new InvalidOperationException("No recording was started."),
                DurationMs = 1_250,
                FrameSize = new PixelSize(640, 360),
                FileSizeBytes = 4,
            });
        }

        public void Pause()
        {
            State = RecordingState.Paused;
        }

        public void Resume()
        {
            State = RecordingState.Recording;
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            State = RecordingState.Canceled;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSettingsService : ISettingsService
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

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly List<RecordingNotification> _items = new();

        public RecordingNotification? Last => _items.LastOrDefault();

        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
        {
            _items.Add(new RecordingNotification(title, message, kind));
        }
    }

    private sealed class RecordingMonitorService : IMonitorService
    {
        private static readonly DisplayInfo Monitor = new(
            MonitorId.Unknown,
            Index: 0,
            Bounds: new PixelRect(0, 0, 1920, 1080),
            WorkArea: new PixelRect(0, 0, 1920, 1040),
            DpiScale: 1.0,
            IsPrimary: true,
            DeviceName: "DISPLAY");

        public PixelRect VirtualDesktopBounds => Monitor.Bounds;

        public event EventHandler? MonitorsChanged;

        public IReadOnlyList<DisplayInfo> GetMonitors() => [Monitor];

        public DisplayInfo GetPrimary() => Monitor;

        public DisplayInfo GetActiveMonitor() => Monitor;

        public DisplayInfo GetMonitorFromPoint(PixelPoint point) => Monitor;

        public DisplayInfo? FindById(MonitorId id) => Monitor;

        public DisplayInfo? Resolve(string? monitorToken) => Monitor;

        public void RaiseMonitorsChangedForTest() => MonitorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NoopRegionSelectionService : IRegionSelectionService
    {
        public Task<RegionSelection> SelectAreaAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RegionSelection.Cancelled);

        public Task<RegionSelection> SelectWindowAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RegionSelection.Cancelled);

        public void HideAll()
        {
        }
    }

    private sealed class RecordingStoragePaths(string root) : IStoragePaths
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

    private sealed class RecordingCaptureRepository : ICaptureRepository
    {
        public List<CaptureRecord> Added { get; } = new();

        public Task AddAsync(CaptureRecord record, CancellationToken cancellationToken = default)
        {
            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<CaptureRecord?>(null);

        public Task<IReadOnlyList<CaptureRecord>> QueryAsync(
            CaptureFilter filter,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CaptureRecord>>([]);

        public Task<int> CountAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(
            int count,
            CancellationToken cancellationToken = default)
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

        public Task<IReadOnlyList<ActionRecord>> GetForCaptureAsync(
            Guid captureId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ActionRecord>>([]);
    }

    private sealed class NoopShelfService : IShelfService
    {
        public Task<bool> ShowAsync(CaptureRecord record, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> RestoreRecentlyClosedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void CloseAll()
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

    private sealed record RecordingNotification(string Title, string Message, NotificationKind Kind);
}

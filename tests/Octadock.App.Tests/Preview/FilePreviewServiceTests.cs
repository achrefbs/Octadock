using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Context;
using Octadock.App.Imaging;
using Octadock.App.Pins;
using Octadock.App.Preview;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Context;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Io;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.App.Tests.Preview;

public sealed class FilePreviewServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "octadock-preview-app-" + Guid.NewGuid().ToString("N"));

    public FilePreviewServiceTests() => Directory.CreateDirectory(_root);

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

    [Theory]
    [InlineData(".png", "PNG files (*.png)|*.png|All files (*.*)|*.*")]
    [InlineData("csv", "CSV files (*.csv)|*.csv|All files (*.*)|*.*")]
    public void BuildSaveFilter_uses_source_extension(string extension, string expected)
        => FilePreviewService.BuildSaveFilter(extension).Should().Be(expected);

    [Fact]
    public void BuildSaveFilter_without_extension_allows_all_files()
        => FilePreviewService.BuildSaveFilter(string.Empty).Should().Be("All files (*.*)|*.*");

    [Theory]
    [InlineData("site.url", true)]
    [InlineData("deployment.application", true)]
    [InlineData("legacy.scf", true)]
    [InlineData("site.url.", true)]
    [InlineData("site.url.txt", false)]
    [InlineData("package.appx", true)]
    [InlineData("package.appxbundle", true)]
    [InlineData("package.msix", true)]
    [InlineData("package.msixbundle", true)]
    [InlineData("remote.appinstaller", true)]
    [InlineData("desktop.theme", true)]
    [InlineData("desktop.themepack", true)]
    [InlineData("desktop.deskthemepack", true)]
    public void External_open_warning_matches_the_guard_used_by_default_app_launch(string path, bool expected)
        => FilePreviewService.RequiresExternalOpenWarning(path).Should().Be(expected);

    [Theory]
    [InlineData(".png")]
    [InlineData("JPG")]
    [InlineData(".jfif")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    [InlineData(".heic")]
    [InlineData(".avif")]
    public void ImageFileSupport_accepts_previewable_raster_images(string extension)
        => ImageFileSupport.IsSupportedRasterExtension(extension).Should().BeTrue();

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData("")]
    public void ImageFileSupport_rejects_non_image_extensions(string extension)
        => ImageFileSupport.IsSupportedRasterExtension(extension).Should().BeFalse();

    [Fact]
    public async Task External_preview_consults_the_file_preview_gate_before_path_access()
    {
        var gate = new RecordingLicenseGate { Allowed = false };
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService([], gate, host: host);

        bool shown = await service.PreviewAsync(Path.Combine(_root, "missing.txt"));

        shown.Should().BeFalse();
        gate.Requests.Should().Equal(GatedFeature.FilePreview);
        host.Presented.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_library_preview_bypasses_paywall_and_presents_recoverable_not_found()
    {
        var gate = new RecordingLicenseGate { Allowed = false };
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService([new TextPreviewProvider()], gate, host: host);

        bool shown = await service.PreviewExistingAsync(Path.Combine(_root, "missing.txt"));

        shown.Should().BeTrue();
        gate.Requests.Should().BeEmpty();
        host.Presented[0].Kind.Should().Be(FilePreviewKind.Loading);
        host.Presented[^1].Failure!.Kind.Should().Be(FilePreviewFailureKind.NotFound);
        host.Presented[^1].Failure!.CanLocate.Should().BeTrue();
    }

    [Fact]
    public async Task Pre_cancelled_preview_does_not_consult_the_license_gate_or_replace_a_card()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var gate = new RecordingLicenseGate();
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService([], gate, host: host);

        Func<Task> act = () => service.PreviewAsync(@"\\server\share\never-touch.txt", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        gate.Requests.Should().BeEmpty();
        host.Presented.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_shell_is_immediate_and_latest_request_wins()
    {
        string slowPath = WriteText("slow.txt", "slow source");
        string fastPath = WriteText("fast.txt", "fast source");
        var provider = new ControllableTextProvider();
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService([provider], new RecordingLicenseGate(), host: host);

        Task<bool> slow = service.PreviewExistingAsync(slowPath);
        await provider.SlowStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Presented[^1].Kind.Should().Be(FilePreviewKind.Loading);
        host.Presented[^1].FilePath.Should().Be(slowPath);

        bool fastShown = await service.PreviewExistingAsync(fastPath);
        provider.ReleaseSlow.TrySetResult();
        bool slowShown = await slow;

        fastShown.Should().BeTrue();
        slowShown.Should().BeFalse();
        host.Presented[^1].SourceContent.Should().Be("fast result");
        host.Presented.Should().NotContain(result => result.SourceContent == "slow result");
    }

    [Fact]
    public async Task User_cancel_leaves_a_stable_cancelled_card_with_retry()
    {
        string path = WriteText("cancel.txt", "source");
        var provider = new CancellationAwareProvider();
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService([provider], new RecordingLicenseGate(), host: host);

        Task<bool> preview = service.PreviewExistingAsync(path);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        host.Actions.Should().NotBeNull();
        host.Actions!.Cancel();

        (await preview).Should().BeTrue();
        host.Presented[^1].Failure!.Kind.Should().Be(FilePreviewFailureKind.Cancelled);
        host.Presented[^1].Failure!.CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task Raster_images_validate_then_open_the_modern_floating_image_viewer()
    {
        string path = WriteBytes("valid.png", [1]);
        var provider = new RecordingImageProvider();
        var pins = new RecordingPins();
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService(
            [provider],
            new RecordingLicenseGate(),
            pins: pins,
            host: host);

        bool shown = await service.PreviewExistingAsync(path);

        shown.Should().BeTrue();
        provider.LoadCalls.Should().Be(1);
        pins.ImagePaths.Should().Equal(path);
        host.DismissCalls.Should().Be(1);
    }

    [Fact]
    public async Task Image_moved_after_validation_preserves_a_locate_recovery_card()
    {
        string path = WriteBytes("moved-after-validation.png", [1]);
        var pins = new RecordingPins { ViewFailure = new FileNotFoundException() };
        var host = new RecordingPreviewCardHost();
        FilePreviewService service = CreateService(
            [new RecordingImageProvider()],
            new RecordingLicenseGate(),
            pins: pins,
            host: host);

        bool shown = await service.PreviewExistingAsync(path);

        shown.Should().BeTrue();
        host.DismissCalls.Should().Be(0);
        host.Presented[^1].Failure!.Kind.Should().Be(FilePreviewFailureKind.NotFound);
        host.Presented[^1].Failure!.CanLocate.Should().BeTrue();
    }

    [Fact]
    public async Task Image_provider_rejects_oversized_compressed_input_before_ui_decode()
    {
        string path = Path.Combine(_root, "huge.png");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(FrameImaging.MaxCompressedFileBytes + 1);
        }

        FilePreviewResult result = await new ImagePreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.TooLarge);
        result.SourceByteLength.Should().Be(FrameImaging.MaxCompressedFileBytes + 1);
    }

    [Fact]
    public async Task Image_provider_reports_true_source_dimensions_above_display_decode_cap()
    {
        const int width = 2001;
        const int height = 1001;
        string path = Path.Combine(_root, "dimensions.png");
        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray8,
            palette: null,
            pixels: new byte[width * height],
            stride: width);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        FilePreviewResult result = await new ImagePreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Image);
        result.ImagePixelWidth.Should().Be(width);
        result.ImagePixelHeight.Should().Be(height);
    }

    [Fact]
    public async Task Image_provider_reports_empty_and_corrupt_files_once_as_structured_errors()
    {
        string empty = WriteBytes("empty.png", []);
        string corrupt = WriteBytes("corrupt.png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);

        FilePreviewResult emptyResult = await new ImagePreviewProvider().LoadAsync(
            empty,
            new FilePreviewOptions(),
            CancellationToken.None);
        FilePreviewResult corruptResult = await new ImagePreviewProvider().LoadAsync(
            corrupt,
            new FilePreviewOptions(),
            CancellationToken.None);

        emptyResult.Failure!.Kind.Should().Be(FilePreviewFailureKind.Empty);
        emptyResult.SourceByteLength.Should().Be(0);
        corruptResult.Failure!.Kind.Should().Be(FilePreviewFailureKind.Malformed);
    }

    [Fact]
    public async Task Corrupt_optional_codec_file_is_not_misreported_as_codec_unavailable()
    {
        string path = WriteBytes("corrupt.webp", [1, 2, 3, 4, 5, 6]);

        FilePreviewResult result = await new ImagePreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.Malformed);
    }

    [Fact]
    public void Wrapped_wic_component_not_found_is_reported_as_codec_unavailable()
    {
        var codecFailure = new WicComponentMissingException();
        var wrapped = new InvalidOperationException("WPF wrapper", codecFailure);

        ImagePreviewException result = FrameImaging.MapDecodeFailure("optional.webp", wrapped);

        result.FailureKind.Should().Be(FilePreviewFailureKind.CodecUnavailable);
        result.Message.Should().Be(FilePreviewFailureCopy.For(FilePreviewFailureKind.CodecUnavailable));
    }

    [Fact]
    public async Task Image_provider_reports_busy_while_a_writer_can_mutate_the_source()
    {
        string path = WritePng("writer-open.png");
        await using var writer = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);

        FilePreviewResult result = await new ImagePreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.Busy);
    }

    [Fact]
    public void Preview_image_memory_guard_accounts_for_high_bit_depth()
    {
        Action act = () => FrameImaging.ValidateDimensions(8192, 8192, bitsPerPixel: 128);

        act.Should().Throw<ImagePreviewException>()
            .Which.FailureKind.Should().Be(FilePreviewFailureKind.TooLarge);
    }

    [Fact]
    public void Preview_image_dimension_guard_rejects_excessive_dimensions()
    {
        Action act = () => FrameImaging.ValidateDimensions(FrameImaging.MaxPixelDimension + 1, 1);

        act.Should().Throw<ImagePreviewException>()
            .Which.FailureKind.Should().Be(FilePreviewFailureKind.TooLarge);
    }

    [Fact]
    public async Task Image_provider_honors_pre_cancel_before_touching_path()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => new ImagePreviewProvider().LoadAsync(
            @"\\server\share\never-touch.png",
            new FilePreviewOptions(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Preview_image_decode_rechecks_cancellation_before_creating_a_pin()
    {
        string path = WriteBytes("slow-image.png", [1]);
        var images = new BlockingImageLoadService();
        var service = new PinService(
            images,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<PinService>.Instance);
        using var cts = new CancellationTokenSource();

        Task viewing = service.ViewImageFileAsync(path, cts.Token);
        await images.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        images.Release.TrySetResult();

        Func<Task> act = () => viewing;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Preview_adds_to_the_explicit_active_context_and_tracks_destination_changes()
    {
        var repository = new InMemoryContextRepository();
        ContextService context = BuildContextService(repository);
        ContextPackage first = (await context.CreatePackageAsync("First"))!;
        ContextPackage second = (await context.CreatePackageAsync("Second"))!;
        var active = new ActiveContextState();
        active.SetActive(first);
        FilePreviewService service = CreateService(
            [new TextPreviewProvider()],
            new RecordingLicenseGate(),
            context: context,
            active: active);
        string firstPath = WriteText("first.txt", "first");
        string secondPath = WriteText("second.txt", "second");

        await service.AddToContextAsync(firstPath);
        active.SetActive(second);
        await service.AddToContextAsync(secondPath);

        ContextPackage storedFirst = (await repository.GetPackageAsync(first.Id))!;
        ContextPackage storedSecond = (await repository.GetPackageAsync(second.Id))!;
        storedFirst.Items.Should().ContainSingle();
        storedFirst.Items[0].ReferenceSourcePath.Should().Be(Path.GetFullPath(firstPath));
        storedSecond.Items.Should().ContainSingle();
        storedSecond.Items[0].ReferenceSourcePath.Should().Be(Path.GetFullPath(secondPath));
    }

    [Fact]
    public async Task Preview_with_no_active_context_opens_explicit_chooser_without_guessing()
    {
        var repository = new InMemoryContextRepository();
        ContextService context = BuildContextService(repository);
        _ = await context.CreatePackageAsync("First");
        _ = await context.CreatePackageAsync("Second");
        var presenter = new RecordingWindowPresenter();
        FilePreviewService service = CreateService(
            [new TextPreviewProvider()],
            new RecordingLicenseGate(),
            context: context,
            active: new ActiveContextState(),
            presenter: presenter);
        string path = WriteText("choose.txt", "choose");

        await service.AddToContextAsync(path);

        presenter.ContextShows.Should().Be(1);
        (await repository.GetPackagesAsync()).Should().HaveCount(2);
        (await repository.GetPackagesAsync()).SelectMany(package => package.Items).Should().BeEmpty();
    }

    private FilePreviewService CreateService(
        IEnumerable<IFilePreviewProvider> providers,
        ILicenseGate gate,
        INotificationService? notifications = null,
        IPinService? pins = null,
        RecordingPreviewCardHost? host = null,
        ContextService? context = null,
        ActiveContextState? active = null,
        RecordingWindowPresenter? presenter = null)
    {
        notifications ??= new RecordingNotifications();
        context ??= BuildContextService(new InMemoryContextRepository(), gate, notifications);
        return new FilePreviewService(
            providers,
            new NoopClipboard(),
            new NoopCaptureCoordinator(),
            pins ?? new RecordingPins(),
            notifications,
            gate,
            context,
            active ?? new ActiveContextState(),
            presenter ?? new RecordingWindowPresenter(),
            host ?? new RecordingPreviewCardHost(),
            NullLogger<FilePreviewService>.Instance);
    }

    private ContextService BuildContextService(
        InMemoryContextRepository repository,
        ILicenseGate? gate = null,
        INotificationService? notifications = null)
    {
        var paths = new StoragePaths(_root);
        paths.EnsureDirectories();
        var safeWriter = new SafeFileWriter(new FileRevisionStore(Path.Combine(_root, "revisions")));
        return new ContextService(
            repository,
            paths,
            safeWriter,
            gate ?? new RecordingLicenseGate(),
            notifications ?? new RecordingNotifications(),
            Octadock.Core.Common.SystemClock.Instance,
            NullLogger<ContextService>.Instance);
    }

    private string WriteText(string name, string content)
        => WriteBytes(name, System.Text.Encoding.UTF8.GetBytes(content));

    private string WriteBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WritePng(string name)
    {
        string path = Path.Combine(_root, name);
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels: new byte[] { 0x10, 0x20, 0x30, 0xFF },
            stride: 4);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return path;
    }

    private sealed class WicComponentMissingException : Exception
    {
        public WicComponentMissingException()
            : base("component missing")
            => HResult = unchecked((int)0x88982F50);
    }

    private sealed class ControllableTextProvider : IFilePreviewProvider
    {
        public TaskCompletionSource SlowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSlow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanPreview(string extension) => extension == ".txt";

        public async Task<FilePreviewResult> LoadAsync(
            string path,
            FilePreviewOptions options,
            CancellationToken cancellationToken)
        {
            if (Path.GetFileName(path).Equals("slow.txt", StringComparison.OrdinalIgnoreCase))
            {
                SlowStarted.TrySetResult();
                await ReleaseSlow.Task.ConfigureAwait(false); // deliberately ignores cancellation
                return Text(path, "slow result");
            }

            return Text(path, "fast result");
        }
    }

    private sealed class CancellationAwareProvider : IFilePreviewProvider
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanPreview(string extension) => extension == ".txt";

        public async Task<FilePreviewResult> LoadAsync(
            string path,
            FilePreviewOptions options,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return Text(path, "unreachable");
        }
    }

    private static FilePreviewResult Text(string path, string content)
        => new()
        {
            Kind = FilePreviewKind.PlainText,
            FilePath = path,
            SourceContent = content,
            SourceByteLength = content.Length,
        };

    private sealed class RecordingImageProvider : IFilePreviewProvider
    {
        public int LoadCalls { get; private set; }

        public bool CanPreview(string extension) => extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

        public Task<FilePreviewResult> LoadAsync(
            string path,
            FilePreviewOptions options,
            CancellationToken cancellationToken)
        {
            LoadCalls++;
            return Task.FromResult(new FilePreviewResult
            {
                Kind = FilePreviewKind.Image,
                FilePath = path,
                ImagePath = path,
                SourceByteLength = new FileInfo(path).Length,
            });
        }
    }

    private sealed class BlockingImageLoadService : IImageLoadService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BitmapSource LoadFromFile(string absolutePath)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            BitmapSource bitmap = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                palette: null,
                pixels: new byte[4],
                stride: 4);
            bitmap.Freeze();
            return bitmap;
        }

        public BitmapSource ToBitmapSource(Octadock.Core.Capture.CapturedFrame frame)
            => throw new NotSupportedException();

        public byte[] EncodePng(BitmapSource image) => throw new NotSupportedException();
    }

    private sealed class RecordingPreviewCardHost : IPreviewCardHost
    {
        private readonly object _gate = new();
        private readonly List<FilePreviewResult> _presented = [];

        public event EventHandler? Closed;

        public IReadOnlyList<FilePreviewResult> Presented
        {
            get
            {
                lock (_gate)
                {
                    return _presented.ToList();
                }
            }
        }

        public PreviewCardActions? Actions { get; private set; }

        public int DismissCalls { get; private set; }

        public void Present(FilePreviewResult result, PreviewCardActions actions)
        {
            lock (_gate)
            {
                Actions = actions;
                _presented.Add(result);
            }
        }

        public void Dismiss() => DismissCalls++;

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RecordingPins : IPinService
    {
        public List<string> ImagePaths { get; } = [];

        public Exception? ViewFailure { get; init; }

        public Task ViewCaptureAsync(CaptureRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ViewImageFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ImagePaths.Add(filePath);
            return ViewFailure is null ? Task.CompletedTask : Task.FromException(ViewFailure);
        }

        public Task PinCaptureAsync(CaptureRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PinImageFileAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PinFromClipboardAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestorePersistedPinsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void HideAll() { }

        public void CloseAll() { }
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(string Title, string Message, NotificationKind Kind)> Items { get; } = [];

        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
            => Items.Add((title, message, kind));
    }

    private sealed class RecordingLicenseGate : ILicenseGate
    {
        public bool Allowed { get; init; } = true;

        public List<GatedFeature> Requests { get; } = [];

        public LicenseState State { get; } = new(LicenseMode.Trial, null, null, false, false, "test");

        public bool AllowsFullUse => Allowed;

        public event EventHandler<LicenseState>? Refused { add { } remove { } }

        public bool Allow(GatedFeature feature)
        {
            Requests.Add(feature);
            return Allowed;
        }
    }

    private sealed class NoopClipboard : IClipboardService
    {
        public bool ContainsImage() => false;
        public void SetImage(EncodedImage image) { }
        public void SetImageFromFile(string filePath) { }
        public void SetFileDropList(IEnumerable<string> filePaths) { }
        public void SetText(string text) { }
        public string? TryGetText() => null;
        public EncodedImage? TryGetImage() => null;
    }

    private sealed class NoopCaptureCoordinator : ICaptureCoordinator
    {
        public Task CaptureAreaAsync(PostCaptureAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CapturePreviousAreaAsync(PostCaptureAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CaptureFullscreenAsync(PostCaptureAction action, string? monitorToken, bool allMonitors, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CaptureWindowAsync(PostCaptureAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CaptureScrollingAsync(PostCaptureAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddExternalFileAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingWindowPresenter : IWindowPresenter
    {
        public int ContextShows { get; private set; }

        public void ShowHistory() { }
        public void ShowClipboardHistory() { }
        public void ShowTextTools() { }
        public void ShowContext() => ContextShows++;
        public void ShowAiActions(OctadockCommand? launchCommand = null) { }
        public void ShowSettings(string? tab = null) { }
        public void ShowAllInOneHud(CaptureMode? mode = null, PixelRect? preloadedRegion = null, int? preloadedWidth = null, int? preloadedHeight = null) { }
        public Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class InMemoryContextRepository : IContextRepository
    {
        private readonly Dictionary<Guid, ContextPackage> _packages = [];

        public Task<ContextPackage> CreatePackageAsync(string name, DateTimeOffset now, CancellationToken ct = default)
        {
            var package = new ContextPackage { Id = Guid.NewGuid(), Name = name, CreatedAt = now };
            _packages[package.Id] = package;
            return Task.FromResult(package);
        }

        public Task RenamePackageAsync(Guid id, string name, DateTimeOffset now, CancellationToken ct = default)
        {
            if (_packages.TryGetValue(id, out ContextPackage? package))
            {
                _packages[id] = package with { Name = name };
            }

            return Task.CompletedTask;
        }

        public Task UpdatePackageNotesAsync(Guid id, string notes, DateTimeOffset now, CancellationToken ct = default)
        {
            if (_packages.TryGetValue(id, out ContextPackage? package))
            {
                _packages[id] = package with { Notes = notes };
            }

            return Task.CompletedTask;
        }

        public Task DeletePackageAsync(Guid id, CancellationToken ct = default)
        {
            _packages.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContextPackage>>(_packages.Values.ToList());

        public Task<ContextPackage?> GetPackageAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_packages.GetValueOrDefault(id));

        public Task AddItemAsync(Guid packageId, ContextItem item, CancellationToken ct = default)
        {
            ContextPackage package = _packages[packageId];
            _packages[packageId] = package with { Items = package.Items.Append(item).ToList() };
            return Task.CompletedTask;
        }

        public Task RemoveItemAsync(Guid itemId, CancellationToken ct = default)
        {
            foreach (Guid key in _packages.Keys.ToList())
            {
                ContextPackage package = _packages[key];
                _packages[key] = package with
                {
                    Items = package.Items.Where(item => item.Id != itemId).ToList(),
                };
            }

            return Task.CompletedTask;
        }

        public Task ReorderItemsAsync(
            Guid packageId,
            IReadOnlyList<Guid> orderedItemIds,
            DateTimeOffset now,
            CancellationToken ct = default)
        {
            ContextPackage package = _packages[packageId];
            Dictionary<Guid, ContextItem> items = package.Items.ToDictionary(item => item.Id);
            _packages[packageId] = package with
            {
                Items = orderedItemIds.Select(id => items[id]).ToList(),
            };
            return Task.CompletedTask;
        }
    }
}

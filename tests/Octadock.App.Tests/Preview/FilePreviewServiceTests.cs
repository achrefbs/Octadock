using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.App.Tests.Preview;

public sealed class FilePreviewServiceTests
{
    [Theory]
    [InlineData(".png", "PNG files (*.png)|*.png|All files (*.*)|*.*")]
    [InlineData("csv", "CSV files (*.csv)|*.csv|All files (*.*)|*.*")]
    public void BuildSaveFilter_uses_source_extension(string extension, string expected)
    {
        FilePreviewService.BuildSaveFilter(extension).Should().Be(expected);
    }

    [Fact]
    public void BuildSaveFilter_without_extension_allows_all_files()
    {
        FilePreviewService.BuildSaveFilter(string.Empty).Should().Be("All files (*.*)|*.*");
    }

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
    {
        FilePreviewService.RequiresExternalOpenWarning(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(".png")]
    [InlineData("JPG")]
    [InlineData(".jfif")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    [InlineData(".heic")]
    [InlineData(".avif")]
    public void ImageFileSupport_accepts_previewable_raster_images(string extension)
    {
        ImageFileSupport.IsSupportedRasterExtension(extension).Should().BeTrue();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData("")]
    public void ImageFileSupport_rejects_non_image_extensions(string extension)
    {
        ImageFileSupport.IsSupportedRasterExtension(extension).Should().BeFalse();
    }

    [Fact]
    public async Task External_preview_consults_the_file_preview_gate_before_path_access()
    {
        var gate = new RecordingLicenseGate { Allowed = false };
        var notifications = new RecordingNotifications();
        FilePreviewService service = CreateService([], gate, notifications);

        bool shown = await service.PreviewAsync(Path.Combine(Path.GetTempPath(), "missing-octadock-file.txt"));

        shown.Should().BeFalse();
        gate.Requests.Should().Equal(GatedFeature.FilePreview);
        notifications.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_library_preview_bypasses_paywall_but_still_validates_path()
    {
        var gate = new RecordingLicenseGate { Allowed = false };
        var notifications = new RecordingNotifications();
        FilePreviewService service = CreateService([], gate, notifications);

        bool shown = await service.PreviewExistingAsync(
            Path.Combine(Path.GetTempPath(), "missing-octadock-library-item.txt"));

        shown.Should().BeFalse();
        gate.Requests.Should().BeEmpty();
        notifications.Items.Should().ContainSingle(item => item.Message.Contains("could not be found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pre_cancelled_preview_does_not_consult_the_license_gate()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var gate = new RecordingLicenseGate();
        FilePreviewService service = CreateService([], gate, new RecordingNotifications());

        Func<Task> act = () => service.PreviewAsync(
            @"\\server\share\never-touch.txt",
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        gate.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Raster_images_validate_then_open_the_modern_floating_image_viewer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"octadock-preview-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, [1]);
        try
        {
            var provider = new RecordingImageProvider();
            var pins = new RecordingPins();
            FilePreviewService service = CreateService(
                [provider],
                new RecordingLicenseGate(),
                new RecordingNotifications(),
                pins);

            bool shown = await service.PreviewExistingAsync(path);

            shown.Should().BeTrue();
            provider.LoadCalls.Should().Be(1);
            pins.ImagePaths.Should().Equal(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Image_provider_rejects_oversized_compressed_input_before_ui_decode()
    {
        string path = Path.Combine(Path.GetTempPath(), $"octadock-huge-preview-{Guid.NewGuid():N}.png");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((64L * 1024 * 1024) + 1);
        }

        try
        {
            var provider = new ImagePreviewProvider();

            FilePreviewResult result = await provider.LoadAsync(
                path,
                new FilePreviewOptions(),
                CancellationToken.None);

            result.Kind.Should().Be(FilePreviewKind.Error);
            result.Error.Should().Contain("too large");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Image_provider_reports_true_source_dimensions_above_display_decode_cap()
    {
        const int width = 2001;
        const int height = 1001;
        string path = Path.Combine(Path.GetTempPath(), $"octadock-dimensions-{Guid.NewGuid():N}.png");
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

        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            var provider = new ImagePreviewProvider();
            FilePreviewResult result = await provider.LoadAsync(
                path,
                new FilePreviewOptions(),
                CancellationToken.None);

            result.Kind.Should().Be(FilePreviewKind.Image);
            result.ImagePixelWidth.Should().Be(width);
            result.ImagePixelHeight.Should().Be(height);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Image_provider_honors_pre_cancel_before_touching_path()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = new ImagePreviewProvider();

        Func<Task> act = () => provider.LoadAsync(
            @"\\server\share\never-touch.png",
            new FilePreviewOptions(),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static FilePreviewService CreateService(
        IEnumerable<IFilePreviewProvider> providers,
        ILicenseGate gate,
        INotificationService notifications,
        IPinService? pins = null)
        => new(
            providers,
            null!,
            null!,
            pins ?? new RecordingPins(),
            notifications,
            gate,
            NullLogger<FilePreviewService>.Instance);

    private sealed class RecordingImageProvider : IFilePreviewProvider
    {
        public int LoadCalls { get; private set; }

        public bool CanPreview(string extension)
            => extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

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
            });
        }
    }

    private sealed class RecordingPins : IPinService
    {
        public List<string> ImagePaths { get; } = [];

        public Task ViewCaptureAsync(Octadock.Core.Models.CaptureRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ViewImageFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ImagePaths.Add(filePath);
            return Task.CompletedTask;
        }

        public Task PinCaptureAsync(Octadock.Core.Models.CaptureRecord record, CancellationToken cancellationToken = default)
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

        public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info, Action? clickAction = null)
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
}

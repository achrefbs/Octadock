using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Ocr;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class OcrServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "Octadock-OcrTests", Guid.NewGuid().ToString("N"));

    public OcrServiceTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task Provider_failure_does_not_touch_clipboard_or_report_no_text()
    {
        string path = CreateFile("damaged.png", [1]);
        var provider = new FakeOcrProvider
        {
            RecognizeFile = (_, _) => throw new InvalidDataException("bad image"),
        };
        var clipboard = new FakeClipboard();
        var notifications = new RecordingNotifications();
        OcrService service = CreateService(provider, clipboard, notifications);

        string result = await service.ExtractFromFileAsync(path, OcrTextMode.Lines, null);

        result.Should().BeEmpty();
        clipboard.TextWrites.Should().Be(0);
        notifications.Items.Should().ContainSingle();
        notifications.Items[0].Title.Should().Be("OCR failed");
        notifications.Items[0].Message.Should().Contain("Nothing was copied");
    }

    [Fact]
    public async Task Missing_file_does_not_call_provider_or_touch_clipboard()
    {
        var provider = new FakeOcrProvider();
        var clipboard = new FakeClipboard();
        var notifications = new RecordingNotifications();
        OcrService service = CreateService(provider, clipboard, notifications);

        string result = await service.ExtractFromFileAsync(
            Path.Combine(_tempDirectory, "missing.png"),
            OcrTextMode.Lines,
            null);

        result.Should().BeEmpty();
        provider.FileCalls.Should().Be(0);
        clipboard.TextWrites.Should().Be(0);
        notifications.Items.Should().ContainSingle(item => item.Message.Contains("could not be found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_provider_decode()
    {
        string path = Path.Combine(_tempDirectory, "huge.png");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(OcrService.MaxOcrFileBytes + 1);
        }

        var provider = new FakeOcrProvider();
        var notifications = new RecordingNotifications();
        OcrService service = CreateService(provider, new FakeClipboard(), notifications);

        string result = await service.ExtractFileTextAsync(path, OcrTextMode.Lines, null);

        result.Should().BeEmpty();
        provider.FileCalls.Should().Be(0);
        notifications.Items.Should().ContainSingle(item => item.Message.Contains("64 MB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Network_path_is_rejected_before_any_provider_or_file_access()
    {
        var provider = new FakeOcrProvider();
        var notifications = new RecordingNotifications();
        OcrService service = CreateService(provider, new FakeClipboard(), notifications);

        string result = await service.ExtractFileTextAsync(
            @"\\server\share\private.png",
            OcrTextMode.Lines,
            null);

        result.Should().BeEmpty();
        provider.FileCalls.Should().Be(0);
        notifications.Items.Should().ContainSingle(item => item.Message.Contains("Network", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_after_provider_returns_prevents_clipboard_write()
    {
        string path = CreateFile("cancel.png", [1]);
        using var cts = new CancellationTokenSource();
        var provider = new FakeOcrProvider
        {
            RecognizeFile = (_, _) =>
            {
                cts.Cancel();
                return Task.FromResult(new OcrResult { Text = "must not be copied" });
            },
        };
        var clipboard = new FakeClipboard();
        OcrService service = CreateService(provider, clipboard, new RecordingNotifications());

        Func<Task> act = () => service.ExtractFromFileAsync(path, OcrTextMode.Lines, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        clipboard.TextWrites.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_after_region_recognition_prevents_clipboard_write()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakeOcrProvider
        {
            RecognizeFrame = (_, _) =>
            {
                cts.Cancel();
                return Task.FromResult(new OcrResult { Text = "must not be copied" });
            },
        };
        var clipboard = new FakeClipboard();
        OcrService service = CreateService(provider, clipboard, new RecordingNotifications());

        Func<Task> act = () => service.CaptureRegionTextAsync(
            new PixelRect(10, 10, 20, 20),
            OcrTextMode.Lines,
            null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        clipboard.TextWrites.Should().Be(0);
    }

    [Fact]
    public async Task License_refusal_does_not_emit_a_misleading_no_text_message()
    {
        var provider = new FakeOcrProvider();
        var clipboard = new FakeClipboard();
        var notifications = new RecordingNotifications();
        var license = new RecordingLicenseGate { Allowed = false };
        OcrService service = CreateService(provider, clipboard, notifications, license);

        await service.CaptureRegionTextAsync(
            new PixelRect(10, 10, 20, 20),
            OcrTextMode.Lines,
            null);

        license.Requests.Should().Equal(GatedFeature.Ocr);
        provider.FrameCalls.Should().Be(0);
        clipboard.TextWrites.Should().Be(0);
        notifications.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Pre_cancelled_request_has_no_gate_or_clipboard_side_effects()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var license = new RecordingLicenseGate();
        var clipboard = new FakeClipboard();
        OcrService service = CreateService(
            new FakeOcrProvider(),
            clipboard,
            new RecordingNotifications(),
            license);

        Func<Task> act = () => service.ExtractFromFileAsync(
            @"\\server\share\never-touch.png",
            OcrTextMode.Lines,
            null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        license.Requests.Should().BeEmpty();
        clipboard.TextWrites.Should().Be(0);
    }

    [Fact]
    public async Task Successful_copy_identifies_the_actual_local_provider()
    {
        string path = CreateFile("ok.png", [1]);
        var provider = new FakeOcrProvider
        {
            RecognizeFile = (_, _) => Task.FromResult(new OcrResult { Text = "hello" }),
        };
        var clipboard = new FakeClipboard();
        var notifications = new RecordingNotifications();
        OcrService service = CreateService(provider, clipboard, notifications);

        string result = await service.ExtractFromFileAsync(path, OcrTextMode.Lines, null);

        result.Should().Be("hello");
        clipboard.LastText.Should().Be("hello");
        notifications.Items.Should().ContainSingle(item =>
            item.Message.Contains("locally", StringComparison.Ordinal) &&
            item.Message.Contains("Windows OCR", StringComparison.Ordinal));
    }

    private static OcrService CreateService(
        FakeOcrProvider provider,
        FakeClipboard clipboard,
        RecordingNotifications notifications,
        RecordingLicenseGate? license = null)
    {
        var settings = new FakeSettings
        {
            Current = OctadockSettings.Defaults with { History = new HistorySettings { Enabled = false } },
        };
        var history = new OcrHistoryRecorder(
            null!,
            null!,
            null!,
            null!,
            null!,
            settings,
            NullLogger<OcrHistoryRecorder>.Instance);

        return new OcrService(
            new FakeProviderFactory(provider),
            new FakeCaptureEngine(),
            new FakeMonitorService(),
            clipboard,
            notifications,
            settings,
            new CaptureGate(),
            license ?? new RecordingLicenseGate(),
            new EmptyServiceProvider(),
            history,
            NullLogger<OcrService>.Instance);
    }

    private string CreateFile(string name, byte[] bytes)
    {
        string path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FakeProviderFactory(FakeOcrProvider provider) : IOcrProviderFactory
    {
        public IOcrProvider? Resolve(OcrProvider preferred) => provider;

        public IReadOnlyList<(OcrProvider Provider, OcrAvailability Availability)> Describe()
            => [(provider.Kind, OcrAvailability.Available)];
    }

    private sealed class FakeOcrProvider : IOcrProvider
    {
        public Func<string, CancellationToken, Task<OcrResult>> RecognizeFile { get; init; }
            = (_, _) => Task.FromResult(OcrResult.Empty);

        public Func<CapturedFrame, CancellationToken, Task<OcrResult>> RecognizeFrame { get; init; }
            = (_, _) => Task.FromResult(OcrResult.Empty);

        public int FileCalls { get; private set; }

        public int FrameCalls { get; private set; }

        public OcrProvider Kind => OcrProvider.WindowsMediaOcr;

        public OcrAvailability CheckAvailability() => OcrAvailability.Available;

        public Task<OcrResult> RecognizeAsync(
            CapturedFrame frame,
            OcrTextMode mode,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            FrameCalls++;
            return RecognizeFrame(frame, cancellationToken);
        }

        public Task<OcrResult> RecognizeAsync(
            string imagePath,
            OcrTextMode mode,
            string? language = null,
            CancellationToken cancellationToken = default)
        {
            FileCalls++;
            return RecognizeFile(imagePath, cancellationToken);
        }
    }

    private sealed class FakeCaptureEngine : ICaptureEngine
    {
        private static readonly CapturedFrame Frame = new(
            new byte[4 * 20 * 20],
            20,
            20,
            20 * 4,
            FramePixelFormat.Bgra32,
            1,
            MonitorId.Unknown,
            CaptureSource.Empty,
            DateTimeOffset.UtcNow);

        public Task<CapturedFrame> CaptureAreaAsync(AreaCaptureRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Frame);

        public Task<CapturedFrame> CaptureWindowAsync(WindowCaptureRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Frame);

        public Task<CapturedFrame> CaptureFullscreenAsync(FullscreenCaptureRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Frame);
    }

    private sealed class FakeMonitorService : IMonitorService
    {
        private static readonly DisplayInfo Display = new(
            MonitorId.Unknown,
            0,
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            1,
            true,
            "Test");

        public IReadOnlyList<DisplayInfo> GetMonitors() => [Display];

        public DisplayInfo GetPrimary() => Display;

        public DisplayInfo GetActiveMonitor() => Display;

        public DisplayInfo GetMonitorFromPoint(PixelPoint point) => Display;

        public DisplayInfo? FindById(MonitorId id) => Display;

        public DisplayInfo? Resolve(string? monitorToken) => Display;

        public PixelRect VirtualDesktopBounds => Display.Bounds;

        public event EventHandler? MonitorsChanged { add { } remove { } }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public int TextWrites { get; private set; }

        public string? LastText { get; private set; }

        public bool ContainsImage() => false;

        public void SetImage(EncodedImage image) { }

        public void SetImageFromFile(string filePath) { }

        public void SetFileDropList(IEnumerable<string> filePaths) { }

        public void SetText(string text)
        {
            TextWrites++;
            LastText = text;
        }

        public string? TryGetText() => LastText;

        public EncodedImage? TryGetImage() => null;
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

    private sealed class FakeSettings : ISettingsService
    {
        public required OctadockSettings Current { get; set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<OctadockSettings, OctadockSettings> mutate, CancellationToken cancellationToken = default)
            => SaveAsync(mutate(Current), cancellationToken);

        public event EventHandler<SettingsChangedEventArgs>? Changed { add { } remove { } }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

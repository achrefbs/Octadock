using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.CaptureUx;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

/// <summary>
/// The settings → overlay seam: RegionSelectionService.CurrentBehavior() projects the
/// capture settings (precision aids, fixed size, locked aspect) onto the behavior the
/// selection overlays consume, and the overlay geometry helpers stay pure.
/// </summary>
public sealed class RegionSelectionBehaviorTests
{
    [Fact]
    public void Default_settings_show_precision_aids_without_size_or_ratio_constraints()
    {
        var service = BuildService(OctadockSettings.Defaults);

        SelectionOverlayBehavior behavior = service.CurrentBehavior();

        behavior.PrecisionAids.Should().BeTrue("precision aids default ON");
        behavior.HasFixedSize.Should().BeFalse();
        behavior.LockedAspectRatio.Should().BeNull();
        SelectionOverlayBehavior.Default.Should().Be(behavior);
    }

    [Fact]
    public void Precision_aids_toggle_flows_to_the_overlay_behavior()
    {
        var service = BuildService(WithCapture(c => c with { PrecisionAids = false }));

        service.CurrentBehavior().PrecisionAids.Should().BeFalse();
    }

    [Fact]
    public void Fixed_size_flows_only_when_enabled_with_a_positive_size()
    {
        var enabled = BuildService(WithCapture(c => c with
        {
            FixedSizeEnabled = true,
            FixedWidth = 1280,
            FixedHeight = 720,
        }));
        enabled.CurrentBehavior().Should().Be(new SelectionOverlayBehavior(true, 1280, 720, null));
        enabled.CurrentBehavior().HasFixedSize.Should().BeTrue();

        var disabled = BuildService(WithCapture(c => c with { FixedWidth = 1280, FixedHeight = 720 }));
        disabled.CurrentBehavior().HasFixedSize.Should().BeFalse("the size alone does not constrain anything");
    }

    [Fact]
    public void Locked_aspect_uses_the_last_confirmed_region_ratio()
    {
        var service = BuildService(WithCapture(c => c with { LockAspectRatio = true }));

        service.CurrentBehavior().LockedAspectRatio.Should().BeNull("no selection has been confirmed yet");

        service.TrackConfirmedRegion(new PixelRect(10, 10, 200, 100));

        service.CurrentBehavior().LockedAspectRatio.Should().Be(2.0);
    }

    [Fact]
    public void Locked_aspect_stays_off_when_the_setting_is_off()
    {
        var service = BuildService(OctadockSettings.Defaults);
        service.TrackConfirmedRegion(new PixelRect(10, 10, 200, 100));

        service.CurrentBehavior().LockedAspectRatio.Should().BeNull();
    }

    [Theory]
    [InlineData(100, 100, 200, 150, 100, 50)]   // drag right-down: width drives, height follows
    [InlineData(100, 100, 0, 150, 100, 50)]     // drag left-down: the rectangle grows into the left quadrant
    [InlineData(100, 100, 100, 50, 0, 0)]       // zero-width drag collapses to zero height
    public void ConstrainToAspectRatio_keeps_the_locked_ratio_in_every_quadrant(
        int anchorX,
        int anchorY,
        int cursorX,
        int cursorY,
        int expectedWidth,
        int expectedHeight)
    {
        System.Windows.Rect r = SelectionOverlayWindow.ConstrainToAspectRatio(
            new System.Windows.Point(anchorX, anchorY),
            new System.Windows.Point(cursorX, cursorY),
            ratio: 2.0);

        r.Width.Should().BeApproximately(expectedWidth, 0.001);
        r.Height.Should().BeApproximately(expectedHeight, 0.001);
    }

    [Fact]
    public void CreateFixedSizeRect_centers_on_the_press_point()
    {
        System.Windows.Rect r = SelectionOverlayWindow.CreateFixedSizeRect(
            new System.Windows.Point(500, 400), 200, 100, 1920, 1080);

        r.Should().Be(new System.Windows.Rect(400, 350, 200, 100));
    }

    [Fact]
    public void CreateFixedSizeRect_clamps_into_the_monitor_surface()
    {
        System.Windows.Rect nearEdge = SelectionOverlayWindow.CreateFixedSizeRect(
            new System.Windows.Point(5, 5), 200, 100, 1920, 1080);
        nearEdge.X.Should().Be(0);
        nearEdge.Y.Should().Be(0);
        nearEdge.Width.Should().Be(200);

        System.Windows.Rect oversized = SelectionOverlayWindow.CreateFixedSizeRect(
            new System.Windows.Point(500, 500), 4000, 100, 1920, 1080);
        oversized.Width.Should().Be(1920, "a fixed size larger than the monitor shrinks to the surface");
    }

    private static OctadockSettings WithCapture(Func<CaptureSettings, CaptureSettings> mutate)
        => OctadockSettings.Defaults with { Capture = mutate(OctadockSettings.Defaults.Capture) };

    private static RegionSelectionService BuildService(OctadockSettings settings)
        => new(
            new ThrowingMonitorService(),
            new ThrowingCaptureEngine(),
            new ThrowingWindowPicker(),
            new ThrowingImageLoadService(),
            new FakeSettingsService { Current = settings },
            NullLogger<RegionSelectionService>.Instance);

    private sealed class ThrowingMonitorService : IMonitorService
    {
        public PixelRect VirtualDesktopBounds => throw new NotSupportedException();

        public event EventHandler? MonitorsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DisplayInfo> GetMonitors() => throw new NotSupportedException();

        public DisplayInfo GetPrimary() => throw new NotSupportedException();

        public DisplayInfo GetActiveMonitor() => throw new NotSupportedException();

        public DisplayInfo GetMonitorFromPoint(PixelPoint point) => throw new NotSupportedException();

        public DisplayInfo? FindById(MonitorId id) => throw new NotSupportedException();

        public DisplayInfo? Resolve(string? monitorToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingCaptureEngine : ICaptureEngine
    {
        public Task<CapturedFrame> CaptureAreaAsync(AreaCaptureRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CapturedFrame> CaptureWindowAsync(WindowCaptureRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<CapturedFrame> CaptureFullscreenAsync(FullscreenCaptureRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingWindowPicker : IWindowPicker
    {
        public IReadOnlyList<CandidateWindow> EnumerateWindows() => throw new NotSupportedException();

        public CandidateWindow? WindowAt(PixelPoint point) => throw new NotSupportedException();
    }

    private sealed class ThrowingImageLoadService : IImageLoadService
    {
        public System.Windows.Media.Imaging.BitmapSource LoadFromFile(string absolutePath)
            => throw new NotSupportedException();

        public System.Windows.Media.Imaging.BitmapSource ToBitmapSource(CapturedFrame frame)
            => throw new NotSupportedException();

        public byte[] EncodePng(System.Windows.Media.Imaging.BitmapSource image)
            => throw new NotSupportedException();
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public OctadockSettings Current { get; set; } = OctadockSettings.Defaults;

        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, new SettingsChangedEventArgs(settings));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<OctadockSettings, OctadockSettings> mutate, CancellationToken cancellationToken = default)
            => SaveAsync(mutate(Current), cancellationToken);
    }
}

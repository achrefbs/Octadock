using FluentAssertions;
using Octadock.App.Pins;
using Octadock.Core.Geometry;
using Xunit;

namespace Octadock.App.Tests.Pins;

public sealed class PinServiceTests
{
    [Fact]
    public void Initial_pin_placement_centers_a_natural_image_in_physical_work_area_at_high_dpi()
    {
        DisplayInfo monitor = new(
            MonitorId.Unknown,
            Index: 1,
            Bounds: new PixelRect(1920, 0, 1920, 1080),
            WorkArea: new PixelRect(1920, 0, 1920, 1040),
            DpiScale: 1.75,
            IsPrimary: false,
            DeviceName: "DISPLAY2");

        PinInitialPlacement placement = PinWindow.CalculateInitialPlacement(597, 255, monitor);

        placement.PhysicalBounds.Should().Be(new PixelRect(2553, 364, 653, 311));
        placement.WidthDip.Should().BeApproximately(373.14, 0.01);
        placement.HeightDip.Should().BeApproximately(177.71, 0.01);
    }

    [Fact]
    public void Initial_pin_placement_caps_a_large_image_and_centers_on_negative_monitor_coordinates()
    {
        DisplayInfo monitor = new(
            MonitorId.Unknown,
            Index: 1,
            Bounds: new PixelRect(-2560, 0, 2560, 1440),
            WorkArea: new PixelRect(-2560, 0, 2560, 1400),
            DpiScale: 1.5,
            IsPrimary: false,
            DeviceName: "DISPLAY-LEFT");

        PinInitialPlacement placement = PinWindow.CalculateInitialPlacement(5000, 3200, monitor);

        placement.PhysicalBounds.Left.Should().BeGreaterThanOrEqualTo(monitor.WorkArea.Left);
        placement.PhysicalBounds.Right.Should().BeLessThanOrEqualTo(monitor.WorkArea.Right);
        placement.PhysicalBounds.Top.Should().BeGreaterThanOrEqualTo(monitor.WorkArea.Top);
        placement.PhysicalBounds.Bottom.Should().BeLessThanOrEqualTo(monitor.WorkArea.Bottom);
        placement.PhysicalBounds.Center.X.Should().BeInRange(
            monitor.WorkArea.Center.X - 1,
            monitor.WorkArea.Center.X + 1);
        placement.PhysicalBounds.Center.Y.Should().BeInRange(
            monitor.WorkArea.Center.Y - 1,
            monitor.WorkArea.Center.Y + 1);
    }

    [Fact]
    public void ClampToVisible_keeps_pin_when_meaningful_slice_is_visible()
    {
        DisplayInfo monitor = Monitor(
            bounds: new PixelRect(0, 0, 1920, 1080),
            workArea: new PixelRect(0, 0, 1920, 1040));
        var bounds = new PixelRect(1880, 400, 240, 180);

        PixelRect clamped = PinService.ClampToVisible(bounds, [monitor], monitor);

        clamped.Should().Be(bounds);
    }

    [Fact]
    public void ClampToVisible_gathers_offscreen_pin_into_fallback_work_area()
    {
        DisplayInfo fallback = Monitor(
            bounds: new PixelRect(0, 0, 1920, 1080),
            workArea: new PixelRect(10, 20, 1900, 1040));
        var bounds = new PixelRect(5000, 100, 300, 200);

        PixelRect clamped = PinService.ClampToVisible(bounds, [fallback], fallback);

        clamped.Should().Be(new PixelRect(1610, 100, 300, 200));
    }

    private static DisplayInfo Monitor(PixelRect bounds, PixelRect workArea)
        => new(
            MonitorId.Unknown,
            Index: 0,
            Bounds: bounds,
            WorkArea: workArea,
            DpiScale: 1.0,
            IsPrimary: true,
            DeviceName: "DISPLAY");
}

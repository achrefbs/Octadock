using FluentAssertions;
using Octadock.App.Pins;
using Octadock.Core.Geometry;
using Xunit;

namespace Octadock.App.Tests.Pins;

public sealed class PinServiceTests
{
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

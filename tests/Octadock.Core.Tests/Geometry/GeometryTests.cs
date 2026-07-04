using FluentAssertions;
using Octadock.Core.Geometry;
using Xunit;

namespace Octadock.Core.Tests.Geometry;

public class PixelRectTests
{
    [Fact]
    public void FromCorners_normalizes_regardless_of_drag_direction()
    {
        var a = new PixelPoint(100, 200);
        var b = new PixelPoint(40, 60);

        PixelRect rect = PixelRect.FromCorners(a, b);

        rect.Should().Be(new PixelRect(40, 60, 60, 140));
        rect.Width.Should().BePositive();
        rect.Height.Should().BePositive();
    }

    [Fact]
    public void FromEdges_builds_expected_rect()
    {
        PixelRect rect = PixelRect.FromEdges(10, 20, 110, 220);
        rect.Should().Be(new PixelRect(10, 20, 100, 200));
        rect.Right.Should().Be(110);
        rect.Bottom.Should().Be(220);
    }

    [Fact]
    public void Normalized_produces_non_negative_dimensions()
    {
        var rect = new PixelRect(100, 100, -40, -60);

        PixelRect normalized = rect.Normalized();

        normalized.X.Should().Be(60);
        normalized.Y.Should().Be(40);
        normalized.Width.Should().Be(40);
        normalized.Height.Should().Be(60);
    }

    [Fact]
    public void Intersect_returns_overlap()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 100, 100);

        a.Intersect(b).Should().Be(new PixelRect(50, 50, 50, 50));
    }

    [Fact]
    public void Intersect_returns_empty_when_disjoint()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(100, 100, 10, 10);

        a.Intersect(b).Should().Be(PixelRect.Empty);
        a.IntersectsWith(b).Should().BeFalse();
    }

    [Fact]
    public void Union_returns_bounding_rectangle()
    {
        var a = new PixelRect(0, 0, 50, 50);
        var b = new PixelRect(100, 100, 50, 50);

        a.Union(b).Should().Be(new PixelRect(0, 0, 150, 150));
    }

    [Fact]
    public void Union_with_empty_returns_other()
    {
        var a = new PixelRect(10, 10, 20, 20);
        a.Union(PixelRect.Empty).Should().Be(a);
        PixelRect.Empty.Union(a).Should().Be(a);
    }

    [Fact]
    public void Contains_point_is_half_open()
    {
        var rect = new PixelRect(0, 0, 100, 100);

        rect.Contains(new PixelPoint(0, 0)).Should().BeTrue();
        rect.Contains(new PixelPoint(99, 99)).Should().BeTrue();
        rect.Contains(new PixelPoint(100, 100)).Should().BeFalse();
        rect.Contains(new PixelPoint(-1, 50)).Should().BeFalse();
    }

    [Fact]
    public void Contains_rect_detects_full_containment()
    {
        var outer = new PixelRect(0, 0, 100, 100);
        var inner = new PixelRect(10, 10, 50, 50);
        var overlapping = new PixelRect(50, 50, 100, 100);

        outer.Contains(inner).Should().BeTrue();
        outer.Contains(overlapping).Should().BeFalse();
    }

    [Fact]
    public void Inflate_grows_on_all_sides()
    {
        var rect = new PixelRect(10, 10, 20, 20);
        rect.Inflate(5, 5).Should().Be(new PixelRect(5, 5, 30, 30));
    }

    [Fact]
    public void Center_and_area_are_computed()
    {
        var rect = new PixelRect(0, 0, 100, 50);
        rect.Center.Should().Be(new PixelPoint(50, 25));
        rect.Area.Should().Be(5000);
    }

    [Fact]
    public void ClampTo_keeps_rect_inside_bounds()
    {
        var bounds = new PixelRect(0, 0, 100, 100);
        var rect = new PixelRect(50, 50, 100, 100);

        rect.ClampTo(bounds).Should().Be(new PixelRect(50, 50, 50, 50));
    }
}

public class DipRectTests
{
    [Theory]
    [InlineData(1.0, 100, 200, 300, 80)]
    [InlineData(1.5, 150, 300, 450, 120)]
    [InlineData(2.0, 200, 400, 600, 160)]
    public void ToPixels_scales_and_rounds(double scale, int x, int y, int w, int h)
    {
        var dip = new DipRect(100, 200, 300, 80);

        PixelRect px = dip.ToPixels(scale);

        px.Should().Be(new PixelRect(x, y, w, h));
    }

    [Fact]
    public void ToPixels_rounds_away_from_zero()
    {
        // 10.5 * 1.0 => 11 (AwayFromZero rounding).
        var dip = new DipRect(10.5, 20.5, 4.5, 4.5);
        PixelRect px = dip.ToPixels(1.0);

        px.X.Should().Be(11);
        px.Y.Should().Be(21);
        px.Width.Should().Be(5);
        px.Height.Should().Be(5);
    }

    [Fact]
    public void ToPixels_guards_non_positive_scale()
    {
        var dip = new DipRect(10, 10, 20, 20);
        dip.ToPixels(0).Should().Be(new PixelRect(10, 10, 20, 20));
        dip.ToPixels(-3).Should().Be(new PixelRect(10, 10, 20, 20));
    }

    [Fact]
    public void FromCorners_normalizes()
    {
        DipRect rect = DipRect.FromCorners(100, 100, 40, 30);
        rect.Should().Be(new DipRect(40, 30, 60, 70));
    }
}

public class DisplayInfoTests
{
    [Fact]
    public void ToPixels_scales_and_offsets_by_monitor_bounds()
    {
        var display = new DisplayInfo(
            new MonitorId("DISPLAY2"),
            1,
            Bounds: new PixelRect(1920, 0, 2560, 1440),
            WorkArea: new PixelRect(1920, 0, 2560, 1400),
            DpiScale: 1.5,
            IsPrimary: false,
            DeviceName: "Dell");

        // A 100x80 DIP rect at (10,20) relative to the monitor: scaled by 1.5 then
        // offset by the monitor origin (1920, 0).
        PixelRect px = display.ToPixels(new DipRect(10, 20, 100, 80));

        px.Should().Be(new PixelRect(1920 + 15, 0 + 30, 150, 120));
    }

    [Fact]
    public void Dpi_reflects_scale()
    {
        var display = new DisplayInfo(
            MonitorId.Unknown, 0, PixelRect.Empty, PixelRect.Empty, 2.0, true, "x");
        display.Dpi.Should().Be(192.0);
    }
}

public class MonitorIdTests
{
    [Fact]
    public void Blank_value_becomes_unknown()
    {
        new MonitorId("   ").Value.Should().Be("UNKNOWN");
        new MonitorId("").Value.Should().Be("UNKNOWN");
    }

    [Fact]
    public void Trims_and_implicitly_converts_to_string()
    {
        var id = new MonitorId("  \\\\.\\DISPLAY1 ");
        string s = id;
        s.Should().Be("\\\\.\\DISPLAY1");
    }
}

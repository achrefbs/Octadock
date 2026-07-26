using FluentAssertions;
using Octadock.App.CaptureUx;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

/// <summary>
/// Service-level regression coverage for the Shelf's fixed-edge-tab anchoring: the
/// position math that keeps the edge tab and the cards in the same configured corner
/// across collapse/expand, monitor changes, and awkward work areas.
/// </summary>
public sealed class ShelfWindowGeometryTests
{
    private static readonly PixelRect Work = new(X: 0, Y: 0, Width: 1920, Height: 1040);

    [Theory]
    [InlineData(ShelfAnchor.BottomLeft, 16, 1040 - 400 - 16)]
    [InlineData(ShelfAnchor.BottomRight, 1920 - 300 - 16, 1040 - 400 - 16)]
    [InlineData(ShelfAnchor.TopLeft, 16, 16)]
    [InlineData(ShelfAnchor.TopRight, 1920 - 300 - 16, 16)]
    public void Anchored_position_places_the_window_in_the_configured_corner(
        ShelfAnchor anchor,
        int expectedX,
        int expectedY)
    {
        PixelPoint position = ShelfWindow.CalculateAnchoredPosition(anchor, Work, 300, 400, 16);

        position.Should().Be(new PixelPoint(expectedX, expectedY));
    }

    [Fact]
    public void Anchored_position_respects_work_areas_with_negative_origins()
    {
        var leftMonitorWork = new PixelRect(X: -1920, Y: 0, Width: 1920, Height: 1080);

        PixelPoint position = ShelfWindow.CalculateAnchoredPosition(
            ShelfAnchor.BottomLeft, leftMonitorWork, 300, 400, 16);

        position.Should().Be(new PixelPoint(-1920 + 16, 1080 - 400 - 16));
    }

    [Fact]
    public void Anchored_position_clamps_an_oversized_window_inside_the_work_area()
    {
        var tinyWork = new PixelRect(X: 100, Y: 100, Width: 200, Height: 150);

        PixelPoint position = ShelfWindow.CalculateAnchoredPosition(
            ShelfAnchor.BottomRight, tinyWork, 500, 400, 16);

        position.X.Should().BeGreaterThanOrEqualTo(tinyWork.X);
        position.Y.Should().BeGreaterThanOrEqualTo(tinyWork.Y);
    }

    [Theory]
    [InlineData(10, 1000, ShelfAnchor.BottomLeft)]
    [InlineData(1900, 1000, ShelfAnchor.BottomRight)]
    [InlineData(10, 10, ShelfAnchor.TopLeft)]
    [InlineData(1900, 10, ShelfAnchor.TopRight)]
    public void Nearest_anchor_tracks_the_cursor_corner(int cursorX, int cursorY, ShelfAnchor expected)
        => ShelfWindow.FindNearestAnchor(new PixelPoint(cursorX, cursorY), Work).Should().Be(expected);

    [Fact]
    public void Clear_anchor_moves_away_from_the_cursor_but_the_tab_stays_on_screen()
    {
        ShelfAnchor clear = ShelfWindow.ChooseClearAnchor(
            ShelfAnchor.BottomLeft, new PixelPoint(10, 1030), Work);

        clear.Should().Be(ShelfAnchor.TopRight, "the tab relocates to the corner farthest from the cursor");
        clear.Should().NotBe(ShelfAnchor.BottomLeft);
    }

    [Theory]
    [InlineData(0, 500, true)]
    [InlineData(2, 500, true)]
    [InlineData(3, 500, false)]
    [InlineData(40, 500, false)]
    [InlineData(0, -1, false)]
    [InlineData(0, 1080, false)]
    public void Left_edge_band_only_covers_the_displays_outer_extreme(int cursorX, int cursorY, bool expected)
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);

        ShelfWindow.CursorRestsOnOuterEdge(new PixelPoint(cursorX, cursorY), bounds, ShelfEdge.Left)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(1919, 500, true)]
    [InlineData(1917, 500, true)]
    [InlineData(1916, 500, false)]
    [InlineData(0, 500, false)]
    public void Right_edge_band_only_covers_the_displays_outer_extreme(int cursorX, int cursorY, bool expected)
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);

        ShelfWindow.CursorRestsOnOuterEdge(new PixelPoint(cursorX, cursorY), bounds, ShelfEdge.Right)
            .Should().Be(expected);
    }

    [Fact]
    public void Edge_is_shared_when_another_monitor_abuts_it_with_vertical_overlap()
    {
        DisplayInfo shelf = Monitor(1, new PixelRect(0, 0, 1920, 1080));
        DisplayInfo leftNeighbor = Monitor(2, new PixelRect(-1920, 200, 1920, 1080));

        ShelfWindow.EdgeIsShared(shelf, ShelfEdge.Left, new[] { shelf, leftNeighbor })
            .Should().BeTrue("the pointer there crosses into the other display");
        ShelfWindow.EdgeIsShared(shelf, ShelfEdge.Right, new[] { shelf, leftNeighbor })
            .Should().BeFalse("no monitor touches the right edge");
    }

    [Fact]
    public void Edge_is_not_shared_when_the_abutting_monitor_does_not_overlap_vertically()
    {
        DisplayInfo shelf = Monitor(1, new PixelRect(0, 0, 1920, 1080));
        DisplayInfo aboveLeft = Monitor(2, new PixelRect(-1920, -1080, 1920, 1080));

        ShelfWindow.EdgeIsShared(shelf, ShelfEdge.Left, new[] { shelf, aboveLeft })
            .Should().BeFalse("the neighbor sits above the shelf monitor, not beside it");
    }

    [Fact]
    public void Edge_is_not_shared_on_a_single_monitor_setup()
    {
        DisplayInfo shelf = Monitor(1, new PixelRect(0, 0, 1920, 1080));

        ShelfWindow.EdgeIsShared(shelf, ShelfEdge.Left, new[] { shelf }).Should().BeFalse();
        ShelfWindow.EdgeIsShared(shelf, ShelfEdge.Right, new[] { shelf }).Should().BeFalse();
    }

    private static DisplayInfo Monitor(int index, PixelRect bounds)
        => new(
            new MonitorId($"DISPLAY{index}"),
            index - 1,
            bounds,
            bounds,
            DpiScale: 1.0,
            IsPrimary: index == 1,
            $"DISPLAY{index}");
}

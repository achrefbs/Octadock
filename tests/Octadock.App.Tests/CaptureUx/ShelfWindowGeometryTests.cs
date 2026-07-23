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
}

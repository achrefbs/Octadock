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
    [InlineData(ShelfAnchor.BottomLeft, 1.0)]
    [InlineData(ShelfAnchor.BottomRight, 1.0)]
    [InlineData(ShelfAnchor.TopLeft, 1.5)]
    [InlineData(ShelfAnchor.TopRight, 2.0)]
    public void Expanded_and_collapsed_layouts_keep_the_tab_at_the_exact_same_screen_point(
        ShelfAnchor anchor,
        double dpiScale)
    {
        int tabWidth = (int)Math.Round(ShelfWindow.EdgeTabHitWidthDip * dpiScale);
        int tabHeight = (int)Math.Round(ShelfWindow.EdgeTabHitHeightDip * dpiScale);
        int margin = (int)Math.Round(16 * dpiScale);
        int expandedWidth = (int)Math.Round(236 * dpiScale);
        int expandedHeight = (int)Math.Round(420 * dpiScale);

        PixelPoint expandedWindow = ShelfWindow.CalculateWindowPositionForEdgeTab(
            anchor,
            Work,
            expandedWidth,
            expandedHeight,
            tabWidth,
            tabHeight,
            margin);
        PixelPoint collapsedWindow = ShelfWindow.CalculateWindowPositionForEdgeTab(
            anchor,
            Work,
            tabWidth,
            tabHeight,
            tabWidth,
            tabHeight,
            margin);

        PixelPoint expandedTab = TabScreenPosition(
            anchor,
            expandedWindow,
            expandedWidth,
            expandedHeight,
            tabWidth,
            tabHeight);

        expandedTab.Should().Be(collapsedWindow);
        collapsedWindow.Should().Be(ShelfWindow.CalculateAnchoredPosition(
            anchor,
            Work,
            tabWidth,
            tabHeight,
            margin));
    }

    [Fact]
    public void Edge_tab_has_a_compact_visual_inside_a_full_keyboard_and_pointer_target()
    {
        ShelfWindow.EdgeTabVisualWidthDip.Should().BeLessThanOrEqualTo(24);
        ShelfWindow.EdgeTabVisualHeightDip.Should().BeLessThanOrEqualTo(30);
        ShelfWindow.EdgeTabHitWidthDip.Should().BeGreaterThanOrEqualTo(32);
        ShelfWindow.EdgeTabHitHeightDip.Should().BeGreaterThanOrEqualTo(32);
    }

    [Fact]
    public void Tab_anchor_is_invariant_on_a_negative_origin_monitor_at_mixed_dpi()
    {
        var work = new PixelRect(X: -2560, Y: -160, Width: 2560, Height: 1440);
        const ShelfAnchor anchor = ShelfAnchor.BottomRight;
        const int tabWidth = 48;
        const int tabHeight = 48;
        const int margin = 24;
        PixelPoint expanded = ShelfWindow.CalculateWindowPositionForEdgeTab(
            anchor, work, 354, 900, tabWidth, tabHeight, margin);
        PixelPoint collapsed = ShelfWindow.CalculateWindowPositionForEdgeTab(
            anchor, work, tabWidth, tabHeight, tabWidth, tabHeight, margin);

        TabScreenPosition(anchor, expanded, 354, 900, tabWidth, tabHeight)
            .Should().Be(collapsed);
    }

    private static PixelPoint TabScreenPosition(
        ShelfAnchor anchor,
        PixelPoint window,
        int windowWidth,
        int windowHeight,
        int tabWidth,
        int tabHeight)
    {
        int x = anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft
            ? window.X
            : window.X + windowWidth - tabWidth;
        int y = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight
            ? window.Y
            : window.Y + windowHeight - tabHeight;
        return new PixelPoint(x, y);
    }
}

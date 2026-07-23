using FluentAssertions;
using Octadock.App.CaptureUx;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

public sealed class ShelfWindowDropTests
{
    [Fact]
    public void TryPickPreviewDropPath_returns_first_non_empty_path()
    {
        bool result = ShelfWindow.TryPickPreviewDropPath(
            ["", "  ", @"C:\repo\notes.md", @"C:\repo\other.txt"],
            out string path);

        result.Should().BeTrue();
        path.Should().Be(@"C:\repo\notes.md");
    }

    [Fact]
    public void TryPickPreviewDropPath_rejects_empty_payload()
    {
        ShelfWindow.TryPickPreviewDropPath(null, out _).Should().BeFalse();
        ShelfWindow.TryPickPreviewDropPath([], out _).Should().BeFalse();
        ShelfWindow.TryPickPreviewDropPath([" ", ""], out _).Should().BeFalse();
    }

    [Fact]
    public void TryPickDockImageDropPath_returns_first_supported_image()
    {
        bool result = ShelfWindow.TryPickDockImageDropPath(
            [@"C:\repo\notes.md", @"C:\repo\photo.tiff", @"C:\repo\other.png"],
            out string path);

        result.Should().BeTrue();
        path.Should().Be(@"C:\repo\photo.tiff");
    }

    [Fact]
    public void TryPickDockImageDropPath_rejects_non_images()
    {
        ShelfWindow.TryPickDockImageDropPath(null, out _).Should().BeFalse();
        ShelfWindow.TryPickDockImageDropPath([], out _).Should().BeFalse();
        ShelfWindow.TryPickDockImageDropPath([" ", @"C:\repo\notes.md"], out _).Should().BeFalse();
    }

    [Fact]
    public void Internal_capture_payload_round_trips_without_becoming_an_external_import()
    {
        Guid id = Guid.NewGuid();

        ShelfDragPayload.TryParseCaptureId(id.ToString("D"), out Guid parsed).Should().BeTrue();
        parsed.Should().Be(id);
        ShelfDragPayload.TryParseCaptureId("not-a-capture", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShelfAnchor.BottomLeft, -60, false)]
    [InlineData(ShelfAnchor.BottomLeft, 60, true)]
    [InlineData(ShelfAnchor.TopRight, 60, false)]
    [InlineData(ShelfAnchor.TopRight, -60, true)]
    public void Swipe_toward_the_screen_edge_is_inert_and_away_from_it_copies(
        ShelfAnchor anchor,
        double offset,
        bool expectedCopy)
    {
        (ShelfItemView.ResolveSwipeAction(anchor, offset) == ShelfSwipeAction.Copy)
            .Should().Be(expectedCopy);
    }

    [Fact]
    public void Clear_corner_is_the_corner_farthest_from_the_pointer()
    {
        var work = new PixelRect(0, 0, 1200, 800);

        ShelfWindow.ChooseClearAnchor(
            ShelfAnchor.BottomLeft,
            new PixelPoint(20, 780),
            work).Should().Be(ShelfAnchor.TopRight);

        ShelfWindow.FindNearestAnchor(new PixelPoint(1180, 30), work)
            .Should().Be(ShelfAnchor.TopRight);
    }

    [Theory]
    [InlineData(ShelfAnchor.BottomLeft)]
    [InlineData(ShelfAnchor.BottomRight)]
    [InlineData(ShelfAnchor.TopLeft)]
    [InlineData(ShelfAnchor.TopRight)]
    public void Expanded_and_collapsed_shelf_keep_the_same_configured_corner_on_screen(
        ShelfAnchor anchor)
    {
        var work = new PixelRect(-1920, -120, 1920, 1080);
        PixelPoint expanded = ShelfWindow.CalculateAnchoredPosition(
            anchor,
            work,
            widthPx: 312,
            heightPx: 420,
            marginPx: 18);
        PixelPoint collapsed = ShelfWindow.CalculateAnchoredPosition(
            anchor,
            work,
            widthPx: 48,
            heightPx: 60,
            marginPx: 18);

        bool left = anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft;
        bool top = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight;
        int expandedAnchorX = left ? expanded.X : expanded.X + 312;
        int collapsedAnchorX = left ? collapsed.X : collapsed.X + 48;
        int expandedAnchorY = top ? expanded.Y : expanded.Y + 420;
        int collapsedAnchorY = top ? collapsed.Y : collapsed.Y + 60;

        collapsedAnchorX.Should().Be(expandedAnchorX);
        collapsedAnchorY.Should().Be(expandedAnchorY);
    }
}

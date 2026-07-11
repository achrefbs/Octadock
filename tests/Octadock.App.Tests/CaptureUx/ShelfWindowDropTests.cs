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
    [InlineData(ShelfAnchor.BottomLeft, -60, true)]
    [InlineData(ShelfAnchor.BottomLeft, 60, false)]
    [InlineData(ShelfAnchor.TopRight, 60, true)]
    [InlineData(ShelfAnchor.TopRight, -60, false)]
    public void Swipe_action_is_relative_to_the_screen_edge(
        ShelfAnchor anchor,
        double offset,
        bool expectedDiscard)
    {
        (ShelfItemView.ResolveSwipeAction(anchor, offset) == ShelfSwipeAction.Discard)
            .Should().Be(expectedDiscard);
    }

    [Theory]
    [InlineData(ShelfAnchor.BottomLeft, -35, false)]
    [InlineData(ShelfAnchor.BottomLeft, -36, true)]
    [InlineData(ShelfAnchor.BottomLeft, 80, false)]
    [InlineData(ShelfAnchor.TopRight, 36, true)]
    [InlineData(ShelfAnchor.TopRight, -80, false)]
    public void Discard_commits_as_soon_as_the_edge_swipe_crosses_the_threshold(
        ShelfAnchor anchor,
        double offset,
        bool expected)
    {
        ShelfItemView.ShouldCommitImmediateDiscard(anchor, offset).Should().Be(expected);
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
}

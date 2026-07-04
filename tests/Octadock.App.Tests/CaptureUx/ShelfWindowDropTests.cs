using FluentAssertions;
using Octadock.App.CaptureUx;
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
}

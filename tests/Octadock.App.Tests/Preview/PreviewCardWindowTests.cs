using System.Windows;
using FluentAssertions;
using Octadock.App.Preview;
using Xunit;

namespace Octadock.App.Tests.Preview;

public sealed class PreviewCardWindowTests
{
    [Fact]
    public void CalculateFittedImageHostSize_uses_visible_viewport_minus_image_margin()
    {
        Size size = PreviewCardWindow.CalculateFittedImageHostSize(
            viewportWidth: 900,
            viewportHeight: 620,
            fallbackWidth: 1200,
            fallbackHeight: 800,
            margin: new Thickness(14, 10, 14, 12));

        size.Width.Should().Be(872);
        size.Height.Should().Be(598);
    }

    [Fact]
    public void CalculateFittedImageHostSize_falls_back_before_scroll_viewer_reports_viewport()
    {
        Size size = PreviewCardWindow.CalculateFittedImageHostSize(
            viewportWidth: 0,
            viewportHeight: double.NaN,
            fallbackWidth: 640,
            fallbackHeight: 480,
            margin: new Thickness(20));

        size.Width.Should().Be(600);
        size.Height.Should().Be(440);
    }
}

using FluentAssertions;
using Octadock.App.CaptureUx;
using Octadock.Core.Geometry;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

public sealed class HudStateTests
{
    [Fact]
    public void ApplyPreload_full_region_seeds_exact_fixed_capture_target()
    {
        var state = new HudState();
        var region = new PixelRect(100, 120, 800, 600);

        state.ApplyPreload(region, width: null, height: null);

        state.LastRegion.Should().Be(region);
        state.FixedWidth.Should().Be(800);
        state.FixedHeight.Should().Be(600);
        state.FixedSizeEnabled.Should().BeTrue();
        state.LastAspectRatio.Should().BeApproximately(4d / 3d, 0.0001d);
    }

    [Fact]
    public void ApplyPreload_size_only_seeds_fixed_size_without_changing_origin()
    {
        var state = new HudState
        {
            LastRegion = new PixelRect(10, 20, 300, 200),
        };

        state.ApplyPreload(region: null, width: 1200, height: 800);

        state.LastRegion.Should().Be(new PixelRect(10, 20, 300, 200));
        state.FixedWidth.Should().Be(1200);
        state.FixedHeight.Should().Be(800);
        state.FixedSizeEnabled.Should().BeTrue();
    }
}

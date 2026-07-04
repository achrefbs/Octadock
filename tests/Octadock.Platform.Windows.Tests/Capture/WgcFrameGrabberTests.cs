using FluentAssertions;
using Octadock.Platform.Windows.Capture;
using Octadock.Platform.Windows.Interop;
using Windows.Graphics;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Capture;

public sealed class WgcFrameGrabberTests
{
    [Fact]
    public void ResolveReadbackSize_UsesFrameContentSize()
    {
        var surface = new D3D11_TEXTURE2D_DESC { Width = 1920, Height = 1080 };
        var content = new SizeInt32 { Width = 1280, Height = 720 };
        var fallback = new SizeInt32 { Width = 1920, Height = 1080 };

        WgcFrameGrabber.ResolveReadbackSize(content, surface, fallback)
            .Should()
            .Be((1280, 720));
    }

    [Fact]
    public void ResolveReadbackSize_ClampsContentSizeToSurface()
    {
        var surface = new D3D11_TEXTURE2D_DESC { Width = 1024, Height = 768 };
        var content = new SizeInt32 { Width = 1200, Height = 900 };
        var fallback = new SizeInt32 { Width = 1200, Height = 900 };

        WgcFrameGrabber.ResolveReadbackSize(content, surface, fallback)
            .Should()
            .Be((1024, 768));
    }

    [Fact]
    public void ResolveReadbackSize_FallsBackWhenFrameContentSizeIsInvalid()
    {
        var surface = new D3D11_TEXTURE2D_DESC { Width = 1920, Height = 1080 };
        var content = new SizeInt32 { Width = 0, Height = -1 };
        var fallback = new SizeInt32 { Width = 1600, Height = 900 };

        WgcFrameGrabber.ResolveReadbackSize(content, surface, fallback)
            .Should()
            .Be((1600, 900));
    }
}

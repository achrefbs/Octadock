using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Platform.Windows.Recording;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Recording;

public sealed class ScrollingSessionTests
{
    [Fact]
    public void EstimateVerticalShift_detects_large_viewport_jump()
    {
        ScrollFrame previous = BuildFrame(width: 32, height: 900, sourceOffset: 0);
        ScrollFrame current = BuildFrame(width: 32, height: 900, sourceOffset: 700);

        int shift = ScrollingSession.EstimateVerticalShift(previous, current);

        shift.Should().Be(700);
    }

    [Fact]
    public void EstimateVerticalShift_ignores_sticky_header_when_matching()
    {
        ScrollFrame previous = BuildFrame(width: 32, height: 500, sourceOffset: 0, stickyHeaderRows: 80);
        ScrollFrame current = BuildFrame(width: 32, height: 500, sourceOffset: 120, stickyHeaderRows: 80);

        int shift = ScrollingSession.EstimateVerticalShift(previous, current);

        shift.Should().Be(120);
    }

    [Fact]
    public void AppendFrame_marks_session_truncated_at_configured_edge()
    {
        var region = new PixelRect(0, 0, 12, 20);
        var options = new ScrollingCaptureOptions
        {
            Direction = ScrollDirection.Vertical,
            MaxStitchedEdge = 25,
        };
        var session = new ScrollingSession(region, options, Monitor);

        session.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 0));
        session.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 10));

        session.Truncated.Should().BeTrue();
        session.StitchedHeight.Should().Be(25);
    }

    private static DisplayInfo Monitor { get; } = new(
        new MonitorId(@"\\.\DISPLAY1"),
        Index: 0,
        Bounds: new PixelRect(0, 0, 1920, 1080),
        WorkArea: new PixelRect(0, 0, 1920, 1040),
        DpiScale: 1.0,
        IsPrimary: true,
        DeviceName: @"\\.\DISPLAY1");

    private static ScrollFrame BuildFrame(int width, int height, int sourceOffset, int stickyHeaderRows = 0)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            int logicalRow = y < stickyHeaderRows ? y : sourceOffset + y;

            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 4);
                pixels[offset] = Pattern(logicalRow, x);
                pixels[offset + 1] = Pattern(logicalRow, x, 29);
                pixels[offset + 2] = Pattern(logicalRow, x, 43);
                pixels[offset + 3] = 255;
            }
        }

        return new ScrollFrame(pixels, width, height, stride);
    }

    private static byte Pattern(int row, int column)
    {
        int sampleGroup = (column / 4) % 4;
        return sampleGroup switch
        {
            0 => (byte)(row & 0xFF),
            1 => (byte)(((row / 256) * 83) & 0xFF),
            2 => (byte)((row * 37) & 0xFF),
            _ => (byte)((((row / 64) * 47) + column) & 0xFF),
        };
    }

    private static byte Pattern(int row, int column, int salt)
        => (byte)(((row * salt) + (column * 31)) % 251);
}

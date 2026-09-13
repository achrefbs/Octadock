using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Platform.Windows.Recording;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Recording;

public sealed class ScrollingSessionTests
{
    [Theory]
    [InlineData(140)]
    [InlineData(200)]
    [InlineData(270)]
    public void Stationary_header_cannot_hide_scrolling_in_the_content(int headerRows)
    {
        var previous = BuildFrame(64, 500, 0, headerRows);
        var current = BuildFrame(64, 500, 60, headerRows);
        var match = ScrollingSession.MatchVerticalShift(previous, current);
        match.Outcome.Should().Be(VerticalScrollMatchOutcome.DownwardMovement);
        match.Shift.Should().Be(60);
    }

    [Fact]
    public void EstimateVerticalShift_detects_large_viewport_jump()
    {
        ScrollFrame previous = BuildFrame(width: 32, height: 900, sourceOffset: 0);
        ScrollFrame current = BuildFrame(width: 32, height: 900, sourceOffset: 700);

        int shift = ScrollingSession.EstimateVerticalShift(previous, current);

        shift.Should().Be(700);
    }

    [Fact]
    public void EstimateSignedVerticalShift_detects_bottom_to_top_scroll()
    {
        ScrollFrame previous = BuildFrame(width: 32, height: 900, sourceOffset: 700);
        ScrollFrame current = BuildFrame(width: 32, height: 900, sourceOffset: 0);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, current);
        int signedShift = ScrollingSession.EstimateSignedVerticalShift(previous, current);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.UpwardMovement, $"match score was {match.Score}");
        match.Shift.Should().Be(-700);
        signedShift.Should().Be(-700);
    }

    [Fact]
    public void AppendFrame_prepends_new_top_rows_when_scrolling_up()
    {
        ScrollingSession session = CreateSession(width: 12, height: 20);

        session.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 100));
        session.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 90));

        session.StitchedHeight.Should().Be(30);
        AssertRowsMatchLogicalRange(session, expectedWidth: 12, expectedStartRow: 90, expectedHeight: 30);
    }

    [Fact]
    public void AppendFrame_does_not_duplicate_rows_when_direction_reverses_inside_captured_range()
    {
        ScrollingSession downThenUp = CreateSession(width: 12, height: 20);
        downThenUp.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 100));
        downThenUp.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 110));
        downThenUp.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 105));

        ScrollingSession upThenDown = CreateSession(width: 12, height: 20);
        upThenDown.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 100));
        upThenDown.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 90));
        upThenDown.AppendFrame(BuildFrame(width: 12, height: 20, sourceOffset: 95));

        downThenUp.StitchedHeight.Should().Be(30);
        AssertRowsMatchLogicalRange(downThenUp, expectedWidth: 12, expectedStartRow: 100, expectedHeight: 30);
        upThenDown.StitchedHeight.Should().Be(30);
        AssertRowsMatchLogicalRange(upThenDown, expectedWidth: 12, expectedStartRow: 90, expectedHeight: 30);
    }

    [Fact]
    public void EstimateVerticalShift_ignores_sticky_header_when_matching()
    {
        ScrollFrame previous = BuildFrame(width: 32, height: 500, sourceOffset: 0, stickyHeaderRows: 80);
        ScrollFrame current = BuildFrame(width: 32, height: 500, sourceOffset: 120, stickyHeaderRows: 80);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, current);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.DownwardMovement, $"match score was {match.Score}");
        match.Shift.Should().Be(120);
    }

    [Fact]
    public void Identical_viewports_are_no_movement_and_do_not_grow_the_stitch()
    {
        ScrollFrame previous = BuildFrame(width: 32, height: 180, sourceOffset: 25);
        ScrollFrame identicalCopy = previous with { Pixels = previous.Pixels.ToArray() };
        var session = CreateSession(width: 32, height: 180);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, identicalCopy);
        session.AppendFrame(previous);
        session.AppendFrame(identicalCopy);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.NoMovement);
        match.Shift.Should().Be(0);
        session.StitchedHeight.Should().Be(180);
        session.Truncated.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(255)]
    public void Uniform_or_blank_viewports_are_no_movement_and_never_fabricate_a_one_pixel_shift(int value)
    {
        ScrollFrame blank = BuildSolidFrame(width: 40, height: 160, value: (byte)value);
        ScrollFrame secondBlank = BuildSolidFrame(width: 40, height: 160, value: (byte)value);
        var session = CreateSession(width: 40, height: 160);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(blank, secondBlank);
        session.AppendFrame(blank);
        for (int i = 0; i < 20; i++)
        {
            session.AppendFrame(secondBlank);
        }

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.NoMovement);
        ScrollingSession.EstimateVerticalShift(blank, secondBlank).Should().Be(0);
        session.StitchedHeight.Should().Be(160);
    }

    [Fact]
    public void Uniform_repaint_with_a_different_color_is_no_movement_and_does_not_grow()
    {
        ScrollFrame previous = BuildSolidFrame(width: 40, height: 160, value: 245);
        ScrollFrame repaint = BuildSolidFrame(width: 40, height: 160, value: 250);
        var session = CreateSession(width: 40, height: 160);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, repaint);
        session.AppendFrame(previous);
        session.AppendFrame(repaint);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.NoMovement);
        session.StitchedHeight.Should().Be(160);
    }

    [Fact]
    public void Sparse_document_with_sticky_header_retains_confident_downward_match()
    {
        ScrollFrame previous = BuildSparseFrame(width: 64, height: 480, sourceOffset: 0, stickyHeaderRows: 48);
        ScrollFrame current = BuildSparseFrame(width: 64, height: 480, sourceOffset: 90, stickyHeaderRows: 48);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, current);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.DownwardMovement, $"match score was {match.Score}");
        match.Shift.Should().Be(90);
    }

    [Fact]
    public void Weak_unrelated_match_is_unmatched_and_skipped()
    {
        ScrollFrame previous = BuildFrame(width: 48, height: 180, sourceOffset: 0);
        ScrollFrame unrelated = BuildNoiseFrame(width: 48, height: 180, seed: 1729);
        var session = CreateSession(width: 48, height: 180);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, unrelated);
        session.AppendFrame(previous);
        session.AppendFrame(unrelated);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.Unmatched);
        match.Shift.Should().Be(0);
        session.StitchedHeight.Should().Be(180, "weak matches must not append guessed rows");

        session.AppendFrame(BuildFrame(width: 48, height: 180, sourceOffset: 40));
        session.StitchedHeight.Should().Be(220, "the last accepted anchor should survive a weak frame");
    }

    [Fact]
    public void Ambiguous_repeating_content_is_unmatched_instead_of_guessing_a_shift()
    {
        ScrollFrame previous = BuildRepeatingFrame(width: 40, height: 160, sourceOffset: 0);
        ScrollFrame current = BuildRepeatingFrame(width: 40, height: 160, sourceOffset: 4);

        VerticalScrollMatch match = ScrollingSession.MatchVerticalShift(previous, current);

        match.Outcome.Should().Be(VerticalScrollMatchOutcome.Unmatched);
        match.Shift.Should().Be(0);
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

    [Fact]
    public void Retained_byte_budget_caps_rows_before_flattening()
    {
        const int width = 12;
        const int height = 20;
        const long retainedBudget = 2000;
        var region = new PixelRect(0, 0, width, height);
        var options = new ScrollingCaptureOptions
        {
            Direction = ScrollDirection.Vertical,
            MaxStitchedEdge = 1000,
        };
        var session = new ScrollingSession(region, options, Monitor, retainedBudget);

        session.MaxStitchedEdge.Should().Be(25, "the byte budget is stricter than the dimension setting");
        session.AppendFrame(BuildFrame(width, height, sourceOffset: 0));
        session.AppendFrame(BuildFrame(width, height, sourceOffset: 10));

        session.Truncated.Should().BeTrue();
        session.StitchedHeight.Should().Be(25);
        session.RetainedPixelBytes.Should().Be(25L * width * 4);

        (byte[] pixels, _, int stitchedHeight, _) = session.BuildStitched();
        stitchedHeight.Should().Be(25);
        pixels.LongLength.Should().BeLessThanOrEqualTo(retainedBudget);
    }

    [Fact]
    public void Default_budget_keeps_4k_retained_plus_flattened_buffers_well_below_one_gigabyte()
    {
        const int width = 3840;
        const int height = 2160;
        var options = new ScrollingCaptureOptions
        {
            Direction = ScrollDirection.Vertical,
            MaxStitchedEdge = 32000,
        };
        var session = new ScrollingSession(new PixelRect(0, 0, width, height), options, Monitor);

        long worstCaseRetainedAndFlattened = 2L * session.MaxStitchedEdge * width * 4;

        session.MaxStitchedEdge.Should().BeLessThan(32000);
        worstCaseRetainedAndFlattened.Should().BeLessThan(512L * 1024 * 1024);
    }

    private static DisplayInfo Monitor { get; } = new(
        new MonitorId(@"\\.\DISPLAY1"),
        Index: 0,
        Bounds: new PixelRect(0, 0, 1920, 1080),
        WorkArea: new PixelRect(0, 0, 1920, 1040),
        DpiScale: 1.0,
        IsPrimary: true,
        DeviceName: @"\\.\DISPLAY1");

    private static void AssertRowsMatchLogicalRange(
        ScrollingSession session, int expectedWidth, int expectedStartRow, int expectedHeight)
    {
        (byte[] pixels, int width, int height, int stride) = session.BuildStitched();

        width.Should().Be(expectedWidth);
        height.Should().Be(expectedHeight);
        for (int y = 0; y < height; y++)
        {
            int logicalRow = expectedStartRow + y;
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 4);
                pixels[offset].Should().Be(Pattern(logicalRow, x));
                pixels[offset + 1].Should().Be(Pattern(logicalRow, x, 29));
                pixels[offset + 2].Should().Be(Pattern(logicalRow, x, 43));
                pixels[offset + 3].Should().Be(255);
            }
        }
    }

    private static ScrollingSession CreateSession(int width, int height)
    {
        var options = new ScrollingCaptureOptions
        {
            Direction = ScrollDirection.Vertical,
            MaxStitchedEdge = 32000,
        };
        return new ScrollingSession(new PixelRect(0, 0, width, height), options, Monitor);
    }

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

    private static ScrollFrame BuildSolidFrame(int width, int height, byte value)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }

        return new ScrollFrame(pixels, width, height, stride);
    }

    private static ScrollFrame BuildSparseFrame(
        int width,
        int height,
        int sourceOffset,
        int stickyHeaderRows)
    {
        ScrollFrame frame = BuildSolidFrame(width, height, value: 248);

        for (int y = 0; y < height; y++)
        {
            int logicalRow = y < stickyHeaderRows ? y : sourceOffset + y;
            bool header = y < stickyHeaderRows;
            bool textRow = logicalRow % 41 is 0 or 1 or 2;
            if (!header && !textRow)
            {
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int offset = (y * frame.Stride) + (x * 4);
                byte value = header
                    ? (byte)(30 + ((x + logicalRow) % 35))
                    : (byte)(20 + ((logicalRow * 7 + x * 3) % 80));
                frame.Pixels[offset] = value;
                frame.Pixels[offset + 1] = (byte)Math.Min(255, value + 9);
                frame.Pixels[offset + 2] = (byte)Math.Min(255, value + 17);
            }
        }

        return frame;
    }

    private static ScrollFrame BuildNoiseFrame(int width, int height, int seed)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        var random = new Random(seed);
        random.NextBytes(pixels);
        for (int offset = 3; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
        }

        return new ScrollFrame(pixels, width, height, stride);
    }

    private static ScrollFrame BuildRepeatingFrame(int width, int height, int sourceOffset)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];

        for (int y = 0; y < height; y++)
        {
            byte value = (byte)(((sourceOffset + y) % 8) * 30);
            for (int x = 0; x < width; x++)
            {
                int offset = (y * stride) + (x * 4);
                pixels[offset] = value;
                pixels[offset + 1] = (byte)(255 - value);
                pixels[offset + 2] = (byte)(value / 2);
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

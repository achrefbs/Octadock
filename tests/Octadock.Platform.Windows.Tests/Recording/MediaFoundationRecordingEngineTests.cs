using FluentAssertions;
using Octadock.Core.Geometry;
using Octadock.Core.Recording;
using Octadock.Platform.Windows.Recording;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Recording;

public sealed class MediaFoundationRecordingEngineTests
{
    [Fact]
    public void CalculateRgb32Stride_ReturnsPositiveTopDownStride()
    {
        MediaFoundationRecordingEngine.CalculateRgb32Stride(1920)
            .Should()
            .Be(7680);
    }

    [Fact]
    public void ElapsedToMediaFoundationTimestamp_UsesElapsedTimeTicks()
    {
        MediaFoundationRecordingEngine.ElapsedToMediaFoundationTimestamp(TimeSpan.FromMilliseconds(1500))
            .Should()
            .Be(15_000_000);
    }

    [Fact]
    public void ElapsedToMediaFoundationTimestamp_ClampsNegativeElapsedTime()
    {
        MediaFoundationRecordingEngine.ElapsedToMediaFoundationTimestamp(TimeSpan.FromTicks(-1))
            .Should()
            .Be(0);
    }

    [Fact]
    public void ResolveRecordingRegion_UsesExplicitRegionInsteadOfMonitorBounds()
    {
        var options = new RecordingOptions
        {
            OutputPath = "out.mp4",
            Region = new PixelRect(10, 20, 801, 603),
        };

        PixelRect region = MediaFoundationRecordingEngine.ResolveRecordingRegion(
            options,
            monitorBounds: new PixelRect(0, 0, 1920, 1080));

        region.Should().Be(new PixelRect(10, 20, 800, 602));
    }

    [Fact]
    public void ResolveRecordingRegion_FallsBackToMonitorBoundsWhenNoRegionIsProvided()
    {
        var options = new RecordingOptions { OutputPath = "out.mp4" };

        PixelRect region = MediaFoundationRecordingEngine.ResolveRecordingRegion(
            options,
            monitorBounds: new PixelRect(-1920, 0, 1920, 1080));

        region.Should().Be(new PixelRect(-1920, 0, 1920, 1080));
    }

    [Fact]
    public void ResolveRecordingRegion_NormalizesNegativeExtentsBeforeEncoding()
    {
        var options = new RecordingOptions
        {
            OutputPath = "out.mp4",
            Region = new PixelRect(20, 30, -101, -51),
        };

        PixelRect region = MediaFoundationRecordingEngine.ResolveRecordingRegion(
            options,
            monitorBounds: new PixelRect(0, 0, 1920, 1080));

        region.Should().Be(new PixelRect(-81, -21, 100, 50));
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(30, 33)]
    [InlineData(240, 4)]
    [InlineData(1000, 4)]
    public void CalculateFrameIntervalMs_ClampsFpsToSupportedRange(int fps, int expected)
    {
        MediaFoundationRecordingEngine.CalculateFrameIntervalMs(fps)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void ValidateCompletedRecording_RejectsAnMp4WithNoEncodedFrames()
    {
        Action act = () => MediaFoundationRecordingEngine.ValidateCompletedRecording(
            writtenFrameCount: 0,
            fileSizeBytes: 4_096);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*no video frames*");
    }

    [Fact]
    public void ValidateCompletedRecording_RejectsAnEmptyOutputFile()
    {
        Action act = () => MediaFoundationRecordingEngine.ValidateCompletedRecording(
            writtenFrameCount: 120,
            fileSizeBytes: 0);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*empty recording file*");
    }

    [Fact]
    public void ValidateCompletedRecording_AcceptsFramesAndNonEmptyOutput()
    {
        Action act = () => MediaFoundationRecordingEngine.ValidateCompletedRecording(
            writtenFrameCount: 120,
            fileSizeBytes: 1_024);

        act.Should().NotThrow();
    }
}

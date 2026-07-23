using FluentAssertions;
using Octadock.Platform.Windows.Recording;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Recording;

public sealed class RecordingEngineReliabilityTests
{
    [Fact]
    public void EnsureSufficientDiskSpace_throws_when_the_drive_is_under_pressure()
    {
        Action act = () => MediaFoundationRecordingEngine.EnsureSufficientDiskSpace(
            @"C:\recordings\out.mp4",
            _ => MediaFoundationRecordingEngine.MinimumFreeBytesToStart - 1);

        act.Should().Throw<IOException>()
            .WithMessage("*Not enough free disk space*");
    }

    [Fact]
    public void EnsureSufficientDiskSpace_passes_at_the_floor()
    {
        Action act = () => MediaFoundationRecordingEngine.EnsureSufficientDiskSpace(
            @"C:\recordings\out.mp4",
            _ => MediaFoundationRecordingEngine.MinimumFreeBytesToStart);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSufficientDiskSpace_never_blocks_on_an_unknown_probe()
    {
        Action act = () => MediaFoundationRecordingEngine.EnsureSufficientDiskSpace(
            @"C:\recordings\out.mp4",
            _ => -1);

        act.Should().NotThrow();
    }

    [Fact]
    public void ProbeAvailableFreeBytes_returns_unknown_for_an_invalid_path()
    {
        MediaFoundationRecordingEngine.ProbeAvailableFreeBytes("?\0\0invalid")
            .Should()
            .Be(-1);
    }

    [Fact]
    public void ProbeAvailableFreeBytes_reports_the_temp_drive()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "octadock-probe.mp4");

        MediaFoundationRecordingEngine.ProbeAvailableFreeBytes(tempFile)
            .Should()
            .BeGreaterThan(0);
    }

    [Fact]
    public void FloatToPcm16_converts_interleaved_float_to_little_endian_pcm()
    {
        float[] samples = [0f, 1f];
        var destination = new byte[4];

        int written = RecordingAudioFormat.FloatToPcm16(samples, frames: 1, destination);

        written.Should().Be(4);
        destination.Should().Equal(0x00, 0x00, 0xFF, 0x7F);
    }

    [Fact]
    public void FloatToPcm16_clamps_out_of_range_peaks_instead_of_wrapping()
    {
        float[] samples = [2f, -2f];
        var destination = new byte[4];

        RecordingAudioFormat.FloatToPcm16(samples, frames: 1, destination);

        destination.Should().Equal(0xFF, 0x7F, 0x01, 0x80);
    }

    [Fact]
    public void FloatToPcm16_rejects_a_destination_that_is_too_small()
    {
        float[] samples = [0f, 0f];
        var destination = new byte[2];

        Action act = () => RecordingAudioFormat.FloatToPcm16(samples, frames: 1, destination);

        act.Should().Throw<ArgumentException>();
    }
}

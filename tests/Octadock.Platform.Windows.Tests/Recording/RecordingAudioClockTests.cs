using FluentAssertions;
using Octadock.Platform.Windows.Recording;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Recording;

public sealed class RecordingAudioClockTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Stamp_advances_the_timeline_by_the_exact_frame_duration()
    {
        var clock = new RecordingAudioClock(SampleRate);

        long first = clock.Stamp(4_800); // 100 ms at 48 kHz.

        first.Should().Be(0);
        clock.NextTimestampHns.Should().Be(1_000_000);
    }

    [Fact]
    public void Stamp_returns_monotonic_timestamps_across_chunks()
    {
        var clock = new RecordingAudioClock(SampleRate);

        long first = clock.Stamp(1_920); // 40 ms.
        long second = clock.Stamp(1_920);

        first.Should().Be(0);
        second.Should().Be(400_000);
        clock.NextTimestampHns.Should().Be(800_000);
    }

    [Fact]
    public void LagCompensationFrames_returns_zero_while_within_tolerance()
    {
        var clock = new RecordingAudioClock(SampleRate);
        clock.Stamp(4_800); // Audio timeline now at 100 ms.

        // Wall clock 150 ms: 50 ms lag, inside the 100 ms tolerance.
        clock.LagCompensationFrames(
                elapsedHns: 1_500_000,
                toleranceHns: RecordingAudioClock.LagToleranceHns,
                maxFrames: 96_000)
            .Should()
            .Be(0);
    }

    [Fact]
    public void LagCompensationFrames_returns_the_full_lag_beyond_tolerance()
    {
        var clock = new RecordingAudioClock(SampleRate);
        clock.Stamp(4_800); // Audio timeline now at 100 ms.

        // Wall clock 400 ms: 300 ms lag → 14,400 frames of silence.
        clock.LagCompensationFrames(
                elapsedHns: 4_000_000,
                toleranceHns: RecordingAudioClock.LagToleranceHns,
                maxFrames: 96_000)
            .Should()
            .Be(14_400);
    }

    [Fact]
    public void LagCompensationFrames_is_capped_so_one_correction_stays_bounded()
    {
        var clock = new RecordingAudioClock(SampleRate);

        // Wall clock 10 s ahead of a fresh clock.
        clock.LagCompensationFrames(
                elapsedHns: 100_000_000,
                toleranceHns: RecordingAudioClock.LagToleranceHns,
                maxFrames: 1_000)
            .Should()
            .Be(1_000);
    }

    [Fact]
    public void IsAhead_is_true_only_beyond_the_tolerance()
    {
        var clock = new RecordingAudioClock(SampleRate);
        clock.Stamp(9_600); // Audio timeline now at 200 ms.

        clock.IsAhead(elapsedHns: 0, toleranceHns: 1_000_000).Should().BeTrue();
        clock.IsAhead(elapsedHns: 1_500_000, toleranceHns: 1_000_000).Should().BeFalse();
    }

    [Fact]
    public void Constructor_rejects_a_nonpositive_sample_rate()
    {
        Action act = () => _ = new RecordingAudioClock(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

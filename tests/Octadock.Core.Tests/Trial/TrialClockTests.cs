using FluentAssertions;
using Octadock.Core.Tests.Fakes;
using Octadock.Core.Trial;
using Xunit;

namespace Octadock.Core.Tests.Trial;

/// <summary>
/// Monotonic trial-clock integrity (WS5, R36): forward time advances the high-water
/// mark, small drift is tolerated, and a large rollback freezes the countdown
/// instead of expiring honest users or extending the trial for tinkerers.
/// </summary>
public class TrialClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed class MemoryStore : ITrialClockStore
    {
        private TrialClockState? _state;

        public TrialClockState? Load() => _state;

        public void Save(TrialClockState state) => _state = state;
    }

    [Fact]
    public void First_read_seeds_the_high_water_mark_to_now()
    {
        var clock = new TestClock(Start);
        var trial = new TrialClock(clock, new MemoryStore());

        TrialClockReading reading = trial.Read();

        reading.EffectiveUtc.Should().Be(Start);
        reading.IsFrozen.Should().BeFalse();
        reading.ClockLooksWrong.Should().BeFalse();
    }

    [Fact]
    public void Forward_time_advances_the_effective_time()
    {
        var clock = new TestClock(Start);
        var trial = new TrialClock(clock, new MemoryStore());
        trial.Read();

        clock.UtcNow = Start.AddDays(3);
        TrialClockReading reading = trial.Read();

        reading.EffectiveUtc.Should().Be(Start.AddDays(3));
        reading.IsFrozen.Should().BeFalse();
    }

    [Fact]
    public void Small_backward_drift_within_grace_holds_at_high_water_without_freezing()
    {
        var clock = new TestClock(Start);
        var store = new MemoryStore();
        var trial = new TrialClock(clock, store);
        trial.Read(); // high-water = Start

        clock.UtcNow = Start.AddHours(-12); // within the 48h grace
        TrialClockReading reading = trial.Read();

        reading.EffectiveUtc.Should().Be(Start, "effective time never dips below high-water");
        reading.IsFrozen.Should().BeFalse("small drift must not freeze the countdown");
        reading.ClockLooksWrong.Should().BeFalse();
    }

    [Fact]
    public void Large_rollback_freezes_the_countdown()
    {
        var clock = new TestClock(Start.AddDays(5));
        var trial = new TrialClock(clock, new MemoryStore());
        trial.Read(); // high-water = Start + 5d

        clock.UtcNow = Start.AddDays(-30); // a big jump backwards (tamper)
        TrialClockReading reading = trial.Read();

        reading.IsFrozen.Should().BeTrue("a >48h rollback must freeze the trial");
        reading.ClockLooksWrong.Should().BeTrue();
        reading.EffectiveUtc.Should().Be(Start.AddDays(5), "frozen time stays pinned at high-water");
    }

    [Fact]
    public void Rolling_back_then_forward_does_not_extend_the_trial()
    {
        var clock = new TestClock(Start);
        var trial = new TrialClock(clock, new MemoryStore());
        clock.UtcNow = Start.AddDays(10);
        trial.Read(); // high-water = Start + 10d

        // Tinkerer rolls the clock way back to "reset" the trial...
        clock.UtcNow = Start;
        trial.Read();

        // ...then back to real time. Effective time is still >= the high-water it reached.
        clock.UtcNow = Start.AddDays(10).AddHours(1);
        TrialClockReading reading = trial.Read();

        reading.EffectiveUtc.Should().BeOnOrAfter(Start.AddDays(10),
            "a rollback cannot rewind the trial countdown");
        reading.IsFrozen.Should().BeFalse();
    }

    [Fact]
    public void Freeze_clears_once_the_clock_returns_to_or_past_high_water()
    {
        var clock = new TestClock(Start.AddDays(5));
        var store = new MemoryStore();
        var trial = new TrialClock(clock, store);
        trial.Read();

        clock.UtcNow = Start.AddDays(-30);
        trial.Read().IsFrozen.Should().BeTrue();

        clock.UtcNow = Start.AddDays(6); // clock corrected, now past high-water
        TrialClockReading reading = trial.Read();

        reading.IsFrozen.Should().BeFalse("a corrected clock unfreezes the countdown");
        reading.EffectiveUtc.Should().Be(Start.AddDays(6));
    }
}

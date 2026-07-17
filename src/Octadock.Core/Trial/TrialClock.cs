using Octadock.Core.Common;

namespace Octadock.Core.Trial;

/// <summary>Persisted monotonic-clock state: the highest UTC ever observed + a freeze flag.</summary>
public sealed record TrialClockState(DateTimeOffset HighWaterUtc, bool IsFrozen)
{
    public static TrialClockState Initial(DateTimeOffset now) => new(now, false);
}

/// <summary>A single reading of the trial clock.</summary>
/// <param name="EffectiveUtc">The time to evaluate trial expiry against (never below the high-water mark).</param>
/// <param name="IsFrozen">True while the countdown is frozen due to a large backward clock jump.</param>
/// <param name="ClockLooksWrong">True when the UI should warn "your PC clock looks wrong".</param>
public readonly record struct TrialClockReading(DateTimeOffset EffectiveUtc, bool IsFrozen, bool ClockLooksWrong);

/// <summary>Persistence for monotonic clock state (currently unsigned local JSON outside octadock.db; B-04 policy pending).</summary>
public interface ITrialClockStore
{
    TrialClockState? Load();

    void Save(TrialClockState state);
}

/// <summary>
/// A monotonic rollback-detecting clock for the current local alpha trial (WS5,
/// R36). It persists the highest UTC ever seen ("high-water"). While that state
/// remains intact, expiry is evaluated against <c>max(now, highWater)</c>, so
/// setting the system clock backwards cannot extend the trial. A backward jump
/// larger than a 48-hour grace window freezes the countdown at the high-water time
/// and tells the UI to warn the user. This does not resist deletion or replay of
/// the unsigned state; B-04 defines the paid-beta enforcement policy.
/// </summary>
public sealed class TrialClock
{
    /// <summary>Backward drift tolerated before the countdown freezes.</summary>
    public static readonly TimeSpan BackwardGrace = TimeSpan.FromHours(48);

    private readonly IClock _clock;
    private readonly ITrialClockStore _store;

    public TrialClock(IClock clock, ITrialClockStore store)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Reads the effective time, advancing (and persisting) the high-water mark when
    /// the clock moves forward, or freezing when it jumps backward beyond the grace.
    /// </summary>
    public TrialClockReading Read()
    {
        DateTimeOffset now = _clock.UtcNow;
        TrialClockState? loaded = _store.Load();
        TrialClockState state = loaded ?? TrialClockState.Initial(now);

        if (now >= state.HighWaterUtc)
        {
            // Time moving forward (or first run): advance high-water and clear any freeze.
            // Always persist the very first reading so the high-water mark survives even
            // when the initial value equals `now`.
            var advanced = new TrialClockState(now, IsFrozen: false);
            if (loaded is null || advanced != state)
            {
                _store.Save(advanced);
            }

            return new TrialClockReading(now, IsFrozen: false, ClockLooksWrong: false);
        }

        // now < highWater: the clock went backwards.
        TimeSpan backward = state.HighWaterUtc - now;
        bool frozen = backward > BackwardGrace;
        if (frozen != state.IsFrozen)
        {
            _store.Save(state with { IsFrozen = frozen });
        }

        // Effective time never dips below the high-water mark, so a rollback neither
        // early-expires an honest user nor extends the trial for a tinkerer.
        return new TrialClockReading(state.HighWaterUtc, frozen, ClockLooksWrong: frozen);
    }
}

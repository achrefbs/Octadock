using Octadock.Core.Abstractions;
using Octadock.Core.Trial;

namespace Octadock.Core.Licensing;

/// <summary>The overall entitlement mode the app is in.</summary>
public enum LicenseMode
{
    Trial,
    TrialExpired,
    TrialFrozen,
    Licensed,
    Revoked,
}

/// <summary>The resolved license state the gate and UI consult.</summary>
public sealed record LicenseState(
    LicenseMode Mode,
    DateTimeOffset? TrialEndsUtc,
    EntitlementPayload? Entitlement,
    bool ClockLooksWrong,
    bool UpdatesExpired,
    string Reason)
{
    /// <summary>True when the app should behave as fully unlocked (active trial or a valid license).</summary>
    public bool AllowsFullUse => Mode is LicenseMode.Trial or LicenseMode.Licensed;
}

/// <summary>
/// Resolves the current <see cref="LicenseState"/> from the signed entitlement (if
/// any) and the monotonic trial clock (WS5, R11/R36). A valid, active, this-device
/// entitlement wins; a refunded/disputed one is Revoked; anything else falls back to
/// the trial, whose countdown uses the tamper-resistant high-water time. Because a
/// forged entitlement fails verification, it never unlocks — it simply falls through
/// to trial.
/// </summary>
public sealed class LicenseStateService
{
    public static readonly TimeSpan DefaultTrialLength = TimeSpan.FromDays(14);

    private readonly IEntitlementStore _store;
    private readonly EntitlementEvaluator _evaluator;
    private readonly IMachineIdentity _machine;
    private readonly TrialClock _trialClock;
    private readonly TimeSpan _trialLength;

    public LicenseStateService(
        IEntitlementStore store,
        EntitlementEvaluator evaluator,
        IMachineIdentity machine,
        TrialClock trialClock,
        TimeSpan? trialLength = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _trialClock = trialClock ?? throw new ArgumentNullException(nameof(trialClock));
        _trialLength = trialLength ?? DefaultTrialLength;
    }

    public LicenseState Current()
    {
        TrialClockReading reading = _trialClock.Read();
        DateTimeOffset now = reading.EffectiveUtc;

        EntitlementDecision decision = _evaluator.Evaluate(_store.LoadEntitlement(), _machine.MachineHash, now);
        switch (decision.Status)
        {
            case LicenseStatus.Licensed:
                return new LicenseState(
                    LicenseMode.Licensed, null, decision.Payload,
                    reading.ClockLooksWrong, decision.UpdatesExpired, decision.Reason);
            case LicenseStatus.Revoked:
                return new LicenseState(
                    LicenseMode.Revoked, null, decision.Payload, reading.ClockLooksWrong, false, decision.Reason);
            default:
                break; // No valid entitlement → trial territory (forged/invalid also lands here).
        }

        DateTimeOffset trialStart = _store.LoadTrialStart() ?? StartTrial(now);
        DateTimeOffset trialEnds = trialStart + _trialLength;

        if (reading.IsFrozen)
        {
            return new LicenseState(
                LicenseMode.TrialFrozen, trialEnds, null, true, false,
                "Trial countdown frozen — your PC clock looks wrong.");
        }

        if (now >= trialEnds)
        {
            return new LicenseState(
                LicenseMode.TrialExpired, trialEnds, null, reading.ClockLooksWrong, false, "Trial ended.");
        }

        return new LicenseState(
            LicenseMode.Trial, trialEnds, null, reading.ClockLooksWrong, false, "Trial active.");
    }

    private DateTimeOffset StartTrial(DateTimeOffset now)
    {
        _store.SaveTrialStart(now);
        return now;
    }
}

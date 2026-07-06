using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Licensing;
using Octadock.Core.Tests.Fakes;
using Octadock.Core.Trial;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>
/// The gate's state source (WS5, R11/R36): a valid entitlement licenses; a forged one
/// falls back to trial; the trial counts down on tamper-resistant high-water time.
/// </summary>
public class LicenseStateServiceTests
{
    private const string Machine = "this-device-hash";
    private static readonly DateTimeOffset Start = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed class MemEntitlementStore : IEntitlementStore
    {
        private EntitlementEnvelope? _envelope;
        private DateTimeOffset? _trialStart;

        public EntitlementEnvelope? LoadEntitlement() => _envelope;

        public void SaveEntitlement(EntitlementEnvelope envelope) => _envelope = envelope;

        public void ClearEntitlement() => _envelope = null;

        public DateTimeOffset? LoadTrialStart() => _trialStart;

        public void SaveTrialStart(DateTimeOffset startUtc) => _trialStart = startUtc;
    }

    private sealed class FixedMachine : IMachineIdentity
    {
        public string MachineHash => Machine;

        public int DeviceHashVersion => 1;
    }

    private sealed class MemTrialClockStore : ITrialClockStore
    {
        private TrialClockState? _state;

        public TrialClockState? Load() => _state;

        public void Save(TrialClockState state) => _state = state;
    }

    private static (LicenseStateService Service, MemEntitlementStore Store, TestClock Clock, byte[] PrivateKey)
        Build()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        var store = new MemEntitlementStore();
        var clock = new TestClock(Start);
        var trialClock = new TrialClock(clock, new MemTrialClockStore());
        var service = new LicenseStateService(
            store, new EntitlementEvaluator(verifier), new FixedMachine(), trialClock);
        return (service, store, clock, privateKey);
    }

    private static EntitlementPayload Payload(string status = "active", string? machine = null)
        => new() { LicenseKey = "OCTA-X", Product = "octadock-local-beta", Status = status, MachineHash = machine ?? Machine };

    [Fact]
    public void Fresh_install_starts_a_14_day_trial_and_persists_the_start()
    {
        (LicenseStateService service, MemEntitlementStore store, _, _) = Build();

        LicenseState state = service.Current();

        state.Mode.Should().Be(LicenseMode.Trial);
        state.AllowsFullUse.Should().BeTrue();
        state.TrialEndsUtc.Should().Be(Start.AddDays(14));
        store.LoadTrialStart().Should().Be(Start, "trial start is persisted on first run");
    }

    [Fact]
    public void Trial_expires_after_the_window()
    {
        (LicenseStateService service, _, TestClock clock, _) = Build();
        service.Current(); // seed trial start at Start

        clock.UtcNow = Start.AddDays(15);
        LicenseState state = service.Current();

        state.Mode.Should().Be(LicenseMode.TrialExpired);
        state.AllowsFullUse.Should().BeFalse();
    }

    [Fact]
    public void A_valid_entitlement_licenses_the_app()
    {
        (LicenseStateService service, MemEntitlementStore store, _, byte[] privateKey) = Build();
        store.SaveEntitlement(EntitlementTestKit.Sign(privateKey, Payload()));

        LicenseState state = service.Current();

        state.Mode.Should().Be(LicenseMode.Licensed);
        state.AllowsFullUse.Should().BeTrue();
        state.Entitlement!.LicenseKey.Should().Be("OCTA-X");
    }

    [Fact]
    public void A_forged_entitlement_does_not_license_and_falls_back_to_trial()
    {
        (LicenseStateService service, MemEntitlementStore store, _, byte[] privateKey) = Build();
        EntitlementEnvelope good = EntitlementTestKit.Sign(privateKey, Payload());
        // Alter the payload after signing so it no longer matches the signature.
        store.SaveEntitlement(good with { Payload = good.Payload + "AA" });

        LicenseState state = service.Current();

        state.Mode.Should().Be(LicenseMode.Trial, "a forged entitlement must never unlock; it falls back to trial");
    }

    [Fact]
    public void A_refunded_entitlement_is_revoked()
    {
        (LicenseStateService service, MemEntitlementStore store, _, byte[] privateKey) = Build();
        store.SaveEntitlement(EntitlementTestKit.Sign(privateKey, Payload(status: "refunded")));

        service.Current().Mode.Should().Be(LicenseMode.Revoked);
    }

    [Fact]
    public void A_large_clock_rollback_freezes_the_trial()
    {
        (LicenseStateService service, _, TestClock clock, _) = Build();
        clock.UtcNow = Start.AddDays(3);
        service.Current(); // high-water = Start + 3d

        clock.UtcNow = Start.AddDays(-40);
        LicenseState state = service.Current();

        state.Mode.Should().Be(LicenseMode.TrialFrozen);
        state.ClockLooksWrong.Should().BeTrue();
    }
}

using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Licensing;
using Octadock.Core.Tests.Fakes;
using Octadock.Core.Trial;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>
/// The gate blocks new activity once the trial ends / a license is revoked, and every
/// refusal raises exactly one announced prompt (never a silent no-op) — WS5, R16.
/// </summary>
public class LicenseGateTests
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

    private sealed class RecordingNotifications : INotificationService
    {
        public int Count { get; private set; }

        public string? LastTitle { get; private set; }

        public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info, Action? clickAction = null)
        {
            Count++;
            LastTitle = title;
        }
    }

    private sealed class NoopPresenter : IWindowPresenter
    {
        public void ShowHistory() { }

        public void ShowClipboardHistory() { }

        public void ShowTextTools() { }

        public void ShowContext() { }

        public void ShowAiActions(OctadockCommand? launchCommand = null) { }

        public void ShowSettings(string? tab = null) { }

        public Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private static (LicenseGate Gate, MemEntitlementStore Store, TestClock Clock, RecordingNotifications Notes, byte[] PrivateKey)
        Build()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        var store = new MemEntitlementStore();
        var clock = new TestClock(Start);
        var trialClock = new TrialClock(clock, new MemTrialClockStore());
        var license = new LicenseStateService(store, new EntitlementEvaluator(verifier), new FixedMachine(), trialClock);
        var notes = new RecordingNotifications();
        var gate = new LicenseGate(license, notes, new NoopPresenter(), clock);
        return (gate, store, clock, notes, privateKey);
    }

    private static EntitlementPayload Payload(string status = "active")
        => new() { LicenseKey = "OCTA-X", Product = "octadock-local-beta", Status = status, MachineHash = Machine };

    [Fact]
    public void An_active_trial_allows_and_does_not_prompt()
    {
        (LicenseGate gate, _, _, RecordingNotifications notes, _) = Build();

        gate.Allow(GatedFeature.Capture).Should().BeTrue();
        notes.Count.Should().Be(0);
    }

    [Fact]
    public void An_expired_trial_blocks_and_raises_one_announced_prompt()
    {
        (LicenseGate gate, _, TestClock clock, RecordingNotifications notes, _) = Build();
        gate.Allow(GatedFeature.Capture); // seed the trial start at Start
        clock.UtcNow = Start.AddDays(15);

        gate.Allow(GatedFeature.Capture).Should().BeFalse();
        notes.Count.Should().Be(1, "a blocked feature must announce, never no-op silently");
        notes.LastTitle.Should().Contain("trial ended");
    }

    [Fact]
    public void A_valid_license_allows()
    {
        (LicenseGate gate, MemEntitlementStore store, TestClock clock, _, byte[] key) = Build();
        clock.UtcNow = Start.AddDays(30); // well past the trial window
        store.SaveEntitlement(EntitlementTestKit.Sign(key, Payload()));

        gate.Allow(GatedFeature.Ocr).Should().BeTrue();
    }

    [Fact]
    public void A_revoked_license_blocks()
    {
        (LicenseGate gate, MemEntitlementStore store, _, RecordingNotifications notes, byte[] key) = Build();
        store.SaveEntitlement(EntitlementTestKit.Sign(key, Payload(status: "refunded")));

        gate.Allow(GatedFeature.Recording).Should().BeFalse();
        notes.LastTitle.Should().Contain("revoked");
    }

    [Fact]
    public void Repeated_refusals_are_throttled_to_avoid_toast_spam()
    {
        (LicenseGate gate, _, TestClock clock, RecordingNotifications notes, _) = Build();
        gate.Allow(GatedFeature.Capture);
        clock.UtcNow = Start.AddDays(15);

        for (int i = 0; i < 5; i++)
        {
            gate.Allow(GatedFeature.Capture).Should().BeFalse();
        }

        notes.Count.Should().Be(1, "a held hotkey must not spam a toast per press");
    }
}

using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Licensing;
using Octadock.Core.Tests.Fakes;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>
/// The client half of the money → key → activate loop (WS5). A valid, this-device
/// entitlement is verified and stored; anything the client can't verify is rejected
/// and NOT stored, so a hostile server can never unlock the app.
/// </summary>
public class ActivationServiceTests
{
    private const string Machine = "this-device-hash";
    private const string ValidKey = "OCTA-ABCDE-FGHJK-MNPQR-STUVW";
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeMachine : IMachineIdentity
    {
        public string MachineHash => Machine;

        public int DeviceHashVersion => 1;
    }

    private sealed class MemStore : IEntitlementStore
    {
        public EntitlementEnvelope? Saved { get; private set; }

        public EntitlementEnvelope? LoadEntitlement() => Saved;

        public void SaveEntitlement(EntitlementEnvelope envelope) => Saved = envelope;

        public void ClearEntitlement() => Saved = null;

        public DateTimeOffset? LoadTrialStart() => null;

        public void SaveTrialStart(DateTimeOffset startUtc) { }
    }

    private sealed class StubClient(ActivationClientResponse response) : IActivationClient
    {
        public string? LastKey { get; private set; }

        public int Calls { get; private set; }

        public Task<ActivationClientResponse> ActivateAsync(
            string licenseKey, string machineHash, int deviceHashVersion, CancellationToken cancellationToken)
        {
            Calls++;
            LastKey = licenseKey;
            return Task.FromResult(response);
        }
    }

    private static (ActivationService Service, MemStore Store, StubClient Client) Build(
        ActivationClientResponse response, byte[]? signWith = null)
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        _ = signWith; // ring/private key are paired below
        var store = new MemStore();
        var client = new StubClient(response);
        var service = new ActivationService(
            client, new FakeMachine(), new EntitlementEvaluator(verifier), store, new TestClock(Now));
        return (service, store, client);
    }

    private static EntitlementPayload Payload(string status = "active", string? machine = null)
        => new() { LicenseKey = ValidKey, Product = "octadock-local-beta", Status = status, MachineHash = machine ?? Machine };

    [Fact]
    public async Task A_valid_this_device_entitlement_activates_and_is_stored()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        EntitlementEnvelope envelope = EntitlementTestKit.Sign(privateKey, Payload());
        var store = new MemStore();
        var client = new StubClient(new ActivationClientResponse(
            ActivationTransport.Activated, envelope.ToJson(), 1, 3, "Activated"));
        var service = new ActivationService(
            client, new FakeMachine(), new EntitlementEvaluator(verifier), store, new TestClock(Now));

        ActivationResult result = await service.ActivateAsync("  octa-abcde-fghjk-mnpqr-stuvw ");

        result.Kind.Should().Be(ActivationResultKind.Activated);
        result.Succeeded.Should().BeTrue();
        store.Saved.Should().NotBeNull("a verified entitlement is persisted");
        client.LastKey.Should().Be(ValidKey, "the key is normalized before it hits the service");
    }

    [Fact]
    public async Task Successful_activation_notifies_once_after_the_entitlement_is_stored()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        EntitlementEnvelope envelope = EntitlementTestKit.Sign(privateKey, Payload());
        var store = new MemStore();
        var client = new StubClient(new ActivationClientResponse(
            ActivationTransport.Activated, envelope.ToJson(), 1, 3, "Activated"));
        var service = new ActivationService(
            client, new FakeMachine(), new EntitlementEvaluator(verifier), store, new TestClock(Now));
        int notifications = 0;
        bool wasStoredWhenRaised = false;
        service.EntitlementStored += (_, _) =>
        {
            notifications++;
            wasStoredWhenRaised = store.LoadEntitlement() is not null;
        };

        ActivationResult result = await service.ActivateAsync(ValidKey);

        result.Kind.Should().Be(ActivationResultKind.Activated);
        notifications.Should().Be(1);
        wasStoredWhenRaised.Should().BeTrue("ambient UI must read the newly persisted license state");
    }

    [Fact]
    public async Task A_failing_status_observer_does_not_break_activation_or_later_observers()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        EntitlementEnvelope envelope = EntitlementTestKit.Sign(privateKey, Payload());
        var store = new MemStore();
        var client = new StubClient(new ActivationClientResponse(
            ActivationTransport.Activated, envelope.ToJson(), 1, 3, "Activated"));
        var service = new ActivationService(
            client, new FakeMachine(), new EntitlementEvaluator(verifier), store, new TestClock(Now));
        int laterNotifications = 0;
        service.EntitlementStored += (_, _) => throw new InvalidOperationException("broken observer");
        service.EntitlementStored += (_, _) => laterNotifications++;

        ActivationResult result = await service.ActivateAsync(ValidKey);

        result.Kind.Should().Be(ActivationResultKind.Activated);
        store.Saved.Should().NotBeNull();
        laterNotifications.Should().Be(1);
    }

    [Fact]
    public async Task An_entitlement_for_another_device_is_rejected_and_not_stored()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        EntitlementEnvelope envelope = EntitlementTestKit.Sign(privateKey, Payload(machine: "some-other-device"));
        var store = new MemStore();
        var client = new StubClient(new ActivationClientResponse(
            ActivationTransport.Activated, envelope.ToJson(), 1, 3, "Activated"));
        var service = new ActivationService(
            client, new FakeMachine(), new EntitlementEvaluator(verifier), store, new TestClock(Now));

        ActivationResult result = await service.ActivateAsync(ValidKey);

        result.Kind.Should().Be(ActivationResultKind.VerificationFailed);
        store.Saved.Should().BeNull("an entitlement that fails verification must never be stored");
    }

    [Fact]
    public async Task A_forged_entitlement_is_rejected_and_not_stored()
    {
        (EntitlementVerifier verifier, byte[] privateKey) = EntitlementTestKit.NewRing();
        EntitlementEnvelope good = EntitlementTestKit.Sign(privateKey, Payload());
        EntitlementEnvelope forged = good with { Payload = good.Payload + "AA" };
        var store = new MemStore();
        var client = new StubClient(new ActivationClientResponse(
            ActivationTransport.Activated, forged.ToJson(), 1, 3, "Activated"));
        var service = new ActivationService(
            client, new FakeMachine(), new EntitlementEvaluator(verifier), store, new TestClock(Now));
        int notifications = 0;
        service.EntitlementStored += (_, _) => notifications++;

        ActivationResult result = await service.ActivateAsync(ValidKey);

        result.Kind.Should().Be(ActivationResultKind.VerificationFailed);
        store.Saved.Should().BeNull();
        notifications.Should().Be(0, "rejected entitlements must not announce a state change");
    }

    [Fact]
    public async Task A_malformed_key_never_calls_the_service()
    {
        (ActivationService service, MemStore store, StubClient client) = Build(
            new ActivationClientResponse(ActivationTransport.LicenseNotFound, null, null, null, null));

        ActivationResult result = await service.ActivateAsync("total-garbage");

        result.Kind.Should().Be(ActivationResultKind.InvalidKeyFormat);
        client.Calls.Should().Be(0);
        store.Saved.Should().BeNull();
    }

    [Theory]
    [InlineData(ActivationTransport.DeviceLimitReached, ActivationResultKind.DeviceLimitReached)]
    [InlineData(ActivationTransport.LicenseNotFound, ActivationResultKind.KeyNotRecognized)]
    [InlineData(ActivationTransport.LicenseNotActive, ActivationResultKind.LicenseInactive)]
    [InlineData(ActivationTransport.EndpointUnavailable, ActivationResultKind.EndpointUnavailable)]
    [InlineData(ActivationTransport.ProtocolError, ActivationResultKind.Error)]
    public async Task Transport_failures_map_to_user_facing_kinds(
        ActivationTransport transport, ActivationResultKind expected)
    {
        (ActivationService service, MemStore store, _) = Build(
            new ActivationClientResponse(transport, null, 3, 3, transport.ToString()));

        ActivationResult result = await service.ActivateAsync(ValidKey);

        result.Kind.Should().Be(expected);
        store.Saved.Should().BeNull();
    }
}

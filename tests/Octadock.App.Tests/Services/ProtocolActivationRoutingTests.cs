using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Common;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class ProtocolActivationRoutingTests
{
    private const string CurrentKey = "OCTA-ABCDE-FGHJK-MNPQR-STUVW";
    private const string IncomingKey = "OCTA-11111-22222-33333-44444";

    [Fact]
    public async Task Distinct_existing_entitlement_is_not_activated_without_confirmation()
    {
        var client = new RecordingActivationClient();
        var confirmation = new RecordingConfirmation { Result = false };
        var dispatcher = BuildDispatcher(LicensedGate(CurrentKey), client, confirmation);

        CommandResult result = await dispatcher.DispatchAsync(ActivationCommand(IncomingKey));

        result.Message.Should().Contain("existing Octadock license was not changed");
        client.Calls.Should().Be(0, "an external activation must be staged before it can replace a distinct entitlement");
        confirmation.Reviews.Should().ContainSingle().Which.Should().Be(new ActivationReplacementReview(CurrentKey, IncomingKey));
    }

    [Fact]
    public async Task Confirmed_distinct_entitlement_continues_to_activation()
    {
        var client = new RecordingActivationClient();
        var confirmation = new RecordingConfirmation { Result = true };
        var dispatcher = BuildDispatcher(LicensedGate(CurrentKey), client, confirmation);

        await dispatcher.DispatchAsync(ActivationCommand(IncomingKey));

        client.Calls.Should().Be(1);
        confirmation.Reviews.Should().ContainSingle();
    }

    [Fact]
    public async Task Same_license_activation_is_idempotent_without_replacement_prompt()
    {
        var client = new RecordingActivationClient();
        var confirmation = new RecordingConfirmation();
        var dispatcher = BuildDispatcher(LicensedGate(CurrentKey), client, confirmation);

        await dispatcher.DispatchAsync(ActivationCommand("octa abcde fghjk mnpqr stuvw"));

        client.Calls.Should().Be(1);
        confirmation.Reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task Activation_without_existing_entitlement_does_not_prompt_for_replacement()
    {
        var client = new RecordingActivationClient();
        var confirmation = new RecordingConfirmation();
        var dispatcher = BuildDispatcher(NoEntitlementGate(), client, confirmation);

        await dispatcher.DispatchAsync(ActivationCommand(IncomingKey));

        client.Calls.Should().Be(1);
        confirmation.Reviews.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CurrentKey, "OCTA-…-STUVW")]
    [InlineData(IncomingKey, "OCTA-…-44444")]
    public void Confirmation_displays_distinguishable_masked_license_identities(string key, string expected)
        => WpfActivationReplacementConfirmation.Mask(key).Should().Be(expected);

    private static CommandDispatcher BuildDispatcher(
        ILicenseGate gate,
        RecordingActivationClient client,
        IActivationReplacementConfirmation confirmation)
    {
        var activation = new ActivationService(
            client,
            new FakeMachine(),
            new EntitlementEvaluator(new EntitlementVerifier(new Dictionary<string, byte[]>())),
            new MemStore(),
            new TestClock());

        return new CommandDispatcher(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            activation,
            confirmation,
            gate,
            null!,
            NullLogger<CommandDispatcher>.Instance);
    }

    private static OctadockCommand ActivationCommand(string key)
        => OctadockCommand.Create(
            CommandType.Activate,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["key"] = key });

    private static ILicenseGate LicensedGate(string key)
        => new FakeLicenseGate(new LicenseState(
            LicenseMode.Licensed,
            null,
            new EntitlementPayload
            {
                LicenseKey = key,
                Product = "octadock-local-beta",
                Status = "active",
                MachineHash = "test-machine",
            },
            false,
            false,
            "licensed"));

    private static ILicenseGate NoEntitlementGate()
        => new FakeLicenseGate(new LicenseState(LicenseMode.Trial, null, null, false, false, "trial"));

    private sealed class RecordingConfirmation : IActivationReplacementConfirmation
    {
        public bool Result { get; init; }
        public List<ActivationReplacementReview> Reviews { get; } = [];

        public bool Confirm(ActivationReplacementReview review)
        {
            Reviews.Add(review);
            return Result;
        }
    }

    private sealed class RecordingActivationClient : IActivationClient
    {
        public int Calls { get; private set; }

        public Task<ActivationClientResponse> ActivateAsync(
            string licenseKey,
            string machineHash,
            int deviceHashVersion,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ActivationClientResponse(
                ActivationTransport.LicenseNotFound,
                null,
                null,
                null,
                "not found"));
        }
    }

    private sealed class FakeLicenseGate(LicenseState state) : ILicenseGate
    {
        public LicenseState State { get; } = state;
        public bool AllowsFullUse => State.AllowsFullUse;
        public event EventHandler<LicenseState>? Refused { add { } remove { } }
        public bool Allow(GatedFeature feature) => AllowsFullUse;
    }

    private sealed class FakeMachine : IMachineIdentity
    {
        public string MachineHash => "test-machine";
        public int DeviceHashVersion => 1;
    }

    private sealed class MemStore : IEntitlementStore
    {
        public EntitlementEnvelope? LoadEntitlement() => null;
        public void SaveEntitlement(EntitlementEnvelope envelope) { }
        public void ClearEntitlement() { }
        public DateTimeOffset? LoadTrialStart() => null;
        public void SaveTrialStart(DateTimeOffset startUtc) { }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        public DateTimeOffset LocalNow => UtcNow;
    }
}

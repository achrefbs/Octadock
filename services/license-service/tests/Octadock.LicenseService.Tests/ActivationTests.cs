using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Octadock.LicenseService.Configuration;
using Octadock.LicenseService.Data;
using Octadock.LicenseService.Licensing;
using Xunit;

namespace Octadock.LicenseService.Tests;

/// <summary>
/// Activation (WS4/WS5, R13): a valid key + device yields a signed, device-bound
/// entitlement that verifies; activation is idempotent per device; the device limit
/// is enforced; the issued entitlement verifies against the service's trust anchor.
/// </summary>
public class ActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static EntitlementIssuer NewIssuer()
        => new(Options.Create(new EntitlementSigningOptions()), NullLogger<EntitlementIssuer>.Instance);

    private static string IssueLicenseKey(TempLicenseDatabase db, int deviceLimit = 3)
    {
        LicenseIssuanceResult issued = db.Repository.IssueLicense(
            new LicenseIssuance("octadock-local-beta", "buyer@example.com", "cus_1", "cs_act", "pi_act",
                4900, "usd", deviceLimit, Now.AddMonths(12)),
            Now);
        return issued.LicenseKey;
    }

    [Fact]
    public void Activating_a_valid_key_issues_a_verifiable_device_bound_entitlement()
    {
        using var db = new TempLicenseDatabase();
        string key = IssueLicenseKey(db);
        EntitlementIssuer issuer = NewIssuer();

        ActivationResult result = db.Repository.Activate(key, "device-1-hash", 1, Now);
        result.Outcome.Should().Be(ActivationOutcome.Activated);

        EntitlementEnvelope entitlement = issuer.Issue(result.License!, "device-1-hash", 1, Now);

        // Verify with a verifier built from the service's published trust anchor.
        (string keyId, string publicKeyB64) = issuer.TrustAnchor;
        var verifier = new EntitlementVerifier(
            new Dictionary<string, byte[]> { [keyId] = Base64Url.Decode(publicKeyB64) });

        verifier.TryVerify(entitlement, out byte[] payloadBytes).Should().BeTrue();
        using JsonDocument payload = JsonDocument.Parse(payloadBytes);
        payload.RootElement.GetProperty("machine_hash").GetString().Should().Be("device-1-hash");
        payload.RootElement.GetProperty("status").GetString().Should().Be("active");
        payload.RootElement.GetProperty("license_key").GetString().Should().Be(key);
    }

    [Fact]
    public void Re_activating_the_same_device_is_idempotent()
    {
        using var db = new TempLicenseDatabase();
        string key = IssueLicenseKey(db);

        db.Repository.Activate(key, "device-1", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);
        ActivationResult again = db.Repository.Activate(key, "device-1", 1, Now.AddHours(1));

        again.Outcome.Should().Be(ActivationOutcome.AlreadyActive);
        again.ActiveDeviceCount.Should().Be(1, "the same device must not consume a second slot");
    }

    [Fact]
    public void The_device_limit_is_enforced()
    {
        using var db = new TempLicenseDatabase();
        string key = IssueLicenseKey(db, deviceLimit: 3);

        db.Repository.Activate(key, "d1", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);
        db.Repository.Activate(key, "d2", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);
        db.Repository.Activate(key, "d3", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);

        ActivationResult fourth = db.Repository.Activate(key, "d4", 1, Now);
        fourth.Outcome.Should().Be(ActivationOutcome.DeviceLimitReached);
        fourth.ActiveDeviceCount.Should().Be(3);
    }

    [Fact]
    public void An_unknown_key_is_not_found()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.Activate("OCTA-DOES-NOT-EXIST", "d1", 1, Now)
            .Outcome.Should().Be(ActivationOutcome.LicenseNotFound);
    }

    [Fact]
    public void A_revoked_license_cannot_activate()
    {
        using var db = new TempLicenseDatabase();
        string key = IssueLicenseKey(db);
        db.Repository.RevokeByPaymentIntent("pi_act", "refunded", "charge.refunded", "webhook", Now);

        db.Repository.Activate(key, "d1", 1, Now).Outcome.Should().Be(ActivationOutcome.LicenseNotActive);
    }
}

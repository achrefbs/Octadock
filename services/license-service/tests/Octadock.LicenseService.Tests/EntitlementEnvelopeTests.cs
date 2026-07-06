using System.Text;
using System.Text.Json;
using FluentAssertions;
using Octadock.LicenseService.Licensing;
using Xunit;

namespace Octadock.LicenseService.Tests;

public class EntitlementEnvelopeTests
{
    private static (EntitlementSigner Signer, EntitlementVerifier Verifier) MakeRing(string keyId = "k1")
    {
        Ed25519KeyMaterial keys = Ed25519KeyMaterial.Generate();
        var signer = new EntitlementSigner(keyId, keys.PrivateKey);
        var verifier = new EntitlementVerifier(new Dictionary<string, byte[]> { [keyId] = keys.PublicKey });
        return (signer, verifier);
    }

    [Fact]
    public void Signed_envelope_round_trips_and_verifies()
    {
        (EntitlementSigner signer, EntitlementVerifier verifier) = MakeRing();
        byte[] payload = Encoding.UTF8.GetBytes("""{"schema":1,"license_key":"OCTA-AAAAA-BBBBB-CCCCC-DDDDD"}""");

        EntitlementEnvelope envelope = signer.Sign(payload);
        EntitlementEnvelope? parsed = EntitlementEnvelope.TryParse(envelope.ToJson());

        parsed.Should().NotBeNull();
        verifier.TryVerify(parsed!, out byte[] verifiedPayload).Should().BeTrue();
        Encoding.UTF8.GetString(verifiedPayload).Should().Contain("OCTA-AAAAA");
    }

    [Fact]
    public void Tampered_payload_fails_verification()
    {
        (EntitlementSigner signer, EntitlementVerifier verifier) = MakeRing();
        EntitlementEnvelope envelope = signer.Sign(Encoding.UTF8.GetBytes("""{"license_key":"OCTA-1"}"""));
        EntitlementEnvelope tampered = envelope with
        {
            Payload = Base64Url.Encode(Encoding.UTF8.GetBytes("""{"license_key":"OCTA-EVIL"}""")),
        };

        verifier.TryVerify(tampered, out _).Should().BeFalse();
    }

    [Fact]
    public void Unknown_key_id_fails_verification()
    {
        (EntitlementSigner signer, _) = MakeRing("k1");
        var otherVerifier = new EntitlementVerifier(
            new Dictionary<string, byte[]> { ["k2"] = Ed25519KeyMaterial.Generate().PublicKey });
        EntitlementEnvelope envelope = signer.Sign(Encoding.UTF8.GetBytes("{}"));

        otherVerifier.TryVerify(envelope, out _).Should().BeFalse();
    }

    // ITEM 9 ACCEPTANCE (R15): today's verifier accepts a synthetic FUTURE-schema entitlement.
    [Fact]
    public void Future_schema_entitlement_with_unknown_fields_is_accepted_by_todays_verifier()
    {
        (EntitlementSigner signer, EntitlementVerifier verifier) = MakeRing();

        // A payload from a hypothetical future release: a higher payload schema and
        // fields this binary has never heard of.
        byte[] payload = Encoding.UTF8.GetBytes(
            """{"schema":999,"license_key":"OCTA-FUTURE","tier":"quantum","unknown_future_field":{"nested":true}}""");

        // Wrap it in a FUTURE envelope schema number too.
        EntitlementEnvelope envelope = signer.Sign(payload, schema: 42);

        EntitlementEnvelope? parsed = EntitlementEnvelope.TryParse(envelope.ToJson());
        parsed.Should().NotBeNull();
        parsed!.Schema.Should().Be(42);

        verifier.TryVerify(parsed!, out byte[] verifiedPayload).Should().BeTrue(
            "a shipped verifier must accept future-schema entitlements when the signature is valid (R15)");

        using JsonDocument doc = JsonDocument.Parse(verifiedPayload);
        doc.RootElement.GetProperty("license_key").GetString().Should().Be("OCTA-FUTURE");
    }

    [Fact]
    public void Machine_hash_is_deterministic_case_insensitive_and_versioned()
    {
        string a = MachineHash.Compute("6F1C2D3E-1111-2222-3333-444455556666");
        string b = MachineHash.Compute("  6f1c2d3e-1111-2222-3333-444455556666  ");

        a.Should().Be(b);
        a.Length.Should().Be(64); // SHA-256 hex
        MachineHash.CurrentVersion.Should().Be(1);
    }
}

using System.Text;
using FluentAssertions;
using Octadock.Core.Licensing;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>
/// Client entitlement evaluation (WS5, R11): only a signature-valid, active,
/// this-device entitlement unlocks; forged/edited/wrong-key/wrong-device/refunded
/// entitlements never do; a license past its update window still works.
/// </summary>
public class EntitlementEvaluatorTests
{
    private const string ThisMachine = "abc123-this-device-hash";
    private const string KeyId = "k1";
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private sealed class Ring
    {
        public byte[] PrivateKey { get; }

        public EntitlementVerifier Verifier { get; }

        public Ring()
        {
            var generator = new Ed25519KeyPairGenerator();
            generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
            AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();
            PrivateKey = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
            byte[] publicKey = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
            Verifier = new EntitlementVerifier(new Dictionary<string, byte[]> { [KeyId] = publicKey });
        }
    }

    private static EntitlementEnvelope Sign(byte[] privateKey, EntitlementPayload payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJson());
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(bytes, 0, bytes.Length);
        byte[] signature = signer.GenerateSignature();
        return new EntitlementEnvelope
        {
            Schema = 1,
            KeyId = KeyId,
            Payload = Base64Url.Encode(bytes),
            Sig = Base64Url.Encode(signature),
        };
    }

    private static EntitlementPayload Payload(
        string status = "active", string? machine = null, DateTimeOffset? updatesUntil = null)
        => new()
        {
            LicenseKey = "OCTA-TEST",
            Product = "octadock-local-beta",
            Status = status,
            MachineHash = machine ?? ThisMachine,
            UpdatesUntil = updatesUntil ?? Now.AddMonths(12),
        };

    [Fact]
    public void Valid_active_entitlement_for_this_device_is_licensed()
    {
        var ring = new Ring();
        var evaluator = new EntitlementEvaluator(ring.Verifier);

        EntitlementDecision decision = evaluator.Evaluate(Sign(ring.PrivateKey, Payload()), ThisMachine, Now);

        decision.Status.Should().Be(LicenseStatus.Licensed);
        decision.IsLicensed.Should().BeTrue();
        decision.UpdatesExpired.Should().BeFalse();
    }

    [Fact]
    public void No_entitlement_is_none()
        => new EntitlementEvaluator(new Ring().Verifier).Evaluate(null, ThisMachine, Now)
            .Status.Should().Be(LicenseStatus.None);

    [Fact]
    public void Tampered_payload_does_not_unlock()
    {
        var ring = new Ring();
        var evaluator = new EntitlementEvaluator(ring.Verifier);
        EntitlementEnvelope env = Sign(ring.PrivateKey, Payload());
        EntitlementEnvelope forged = env with
        {
            Payload = Base64Url.Encode(Encoding.UTF8.GetBytes(Payload().ToJson().Replace("OCTA-TEST", "OCTA-HACK"))),
        };

        evaluator.Evaluate(forged, ThisMachine, Now).Status.Should().Be(LicenseStatus.InvalidSignature);
    }

    [Fact]
    public void Entitlement_for_another_device_is_rejected()
    {
        var ring = new Ring();
        var evaluator = new EntitlementEvaluator(ring.Verifier);

        evaluator.Evaluate(Sign(ring.PrivateKey, Payload(machine: "some-other-device")), ThisMachine, Now)
            .Status.Should().Be(LicenseStatus.WrongDevice);
    }

    [Fact]
    public void Refunded_entitlement_is_revoked()
    {
        var ring = new Ring();
        var evaluator = new EntitlementEvaluator(ring.Verifier);

        evaluator.Evaluate(Sign(ring.PrivateKey, Payload(status: "refunded")), ThisMachine, Now)
            .Status.Should().Be(LicenseStatus.Revoked);
    }

    [Fact]
    public void Entitlement_signed_by_an_untrusted_key_is_rejected()
    {
        var trusted = new Ring();
        var attacker = new Ring();
        var evaluator = new EntitlementEvaluator(trusted.Verifier);

        // Same key_id in the envelope, but signed with a different (untrusted) key.
        evaluator.Evaluate(Sign(attacker.PrivateKey, Payload()), ThisMachine, Now)
            .Status.Should().Be(LicenseStatus.InvalidSignature);
    }

    [Fact]
    public void Licensed_past_the_update_window_still_works()
    {
        var ring = new Ring();
        var evaluator = new EntitlementEvaluator(ring.Verifier);

        EntitlementDecision decision = evaluator.Evaluate(
            Sign(ring.PrivateKey, Payload(updatesUntil: Now.AddMonths(-1))), ThisMachine, Now);

        decision.Status.Should().Be(LicenseStatus.Licensed);
        decision.UpdatesExpired.Should().BeTrue();
    }
}

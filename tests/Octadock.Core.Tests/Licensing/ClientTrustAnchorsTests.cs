using System.Text;
using FluentAssertions;
using Octadock.Core.Licensing;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>
/// Cross-checks that the DEV public key embedded in the client trust ring
/// (<c>dev1</c>) actually matches the DEV private seed the license service uses in
/// its Development config — so a locally-run service issues entitlements this client
/// accepts. If this fails, the committed keypair halves have drifted apart.
/// </summary>
public class ClientTrustAnchorsTests
{
    // The DEV private seed (std base64), mirrored in the license service's
    // appsettings.Development.json. Dev-only; production is KMS-backed (founder-gated).
    private const string DevPrivateSeedBase64 = "7cQMnTWKYl/KvTWSrAvkpfO19yRtEvy2CBhmbZd8KP0=";

    [Fact]
    public void Embedded_dev_public_key_verifies_a_signature_from_the_dev_private_seed()
    {
        byte[] seed = Convert.FromBase64String(DevPrivateSeedBase64);
        byte[] payload = Encoding.UTF8.GetBytes("""{"schema":1,"status":"active"}""");

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(seed, 0));
        signer.BlockUpdate(payload, 0, payload.Length);
        byte[] signature = signer.GenerateSignature();

        var envelope = new EntitlementEnvelope
        {
            Schema = 1,
            KeyId = ClientTrustAnchors.DevKeyId,
            Payload = Base64Url.Encode(payload),
            Sig = Base64Url.Encode(signature),
        };

        var verifier = new EntitlementVerifier(ClientTrustAnchors.Default);
        verifier.TryVerify(envelope, out byte[] verified).Should().BeTrue(
            "the embedded dev1 public key must match the committed dev private seed");
        verified.Should().Equal(payload);
    }

    [Fact]
    public void Trust_ring_rejects_an_unknown_key_id()
    {
        var verifier = new EntitlementVerifier(ClientTrustAnchors.Default);
        var envelope = new EntitlementEnvelope
        {
            Schema = 1,
            KeyId = "no-such-key",
            Payload = Base64Url.Encode(Encoding.UTF8.GetBytes("{}")),
            Sig = Base64Url.Encode(new byte[64]),
        };

        verifier.TryVerify(envelope, out _).Should().BeFalse();
    }
}

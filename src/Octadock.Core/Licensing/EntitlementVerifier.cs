using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Octadock.Core.Licensing;

/// <summary>
/// Verify-only Ed25519 checker for signed entitlements on the client (WS4/WS5, R11/R15).
/// Holds a trust ring of public keys (<c>key_id → public key</c>) so a leaked signing
/// key retires in a normal client release. Verifies the signature over the RAW payload
/// bytes, never a re-serialization, so a forged or edited entitlement never unlocks and
/// future-schema entitlements still verify.
/// </summary>
public sealed class EntitlementVerifier
{
    private readonly IReadOnlyDictionary<string, byte[]> _trustedKeys;

    public EntitlementVerifier(IReadOnlyDictionary<string, byte[]> trustedKeys)
        => _trustedKeys = trustedKeys ?? throw new ArgumentNullException(nameof(trustedKeys));

    /// <summary>Returns true and the raw payload bytes when the envelope's signature verifies.</summary>
    public bool TryVerify(EntitlementEnvelope? envelope, out byte[] payloadBytes)
    {
        payloadBytes = Array.Empty<byte>();
        if (envelope is null || string.IsNullOrEmpty(envelope.KeyId))
        {
            return false;
        }

        if (!_trustedKeys.TryGetValue(envelope.KeyId, out byte[]? publicKey))
        {
            return false;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64Url.Decode(envelope.Payload);
            signature = Base64Url.Decode(envelope.Sig);
        }
        catch (FormatException)
        {
            return false;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(payload, 0, payload.Length);
        if (!verifier.VerifySignature(signature))
        {
            return false;
        }

        payloadBytes = payload;
        return true;
    }
}

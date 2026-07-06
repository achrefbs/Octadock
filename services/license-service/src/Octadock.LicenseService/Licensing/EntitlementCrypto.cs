using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Octadock.LicenseService.Licensing;

/// <summary>A raw Ed25519 keypair (32-byte seed / 32-byte public key).</summary>
public sealed record Ed25519KeyMaterial(byte[] PrivateKey, byte[] PublicKey)
{
    /// <summary>Generates a fresh keypair (dev/test/tooling; production keys live in KMS).</summary>
    public static Ed25519KeyMaterial Generate()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        AsymmetricCipherKeyPair pair = generator.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)pair.Private;
        var pub = (Ed25519PublicKeyParameters)pair.Public;
        return new Ed25519KeyMaterial(priv.GetEncoded(), pub.GetEncoded());
    }
}

/// <summary>
/// Signs entitlement payloads into <see cref="EntitlementEnvelope"/>s. In production
/// the private key never leaves KMS (WS4); this class is the reference/dev signer and
/// defines the exact bytes-signed contract the client verifier must match.
/// </summary>
public sealed class EntitlementSigner
{
    private readonly string _keyId;
    private readonly byte[] _privateKey;

    public EntitlementSigner(string keyId, byte[] privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(privateKey);
        _keyId = keyId;
        _privateKey = privateKey;
    }

    /// <summary>Signs the raw payload bytes and wraps them in an envelope.</summary>
    public EntitlementEnvelope Sign(byte[] payloadBytes, int schema = EntitlementEnvelope.CurrentSchema)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(_privateKey, 0));
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        byte[] signature = signer.GenerateSignature();

        return new EntitlementEnvelope
        {
            Schema = schema,
            KeyId = _keyId,
            Payload = Base64Url.Encode(payloadBytes),
            Sig = Base64Url.Encode(signature),
        };
    }
}

/// <summary>
/// Verifies entitlement envelopes against a trust ring of public keys (key_id →
/// public key). Multiple keys let a leaked key retire in a normal client release.
/// Unknown/future envelope schema values are accepted: the Ed25519 signature over
/// the RAW payload bytes is the sole authority, so a client shipped today keeps
/// accepting entitlements minted by a future, richer schema (R15).
/// </summary>
public sealed class EntitlementVerifier
{
    private readonly IReadOnlyDictionary<string, byte[]> _trustedKeys;

    public EntitlementVerifier(IReadOnlyDictionary<string, byte[]> trustedKeys)
        => _trustedKeys = trustedKeys ?? throw new ArgumentNullException(nameof(trustedKeys));

    /// <summary>
    /// Returns true and the raw payload bytes when the envelope's signature verifies
    /// under the trusted key named by <c>key_id</c>; false otherwise.
    /// </summary>
    public bool TryVerify(EntitlementEnvelope envelope, out byte[] payloadBytes)
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

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octadock.LicenseService.Configuration;
using Octadock.LicenseService.Data;
using Org.BouncyCastle.Crypto.Parameters;

namespace Octadock.LicenseService.Licensing;

/// <summary>Issues signed entitlements for activated devices, and exposes its public trust anchor.</summary>
public interface IEntitlementIssuer
{
    EntitlementEnvelope Issue(LicenseLookup license, string machineHash, int deviceHashVersion, DateTimeOffset now);

    /// <summary>The current signing key's id + public key (base64url) — clients embed this in their trust ring.</summary>
    (string KeyId, string PublicKeyBase64Url) TrustAnchor { get; }
}

/// <inheritdoc />
public sealed class EntitlementIssuer : IEntitlementIssuer
{
    private readonly EntitlementSigner _signer;
    private readonly string _keyId;
    private readonly byte[] _publicKey;

    public EntitlementIssuer(IOptions<EntitlementSigningOptions> options, ILogger<EntitlementIssuer> logger)
    {
        EntitlementSigningOptions opts = options.Value;
        _keyId = string.IsNullOrWhiteSpace(opts.KeyId) ? "k1" : opts.KeyId;

        byte[] privateKey;
        if (!string.IsNullOrWhiteSpace(opts.PrivateKeyBase64))
        {
            privateKey = Convert.FromBase64String(opts.PrivateKeyBase64);
            _publicKey = new Ed25519PrivateKeyParameters(privateKey, 0).GeneratePublicKey().GetEncoded();
        }
        else
        {
            Ed25519KeyMaterial dev = Ed25519KeyMaterial.Generate();
            privateKey = dev.PrivateKey;
            _publicKey = dev.PublicKey;
            logger.LogWarning(
                "EntitlementSigning has no configured key — generated an EPHEMERAL DEV key '{KeyId}'. " +
                "This is fine for local testing but MUST be replaced with a KMS-backed key before beta.",
                _keyId);
        }

        _signer = new EntitlementSigner(_keyId, privateKey);
    }

    public (string KeyId, string PublicKeyBase64Url) TrustAnchor => (_keyId, Base64Url.Encode(_publicKey));

    public EntitlementEnvelope Issue(
        LicenseLookup license, string machineHash, int deviceHashVersion, DateTimeOffset now)
    {
        // Field names MUST match the client's EntitlementPayload (snake_case) exactly.
        var payload = new
        {
            schema = 1,
            license_key = license.LicenseKey,
            product = license.Product,
            status = "active",
            machine_hash = machineHash,
            device_hash_v = deviceHashVersion,
            seats = license.Seats,
            updates_until = license.UpdatesUntil,
            issued_at = now,
        };

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return _signer.Sign(payloadBytes);
    }
}

namespace Octadock.LicenseService.Configuration;

/// <summary>
/// Entitlement signing configuration. In production the private key is KMS-backed
/// and injected here as base64; if left empty the service generates an EPHEMERAL
/// DEV key at startup (and logs a loud warning) so the activation loop is testable
/// end-to-end before KMS exists.
/// </summary>
public sealed class EntitlementSigningOptions
{
    public const string SectionName = "EntitlementSigning";

    /// <summary>The key id stamped into the envelope (must match a client trust-ring entry).</summary>
    public string KeyId { get; set; } = "k1";

    /// <summary>Standard base64 of the 32-byte Ed25519 private seed. Empty ⇒ ephemeral dev key.</summary>
    public string PrivateKeyBase64 { get; set; } = string.Empty;
}

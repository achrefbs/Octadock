namespace Octadock.Core.Licensing;

/// <summary>
/// The client's Ed25519 trust ring (<c>key_id → public key</c>) for verifying signed
/// entitlements offline (WS4/WS5, R15/R20). See ENTITLEMENT_ENVELOPE.md.
///
/// The production signing key is KMS-backed and its custody is founder-gated. Until
/// that key is provisioned the ring ships the DEV signing key (<c>dev1</c>) whose
/// PRIVATE half a locally-run license service holds (its
/// <c>appsettings.Development.json</c>), so the money → key → activate → Licensed loop
/// is verifiable end-to-end on a developer machine. Public keys are not secrets.
///
/// When the production key lands, add its public half here under a NEW <c>key_id</c>
/// (never edit an existing entry — shipped verifiers are immortal). The old key retires
/// in a later release once no live entitlement references it.
/// </summary>
public static class ClientTrustAnchors
{
    /// <summary>The dev signing key id (matches the service's appsettings.Development.json).</summary>
    public const string DevKeyId = "dev1";

    // base64url of the 32-byte Ed25519 public key for 'dev1'. Deterministically derived
    // from a fixed dev seed; the matching private seed lives ONLY in the service's
    // Development config. Not a secret.
    private const string DevPublicKeyBase64Url = "uot8gEBPMjaM6JwMAN03DZJgPDSvxmUyyJYmHgrMuuw";

    /// <summary>
    /// The default embedded trust ring. Production KMS public key is added here
    /// (new key_id) when key custody lands — founder-gated (R20).
    /// </summary>
    public static IReadOnlyDictionary<string, byte[]> Default { get; } = Build();

    private static IReadOnlyDictionary<string, byte[]> Build()
    {
        var ring = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [DevKeyId] = Base64Url.Decode(DevPublicKeyBase64Url),
        };
        return ring;
    }
}

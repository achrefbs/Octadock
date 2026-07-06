using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.LicenseService.Licensing;

/// <summary>
/// The FROZEN, versioned signed-entitlement envelope (WS13, R15). It is the outer
/// wrapper around an opaque entitlement payload:
///
///   schema  — envelope format version (int). Verifiers ACCEPT unknown/future values;
///             the signature over the raw payload bytes is the authority, not the number.
///   key_id  — which signing key produced <c>sig</c> (multi-key trust ring / rotation).
///   payload — Base64Url of the RAW payload bytes. The signature is over exactly these
///             bytes, so adding fields to the payload never invalidates a shipped verifier.
///   sig     — Base64Url Ed25519 signature over the raw payload bytes.
///
/// Do not reorder or rename these fields once the first key issues — shipped
/// verifiers are immortal. New needs go INSIDE the payload (which is unknown-field
/// tolerant) or behind a new, additively-verified envelope schema.
/// </summary>
public sealed record EntitlementEnvelope
{
    /// <summary>Current envelope schema version.</summary>
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    [JsonPropertyName("schema")]
    public int Schema { get; init; } = CurrentSchema;

    [JsonPropertyName("key_id")]
    public string KeyId { get; init; } = string.Empty;

    /// <summary>Base64Url of the raw payload bytes.</summary>
    [JsonPropertyName("payload")]
    public string Payload { get; init; } = string.Empty;

    /// <summary>Base64Url of the Ed25519 signature over the raw payload bytes.</summary>
    [JsonPropertyName("sig")]
    public string Sig { get; init; } = string.Empty;

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>Parses an envelope, tolerating unknown/extra fields. Null if malformed.</summary>
    public static EntitlementEnvelope? TryParse(string json)
    {
        try
        {
            // System.Text.Json ignores unknown members by default → future envelope
            // fields do not break today's parser.
            return JsonSerializer.Deserialize<EntitlementEnvelope>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Core.Licensing;

/// <summary>
/// Client-side view of the FROZEN entitlement envelope (see ENTITLEMENT_ENVELOPE.md).
/// Mirrors the license service's type exactly. The client only ever VERIFIES; it
/// never signs. Unknown fields and unknown/future <c>schema</c> values are tolerated
/// so a client shipped today keeps accepting entitlements minted by a future release.
/// </summary>
public sealed record EntitlementEnvelope
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new();

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

    public static EntitlementEnvelope? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EntitlementEnvelope>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

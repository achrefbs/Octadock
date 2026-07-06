using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Core.Licensing;

/// <summary>
/// The signed entitlement payload (v1 known fields; unknown fields tolerated). This
/// is the content the Ed25519 signature covers. See ENTITLEMENT_ENVELOPE.md.
/// </summary>
public sealed record EntitlementPayload
{
    private static readonly JsonSerializerOptions Options = new();

    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    [JsonPropertyName("license_key")]
    public string LicenseKey { get; init; } = string.Empty;

    [JsonPropertyName("product")]
    public string Product { get; init; } = string.Empty;

    /// <summary>"active" grants entitlement; anything else (revoked/refunded) does not.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

    [JsonPropertyName("machine_hash")]
    public string MachineHash { get; init; } = string.Empty;

    [JsonPropertyName("device_hash_v")]
    public int DeviceHashVersion { get; init; } = 1;

    [JsonPropertyName("seats")]
    public int Seats { get; init; } = 1;

    [JsonPropertyName("updates_until")]
    public DateTimeOffset? UpdatesUntil { get; init; }

    [JsonPropertyName("issued_at")]
    public DateTimeOffset? IssuedAt { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static EntitlementPayload? TryParse(ReadOnlySpan<byte> payloadBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<EntitlementPayload>(payloadBytes, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

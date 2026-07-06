using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Core.Updates;

/// <summary>
/// The published "latest release" manifest the update check reads (WS1). The manifest
/// bytes are the authority for the signature (see <see cref="UpdateCheckService"/>); the
/// installer's own SHA-256 + code signature remain the integrity gate for the download.
/// </summary>
public sealed record UpdateManifest
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string? Channel { get; init; }

    [JsonPropertyName("releaseDate")]
    public DateTimeOffset? ReleaseDate { get; init; }

    /// <summary>The canonical download URL for the signed installer.</summary>
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    /// <summary>Published SHA-256 of the installer, for verify-your-download.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("notesUrl")]
    public string? NotesUrl { get; init; }

    public static UpdateManifest? TryParse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<UpdateManifest>(bytes, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static UpdateManifest? TryParse(string json)
        => string.IsNullOrWhiteSpace(json) ? null : TryParse(Encoding.UTF8.GetBytes(json));
}

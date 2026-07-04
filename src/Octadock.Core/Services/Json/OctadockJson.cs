using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Core.Services.Json;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for Octadock's persisted JSON
/// (project manifests, annotation objects). camelCase property names, enums as
/// strings, and <see cref="RgbaColorJsonConverter"/> for colors.
/// </summary>
public static class OctadockJson
{
    /// <summary>The canonical serializer options used across the project format.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNameCaseInsensitive = true,
        };

        // Enum values serialize as camelCase strings (e.g. Arrow -> "arrow") to
        // match the annotation schema in the data spec and stay framework-neutral.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new RgbaColorJsonConverter());
        return options;
    }
}

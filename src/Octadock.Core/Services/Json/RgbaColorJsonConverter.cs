using System.Text.Json;
using System.Text.Json.Serialization;
using Octadock.Core.Primitives;

namespace Octadock.Core.Services.Json;

/// <summary>
/// Serializes <see cref="RgbaColor"/> as a portable <c>#RRGGBB</c> /
/// <c>#RRGGBBAA</c> hex string (matching the annotation object schema) rather than
/// an object with four numeric channels.
/// </summary>
public sealed class RgbaColorJsonConverter : JsonConverter<RgbaColor>
{
    /// <inheritdoc />
    public override RgbaColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a hex color string but found {reader.TokenType}.");
        }

        string? hex = reader.GetString();
        if (RgbaColor.TryParse(hex, out RgbaColor color))
        {
            return color;
        }

        throw new JsonException($"'{hex}' is not a valid RGBA hex color.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, RgbaColor value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToHex());
}

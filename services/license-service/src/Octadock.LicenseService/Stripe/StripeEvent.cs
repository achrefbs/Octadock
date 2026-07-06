using System.Text.Json;

namespace Octadock.LicenseService.Stripe;

/// <summary>
/// The minimal shape of a Stripe event the license service needs: its id, type,
/// and the <c>data.object</c> element. Parsing is deliberately tolerant — Stripe
/// adds fields over time — and only reads what issuance/revocation require.
/// </summary>
public sealed class StripeEvent
{
    private StripeEvent(string id, string type, JsonElement dataObject)
    {
        Id = id;
        Type = type;
        DataObject = dataObject;
    }

    public string Id { get; }

    public string Type { get; }

    public JsonElement DataObject { get; }

    /// <summary>Parses an event body. Returns null if it is not a well-formed event.</summary>
    public static StripeEvent? TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("id", out JsonElement idElement) ||
                idElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("type", out JsonElement typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            JsonElement dataObject = default;
            if (root.TryGetProperty("data", out JsonElement data) &&
                data.TryGetProperty("object", out JsonElement obj))
            {
                // Clone so the element outlives the JsonDocument's using scope.
                dataObject = obj.Clone();
            }

            return new StripeEvent(idElement.GetString()!, typeElement.GetString()!, dataObject);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads a string property from <see cref="DataObject"/>, or null.</summary>
    public string? GetString(string property)
        => DataObject.ValueKind == JsonValueKind.Object &&
           DataObject.TryGetProperty(property, out JsonElement element) &&
           element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    /// <summary>Reads a nested string (e.g. <c>customer_details.email</c>), or null.</summary>
    public string? GetNestedString(string parent, string child)
    {
        if (DataObject.ValueKind == JsonValueKind.Object &&
            DataObject.TryGetProperty(parent, out JsonElement parentElement) &&
            parentElement.ValueKind == JsonValueKind.Object &&
            parentElement.TryGetProperty(child, out JsonElement childElement) &&
            childElement.ValueKind == JsonValueKind.String)
        {
            return childElement.GetString();
        }

        return null;
    }

    /// <summary>Reads a string value from <c>data.object.metadata</c>, or null.</summary>
    public string? GetMetadataString(string key)
        => GetNestedString("metadata", key);

    /// <summary>Reads a numeric property from <see cref="DataObject"/>, or null.</summary>
    public long? GetInt64(string property)
        => DataObject.ValueKind == JsonValueKind.Object &&
           DataObject.TryGetProperty(property, out JsonElement element) &&
           element.ValueKind == JsonValueKind.Number &&
           element.TryGetInt64(out long value)
            ? value
            : null;

    /// <summary>
    /// Returns true when the event includes an expanded Checkout line item whose
    /// price id or lookup key matches the supplied launch price.
    /// </summary>
    public bool HasLineItemPrice(string expectedPriceId, string expectedLookupKey)
    {
        if (DataObject.ValueKind != JsonValueKind.Object ||
            !DataObject.TryGetProperty("line_items", out JsonElement lineItems) ||
            lineItems.ValueKind != JsonValueKind.Object ||
            !lineItems.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("price", out JsonElement price) ||
                price.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (StringPropertyEquals(price, "id", expectedPriceId) ||
                StringPropertyEquals(price, "lookup_key", expectedLookupKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StringPropertyEquals(JsonElement element, string property, string expected)
        => !string.IsNullOrWhiteSpace(expected) &&
           element.TryGetProperty(property, out JsonElement value) &&
           value.ValueKind == JsonValueKind.String &&
           string.Equals(value.GetString(), expected, StringComparison.Ordinal);
}

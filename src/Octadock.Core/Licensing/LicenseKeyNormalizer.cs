using System.Text;

namespace Octadock.Core.Licensing;

/// <summary>
/// Normalizes a user-pasted license key to the canonical
/// <c>OCTA-XXXXX-XXXXX-XXXXX-XXXXX</c> form (WS5): tolerant of surrounding
/// whitespace, lower case, and missing/extra dashes or spaces between groups.
/// A well-formed key always normalizes to the exact string the service issued,
/// so activation matches regardless of how the buyer copied it.
/// </summary>
public static class LicenseKeyNormalizer
{
    private const int BodyLength = 20; // 4 groups × 5 chars
    private const string Prefix = "OCTA";

    /// <summary>Returns the canonical key, or the compacted upper-case input if the shape is unknown.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var compact = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                compact.Append(char.ToUpperInvariant(c));
            }
        }

        string body = compact.ToString();
        if (body.Length == Prefix.Length + BodyLength &&
            body.StartsWith(Prefix, StringComparison.Ordinal))
        {
            string groups = body[Prefix.Length..];
            return $"{Prefix}-{groups[..5]}-{groups[5..10]}-{groups[10..15]}-{groups[15..]}";
        }

        // Unknown shape: hand back the compacted upper-case form. The service rejects
        // it as not-found, and the UI surfaces "key not recognized".
        return body;
    }

    /// <summary>True when the normalized key has the canonical Octadock shape.</summary>
    public static bool IsWellFormed(string? raw)
    {
        string normalized = Normalize(raw);
        if (normalized.Length != Prefix.Length + BodyLength + 4)
        {
            return false;
        }

        return normalized.StartsWith(Prefix + "-", StringComparison.Ordinal);
    }
}

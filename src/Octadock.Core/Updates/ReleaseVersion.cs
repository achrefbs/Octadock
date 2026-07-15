using System.Globalization;

namespace Octadock.Core.Updates;

/// <summary>
/// A comparable app version parsed from <c>MAJOR.MINOR.PATCH[-prerelease]</c> (WS1
/// update check). Follows the SemVer precedence rule that a release outranks a
/// pre-release of the same core version (so 0.2.0 &gt; 0.2.0-alpha.0), which is what
/// the update check needs to avoid offering a downgrade.
/// </summary>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<ReleaseVersion>
{
    /// <summary>Parses a version string; tolerates a missing patch and build metadata (<c>+…</c>).</summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string core = text.Trim();

        int plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            core = core[..plus];
        }

        string? pre = null;
        int dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            pre = core[(dash + 1)..];
            core = core[..dash];
        }

        string[] parts = core.Split('.');
        if (parts.Length == 0 || !TryPart(parts, 0, out int major))
        {
            return false;
        }

        if (!TryPart(parts, 1, out int minor) || !TryPart(parts, 2, out int patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, string.IsNullOrEmpty(pre) ? null : pre);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(ReleaseVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        // A release (no pre-release tag) outranks a pre-release of the same core.
        bool thisPre = PreRelease is not null;
        bool otherPre = other.PreRelease is not null;
        if (thisPre != otherPre)
        {
            return thisPre ? -1 : 1;
        }

        return thisPre ? string.CompareOrdinal(PreRelease, other.PreRelease) : 0;
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right)
        => left.CompareTo(right) < 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right)
        => left.CompareTo(right) <= 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right)
        => left.CompareTo(right) > 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right)
        => left.CompareTo(right) >= 0;

    public override string ToString()
        => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    private static bool TryPart(string[] parts, int index, out int value)
    {
        if (index >= parts.Length)
        {
            value = 0;
            return true; // absent minor/patch defaults to 0
        }

        return int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}

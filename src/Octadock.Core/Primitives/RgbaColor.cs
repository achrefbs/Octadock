using System.Globalization;

namespace Octadock.Core.Primitives;

/// <summary>
/// A straight (non-premultiplied) 8-bit-per-channel RGBA color. Used by the
/// annotation model so styles serialize to portable <c>#RRGGBB</c> /
/// <c>#RRGGBBAA</c> hex strings independent of any UI framework's color type.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbaColor Transparent = new(0, 0, 0, 0);
    public static readonly RgbaColor Black = new(0, 0, 0);
    public static readonly RgbaColor White = new(255, 255, 255);

    /// <summary>Octadock's default accent (a calm blue distinct from any cloned brand).</summary>
    public static readonly RgbaColor Accent = new(0x2F, 0x6F, 0xED);

    /// <summary>Opacity in the range 0..1.</summary>
    public double Opacity => A / 255.0;

    /// <summary>Returns a copy with the given 0..1 opacity applied to the alpha channel.</summary>
    public RgbaColor WithOpacity(double opacity)
    {
        double clamped = Math.Clamp(opacity, 0.0, 1.0);
        return this with { A = (byte)Math.Round(clamped * 255.0) };
    }

    /// <summary>Formats as <c>#RRGGBB</c> when fully opaque, otherwise <c>#RRGGBBAA</c>.</summary>
    public string ToHex()
        => A == 255
            ? $"#{R:X2}{G:X2}{B:X2}"
            : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public override string ToString() => ToHex();

    /// <summary>
    /// Parses <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>
    /// (leading '#' optional). Throws <see cref="FormatException"/> on failure.
    /// </summary>
    public static RgbaColor Parse(string hex)
        => TryParse(hex, out RgbaColor color)
            ? color
            : throw new FormatException($"'{hex}' is not a valid RGBA hex color.");

    public static bool TryParse(string? hex, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        ReadOnlySpan<char> s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
        {
            s = s[1..];
        }

        switch (s.Length)
        {
            case 3: // RGB
                return TryNibbles(s, out color, hasAlpha: false, doubled: true);
            case 4: // RGBA
                return TryNibbles(s, out color, hasAlpha: true, doubled: true);
            case 6: // RRGGBB
                return TryBytes(s, out color, hasAlpha: false);
            case 8: // RRGGBBAA
                return TryBytes(s, out color, hasAlpha: true);
            default:
                return false;
        }
    }

    private static bool TryNibbles(ReadOnlySpan<char> s, out RgbaColor color, bool hasAlpha, bool doubled)
    {
        color = default;
        Span<byte> vals = stackalloc byte[4] { 0, 0, 0, 255 };
        int count = hasAlpha ? 4 : 3;
        for (int i = 0; i < count; i++)
        {
            if (!TryHexDigit(s[i], out int n))
            {
                return false;
            }

            vals[i] = (byte)(doubled ? (n * 16) + n : n);
        }

        color = new RgbaColor(vals[0], vals[1], vals[2], vals[3]);
        return true;
    }

    private static bool TryBytes(ReadOnlySpan<char> s, out RgbaColor color, bool hasAlpha)
    {
        color = default;
        Span<byte> vals = stackalloc byte[4] { 0, 0, 0, 255 };
        int count = hasAlpha ? 4 : 3;
        for (int i = 0; i < count; i++)
        {
            if (!byte.TryParse(s.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return false;
            }

            vals[i] = b;
        }

        color = new RgbaColor(vals[0], vals[1], vals[2], vals[3]);
        return true;
    }

    private static bool TryHexDigit(char c, out int value)
    {
        if (c is >= '0' and <= '9')
        {
            value = c - '0';
            return true;
        }

        if (c is >= 'a' and <= 'f')
        {
            value = 10 + (c - 'a');
            return true;
        }

        if (c is >= 'A' and <= 'F')
        {
            value = 10 + (c - 'A');
            return true;
        }

        value = 0;
        return false;
    }
}

namespace Octadock.Core.Capture;

/// <summary>
/// A platform-neutral wrapper over a native window handle (HWND on Windows).
/// Keeping this in Core lets capture abstractions reference windows without the
/// domain taking a dependency on Win32 types.
/// </summary>
public readonly record struct WindowHandle(long Value)
{
    public static readonly WindowHandle None = new(0);

    public bool IsValid => Value != 0;

    /// <summary>Parses a hexadecimal handle string (with or without a 0x prefix).</summary>
    public static bool TryParseHex(string? text, out WindowHandle handle)
    {
        handle = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> s = text.AsSpan().Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            s = s[2..];
        }

        if (long.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long value))
        {
            handle = new WindowHandle(value);
            return true;
        }

        return false;
    }

    public string ToHex() => $"0x{Value:X}";

    public override string ToString() => ToHex();

    public static explicit operator nint(WindowHandle handle) => (nint)handle.Value;

    public static explicit operator WindowHandle(nint handle) => new(handle);
}

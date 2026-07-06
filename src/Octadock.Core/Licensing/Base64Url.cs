namespace Octadock.Core.Licensing;

/// <summary>
/// URL-safe, unpadded Base64 (RFC 4648 §5). Mirrors the license service's encoder
/// exactly — client and server must agree byte-for-byte on the entitlement wire
/// format (see docs ENTITLEMENT_ENVELOPE.md).
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        string s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(s);
    }
}

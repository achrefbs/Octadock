namespace Octadock.LicenseService.Licensing;

/// <summary>URL-safe, unpadded Base64 (RFC 4648 §5) used for envelope fields.</summary>
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

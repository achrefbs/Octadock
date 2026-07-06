using System.Security.Cryptography;

namespace Octadock.LicenseService.Licensing;

/// <summary>
/// Produces human-legible, cryptographically-random license keys of the form
/// <c>OCTA-XXXXX-XXXXX-XXXXX-XXXXX</c> (~98 bits). The alphabet omits easily
/// confused characters (0/O, 1/I/L) so keys are safe to read aloud and retype.
/// </summary>
public static class LicenseKeyGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // 30 symbols, no 0/O/1/I/L
    private const int Groups = 4;
    private const int GroupLength = 5;

    public static string New()
    {
        Span<byte> random = stackalloc byte[Groups * GroupLength];
        RandomNumberGenerator.Fill(random);

        Span<char> buffer = stackalloc char[4 + Groups * (GroupLength + 1)];
        int pos = 0;
        "OCTA".AsSpan().CopyTo(buffer);
        pos += 4;

        int r = 0;
        for (int g = 0; g < Groups; g++)
        {
            buffer[pos++] = '-';
            for (int i = 0; i < GroupLength; i++)
            {
                buffer[pos++] = Alphabet[random[r++] % Alphabet.Length];
            }
        }

        return new string(buffer);
    }
}

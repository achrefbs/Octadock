using System.Security.Cryptography;
using System.Text;

namespace Octadock.Core.Licensing;

/// <summary>
/// FROZEN machine-identity hashing for the "3 devices" policy (WS4, R13). Mirrors
/// the license service exactly so client and server agree byte-for-byte.
/// v1: <c>lowercasehex( SHA-256( utf8( lowercase(trim(machineGuid)) ) ) )</c>.
/// The Windows platform layer reads HKLM MachineGuid and calls <see cref="Compute"/>.
/// </summary>
public static class MachineHash
{
    /// <summary>Current machine-hash algorithm version (device_hash_v).</summary>
    public const int CurrentVersion = 1;

    public static string Compute(string machineGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineGuid);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

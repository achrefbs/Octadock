using System.Security.Cryptography;
using System.Text;

namespace Octadock.LicenseService.Licensing;

/// <summary>
/// FROZEN machine-identity hashing for the "3 devices" policy (WS4, R13). The
/// desktop client reads the platform machine id (Windows: HKLM\SOFTWARE\Microsoft\
/// Cryptography\MachineGuid) and reports <see cref="Compute"/> of it; the service
/// stores that hash. Defining the algorithm here keeps client and service byte-for-
/// byte identical. <see cref="CurrentVersion"/> (device_hash_v) versions the algorithm
/// so it can evolve later without invalidating existing device slots.
///
/// Algorithm v1: <c>lowercasehex( SHA-256( utf8( lowercase(trim(machineGuid)) ) ) )</c>.
/// </summary>
public static class MachineHash
{
    /// <summary>Current machine-hash algorithm version (device_hash_v).</summary>
    public const int CurrentVersion = 1;

    /// <summary>Computes the v1 machine hash for a platform machine id.</summary>
    public static string Compute(string machineGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(machineGuid);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

using System.Runtime.Versioning;
using Microsoft.Win32;
using Octadock.Core.Abstractions;
using CoreMachineHash = Octadock.Core.Licensing.MachineHash;

namespace Octadock.Platform.Windows.System;

/// <summary>
/// Windows <see cref="IMachineIdentity"/>: hashes <c>HKLM\SOFTWARE\Microsoft\
/// Cryptography\MachineGuid</c> (the same value across reinstalls of the same OS
/// image, stable per machine). Computed once and cached. Falls back to the machine
/// name only if the registry value is unexpectedly absent.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineIdentity : IMachineIdentity
{
    private readonly Lazy<string> _hash = new(ComputeHash);

    /// <inheritdoc />
    public string MachineHash => _hash.Value;

    /// <inheritdoc />
    public int DeviceHashVersion => CoreMachineHash.CurrentVersion;

    private static string ComputeHash()
    {
        string? machineGuid = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;

        if (string.IsNullOrWhiteSpace(machineGuid))
        {
            // Extremely rare; keep licensing functional with a stable-enough fallback.
            machineGuid = Environment.MachineName;
        }

        return CoreMachineHash.Compute(machineGuid);
    }
}

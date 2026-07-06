namespace Octadock.Core.Abstractions;

/// <summary>
/// This machine's stable identity for device-limited licensing (WS4, R13). The hash
/// is computed by <c>Octadock.Core.Licensing.MachineHash</c> over the platform machine
/// id (Windows: HKLM MachineGuid); the platform layer supplies that id.
/// </summary>
public interface IMachineIdentity
{
    /// <summary>The device hash reported at activation and stored in the entitlement.</summary>
    string MachineHash { get; }

    /// <summary>The machine-hash algorithm version (device_hash_v).</summary>
    int DeviceHashVersion { get; }
}

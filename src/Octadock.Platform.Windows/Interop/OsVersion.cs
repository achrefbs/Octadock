namespace Octadock.Platform.Windows.Interop;

/// <summary>
/// Reports the running Windows build number via <c>RtlGetVersion</c>, which is
/// accurate regardless of the application's compatibility manifest.
/// </summary>
internal static class OsVersion
{
    /// <summary>Windows 10 20H1 (2004) build; the minimum for WGC + capture exclusion.</summary>
    public const uint Build2004 = 19041;

    private static readonly Lazy<uint> BuildNumberLazy = new(QueryBuildNumber);

    /// <summary>The current OS build number (for example 19045, 22631).</summary>
    public static uint BuildNumber => BuildNumberLazy.Value;

    /// <summary>True when the OS build supports full <c>WDA_EXCLUDEFROMCAPTURE</c> and WGC.</summary>
    public static bool SupportsCaptureExclusion => BuildNumber >= Build2004;

    private static uint QueryBuildNumber()
    {
        var info = new OSVERSIONINFOEXW
        {
            OSVersionInfoSize = (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<OSVERSIONINFOEXW>(),
        };

        // RtlGetVersion returns STATUS_SUCCESS (0) and is documented to always succeed.
        if (Ntdll.RtlGetVersion(ref info) == 0)
        {
            return info.BuildNumber;
        }

        // Fall back to the (manifest-dependent) managed value.
        return (uint)Environment.OSVersion.Version.Build;
    }
}

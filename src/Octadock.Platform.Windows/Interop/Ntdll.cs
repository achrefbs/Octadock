using System.Runtime.InteropServices;

namespace Octadock.Platform.Windows.Interop;

/// <summary>
/// P/Invoke for <c>ntdll.dll</c>. <c>RtlGetVersion</c> reports the true OS build
/// number (unlike <c>GetVersionEx</c>, which lies without a manifest entry).
/// </summary>
internal static partial class Ntdll
{
    private const string Dll = "ntdll.dll";

    // DllImport: OSVERSIONINFOEXW contains a ByValTStr string (non-blittable).
    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "RtlGetVersion")]
    public static extern int RtlGetVersion(ref OSVERSIONINFOEXW versionInfo);
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Octadock.Platform.Windows.Interop;

/// <summary>P/Invoke declarations for <c>shcore.dll</c> (per-monitor DPI).</summary>
[SupportedOSPlatform("windows")]
internal static partial class Shcore
{
    private const string Dll = "shcore.dll";

    /// <summary>
    /// Retrieves the DPI of a monitor. Returns S_OK (0) on success and writes the
    /// horizontal/vertical DPI (typically equal) into the out params.
    /// </summary>
    [LibraryImport(Dll)]
    public static partial int GetDpiForMonitor(nint hmonitor, MONITOR_DPI_TYPE dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport(Dll)]
    public static partial int GetScaleFactorForMonitor(nint hMon, out DEVICE_SCALE_FACTOR pScale);
}

/// <summary>
/// P/Invoke for <c>user32.dll</c> process-DPI-awareness context APIs. Declared
/// separately because the entry points live in user32 (Win10 1607+ / 1703+).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class DpiApi
{
    private const string Dll = "user32.dll";

    /// <summary>
    /// Sets the process DPI awareness context. Returns non-zero on success.
    /// Available on Windows 10 1703+ (the app's minimum is 20H1).
    /// </summary>
    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint value);

    /// <summary>The <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c> pseudo-handle value.</summary>
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    /// <summary>The <c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE</c> pseudo-handle value.</summary>
    public static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = -3;
}

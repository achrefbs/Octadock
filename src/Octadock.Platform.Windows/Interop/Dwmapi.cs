using System.Runtime.InteropServices;

namespace Octadock.Platform.Windows.Interop;

/// <summary>P/Invoke declarations for <c>dwmapi.dll</c>.</summary>
internal static partial class Dwmapi
{
    private const string Dll = "dwmapi.dll";

    /// <summary>
    /// Retrieves a window attribute. Used for <c>DWMWA_EXTENDED_FRAME_BOUNDS</c>
    /// (true DWM frame, excluding the invisible resize border) and
    /// <c>DWMWA_CLOAKED</c> (whether the window is hidden by the shell/UWP).
    /// </summary>
    [LibraryImport(Dll)]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out RECT pvAttribute, uint cbAttribute);

    [LibraryImport(Dll)]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, uint cbAttribute);
}

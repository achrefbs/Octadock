using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The small Win32 surface the Capture UX needs beyond WPF: positioning
/// chrome-less windows in <em>physical</em> pixels (WPF's <c>Left</c>/<c>Top</c>
/// are DIPs on the primary monitor and are unreliable across mixed-DPI monitors),
/// reading the live cursor position on the virtual desktop, and nudging a window
/// into the no-activate / tool-window band so overlays never steal focus from the
/// app being captured.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    public static readonly IntPtr HwndTopmost = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int index, int newLong);

    /// <summary>Reads the current cursor position in physical pixels on the virtual desktop.</summary>
    public static Octadock.Core.Geometry.PixelPoint GetCursorPixel()
    {
        return GetCursorPos(out Point p)
            ? new Octadock.Core.Geometry.PixelPoint(p.X, p.Y)
            : Octadock.Core.Geometry.PixelPoint.Zero;
    }

    /// <summary>
    /// Positions a window at an exact physical-pixel rectangle without activating
    /// it or changing its z-order band.
    /// </summary>
    public static void PositionPhysical(IntPtr hwnd, Octadock.Core.Geometry.PixelRect bounds)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpShowWindow | SwpNoZOrder);
    }

    /// <summary>
    /// Positions a temporary focus-taking surface in physical pixels and promotes
    /// it to the topmost band.
    /// </summary>
    public static void PositionPhysicalTopmost(IntPtr hwnd, Octadock.Core.Geometry.PixelRect bounds)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            HwndTopmost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpShowWindow);
    }
    /// <summary>
    /// Moves a window's top-left to an exact physical-pixel point without resizing it
    /// (used for <c>SizeToContent</c> windows whose size WPF owns) or changing z-order.
    /// </summary>
    public static void MovePhysical(IntPtr hwnd, int x, int y)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            HwndTopmost,
            x,
            y,
            0,
            0,
            SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    /// <summary>Adds the tool-window + no-activate extended styles so the window never takes focus.</summary>
    public static void MakeNoActivateToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int style = GetWindowLong(hwnd, GwlExStyle);
        _ = SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WinRect rect);

    /// <summary>
    /// The window's current rectangle in physical screen pixels, or an empty
    /// rect when it cannot be read. Used to VERIFY mixed-DPI placement instead
    /// of trusting WPF's DIP-space properties.
    /// </summary>
    public static Octadock.Core.Geometry.PixelRect GetPhysicalWindowRect(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out WinRect r))
        {
            return new Octadock.Core.Geometry.PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        return default;
    }
}

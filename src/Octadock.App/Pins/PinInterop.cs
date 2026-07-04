using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Octadock.App.Pins;

/// <summary>
/// The small Win32 surface floating pins need beyond WPF: toggling the
/// click-through (transparent) extended-window-style bits so, when a pin is
/// locked, mouse input passes through to the application beneath it. Uses the
/// pointer-sized <c>GetWindowLongPtr</c>/<c>SetWindowLongPtr</c> so it is correct
/// on 64-bit Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PinInterop
{
    public const int GwlExStyle = -20;

    /// <summary>Excludes the window from hit-testing; clicks fall through to windows below.</summary>
    public const long WsExTransparent = 0x00000020;

    /// <summary>Required alongside <see cref="WsExTransparent"/> for reliable click-through.</summary>
    public const long WsExLayered = 0x00080000;

    /// <summary>Keeps the pin out of Alt-Tab.</summary>
    public const long WsExToolWindow = 0x00000080;

    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;

    /// <summary>Adds or removes the click-through extended styles on the window.</summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style = enabled
            ? style | WsExTransparent | WsExLayered
            : style & ~WsExTransparent; // keep WS_EX_LAYERED so opacity keeps working

        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
    }

    /// <summary>Ensures the window carries the layered + tool-window styles used by pins.</summary>
    public static void EnsureBaseStyles(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExLayered | WsExToolWindow));
    }

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
            SwpNoZOrder | SwpNoActivate | SwpShowWindow);
    }

    public static Octadock.Core.Geometry.PixelRect? GetWindowBounds(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rect))
        {
            return null;
        }

        return Octadock.Core.Geometry.PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    // GetWindowLongPtr / SetWindowLongPtr are only exported by that name in the
    // 64-bit user32; on 32-bit they fall back to GetWindowLong. These wrappers pick
    // the right export based on the process pointer size.

    public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

    public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

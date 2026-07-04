using System.Runtime.InteropServices;

namespace Octadock.Platform.Windows.Interop;

/// <summary>P/Invoke declarations for <c>gdi32.dll</c> (GDI capture fallback).</summary>
internal static partial class Gdi32
{
    private const string Dll = "gdi32.dll";

    [LibraryImport(Dll, SetLastError = true)]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport(Dll, SetLastError = true)]
    public static partial nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    /// <summary>
    /// Creates a DIB section. <paramref name="ppvBits"/> receives a pointer to the
    /// raw pixel buffer so the caller can read the captured pixels directly.
    /// </summary>
    [DllImport(Dll, SetLastError = true, EntryPoint = "CreateDIBSection")]
    public static extern nint CreateDIBSection(
        nint hdc,
        ref BITMAPINFO pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset);

    [LibraryImport(Dll)]
    public static partial nint SelectObject(nint hdc, nint hgdiobj);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(
        nint hdc,
        int x,
        int y,
        int cx,
        int cy,
        nint hdcSrc,
        int x1,
        int y1,
        uint rop);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);
}

using System.Runtime.InteropServices;

namespace Octadock.Platform.Windows.Interop;

/// <summary>The Win32 <c>RECT</c> structure (left, top, right, bottom edges).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}

/// <summary>The Win32 <c>POINT</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>The Win32 <c>SIZE</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int Cx;
    public int Cy;
}

/// <summary>The Win32 <c>MSG</c> structure used by the message pump.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint Hwnd;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public POINT Pt;
    public uint LPrivate;
}

/// <summary>
/// <c>MONITORINFOEXW</c>. The fixed 32-char <c>szDevice</c> buffer carries the
/// device name (e.g. <c>\\.\DISPLAY1</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEXW
{
    public int CbSize;
    public RECT RcMonitor;
    public RECT RcWork;
    public uint DwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string SzDevice;
}

/// <summary>The Win32 <c>WNDCLASSEXW</c> structure for registering a window class.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WNDCLASSEXW
{
    public uint CbSize;
    public uint Style;
    public nint LpfnWndProc;
    public int CbClsExtra;
    public int CbWndExtra;
    public nint HInstance;
    public nint HIcon;
    public nint HCursor;
    public nint HbrBackground;
    public nint LpszMenuName;
    public nint LpszClassName;
    public nint HIconSm;
}

/// <summary>The Win32 <c>CURSORINFO</c> structure for querying the cursor.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CURSORINFO
{
    public int CbSize;
    public int Flags;
    public nint HCursor;
    public POINT PtScreenPos;
}

/// <summary>The Win32 <c>ICONINFO</c> structure returned by <c>GetIconInfo</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    [MarshalAs(UnmanagedType.Bool)]
    public bool FIcon;
    public int XHotspot;
    public int YHotspot;
    public nint HbmMask;
    public nint HbmColor;
}

/// <summary>The Win32 <c>BITMAPINFOHEADER</c> used with <c>CreateDIBSection</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint BiSize;
    public int BiWidth;
    public int BiHeight;
    public ushort BiPlanes;
    public ushort BiBitCount;
    public uint BiCompression;
    public uint BiSizeImage;
    public int BiXPelsPerMeter;
    public int BiYPelsPerMeter;
    public uint BiClrUsed;
    public uint BiClrImportant;
}

/// <summary>Full <c>BITMAPINFO</c> (header plus a single palette placeholder).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER BmiHeader;
    public uint BmiColors;
}

/// <summary>The Win32 <c>OSVERSIONINFOEXW</c> for <c>RtlGetVersion</c>.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct OSVERSIONINFOEXW
{
    public uint OSVersionInfoSize;
    public uint MajorVersion;
    public uint MinorVersion;
    public uint BuildNumber;
    public uint PlatformId;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string CSDVersion;

    public ushort ServicePackMajor;
    public ushort ServicePackMinor;
    public ushort SuiteMask;
    public byte ProductType;
    public byte Reserved;
}

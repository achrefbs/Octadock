using System.Runtime.InteropServices;

namespace Octadock.Platform.Windows.Interop;

/// <summary>Callback for <c>EnumWindows</c>. Return false to stop enumeration.</summary>
internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

/// <summary>Callback for <c>EnumDisplayMonitors</c>. Return false to stop enumeration.</summary>
internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT lprcMonitor, nint dwData);

/// <summary>Window procedure signature for the message-only window.</summary>
internal delegate nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam);

/// <summary>P/Invoke declarations for <c>user32.dll</c>.</summary>
internal static partial class User32
{
    private const string Dll = "user32.dll";

    // ----- Hotkeys -----

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    // ----- Foreground / cursor -----

    [LibraryImport(Dll)]
    public static partial nint GetForegroundWindow();

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorInfo(ref CURSORINFO pci);

    // ----- Window enumeration / properties -----

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    // Uses classic DllImport: LibraryImport requires an explicit element-count
    // marshalling annotation for [Out] arrays, which DllImport infers from length.
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetWindowTextW")]
    public static extern int GetWindowText(nint hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport(Dll, EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    public static partial int GetWindowTextLength(nint hWnd);

    [LibraryImport(Dll, SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    // GetWindowLongPtr / SetWindowLongPtr are only exported under those names in
    // 64-bit user32; on 32-bit they map to GetWindowLong/SetWindowLong. .NET
    // desktop targets are effectively 64-bit here, but keep the *Ptr entry points.

    [LibraryImport(Dll, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport(Dll, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    // ----- Capture exclusion -----

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowDisplayAffinity(nint hWnd, out uint pdwAffinity);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    // ----- Monitors -----

    [LibraryImport(Dll)]
    public static partial nint MonitorFromWindow(nint hWnd, uint dwFlags);

    [LibraryImport(Dll)]
    public static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    // DllImport: MONITORINFOEXW contains a ByValTStr string (non-blittable), which
    // LibraryImport cannot marshal.
    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEXW lpmi);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    // ----- Device contexts -----

    [LibraryImport(Dll)]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport(Dll)]
    public static partial nint GetWindowDC(nint hWnd);

    [LibraryImport(Dll)]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    // ----- Icons / cursors -----

    // DllImport: ICONINFO contains a BOOL field (non-blittable for LibraryImport).
    [DllImport(Dll, SetLastError = true, EntryPoint = "GetIconInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetIconInfo(nint hIcon, out ICONINFO piconinfo);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(
        nint hdc,
        int xLeft,
        int yTop,
        nint hIcon,
        int cxWidth,
        int cyWidth,
        uint istepIfAniCur,
        nint hbrFlickerFreeDraw,
        uint diFlags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    // ----- PrintWindow (window capture fallback) -----

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    // ----- Message-only window plumbing -----

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
    public static extern ushort RegisterClassEx(ref WNDCLASSEXW lpwcx);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClass(nint lpClassName, nint hInstance);

    [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    public static extern nint CreateWindowEx(
        int dwExStyle,
        nint lpClassName,
        string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    public static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    // ----- Message pump -----

    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    public static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [DllImport(Dll, CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    public static extern nint DispatchMessage(ref MSG lpMsg);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);
}

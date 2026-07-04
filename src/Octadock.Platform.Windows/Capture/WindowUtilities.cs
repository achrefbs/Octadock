using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Capture;

/// <summary>
/// Shared Win32 window helpers used by the window picker, capture engine and
/// capture-source metadata builder.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowUtilities
{
    /// <summary>Reads a window's title, returning an empty string when it has none.</summary>
    public static string GetWindowTitle(nint hwnd)
    {
        int length = User32.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        char[] buffer = new char[length + 1];
        int copied = User32.GetWindowText(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    /// <summary>Returns the process image file name (e.g. <c>chrome.exe</c>) owning the window.</summary>
    public static string GetProcessName(nint hwnd)
    {
        _ = User32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return string.Empty;
        }

        nint handle = Kernel32.OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == nint.Zero)
        {
            return string.Empty;
        }

        try
        {
            uint capacity = 1024;
            char[] buffer = new char[capacity];
            if (Kernel32.QueryFullProcessImageName(handle, 0, buffer, ref capacity) && capacity > 0)
            {
                string fullPath = new(buffer, 0, (int)capacity);
                return Path.GetFileName(fullPath);
            }
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the window's on-screen bounds in physical pixels. Prefers the DWM
    /// extended frame bounds (excludes the invisible resize border) and falls back
    /// to <c>GetWindowRect</c>.
    /// </summary>
    public static PixelRect GetWindowBounds(nint hwnd, bool includeShadow = true)
    {
        if (!includeShadow &&
            Dwmapi.DwmGetWindowAttribute(
                hwnd,
                (uint)DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT frame,
                (uint)Marshal.SizeOf<RECT>()) == 0)
        {
            return PixelRect.FromEdges(frame.Left, frame.Top, frame.Right, frame.Bottom);
        }

        return User32.GetWindowRect(hwnd, out RECT rect)
            ? PixelRect.FromEdges(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : PixelRect.Empty;
    }

    /// <summary>True when the window is cloaked (hidden by the shell, e.g. inactive UWP).</summary>
    public static bool IsCloaked(nint hwnd)
    {
        int hr = Dwmapi.DwmGetWindowAttribute(
            hwnd,
            (uint)DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    /// <summary>True when the window has the <c>WS_EX_TOOLWINDOW</c> extended style.</summary>
    public static bool IsToolWindow(nint hwnd)
    {
        long exStyle = User32.GetWindowLongPtr(hwnd, NativeConstants.GWL_EXSTYLE).ToInt64();
        return (exStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0;
    }

    /// <summary>
    /// Builds capture-source provenance for a window: process name, title and a
    /// privacy-preserving SHA-256 prefix hash of the HWND value.
    /// </summary>
    public static CaptureSource BuildSource(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return CaptureSource.Empty;
        }

        string process = GetProcessName(hwnd);
        string title = GetWindowTitle(hwnd);
        string hash = HashHandle(hwnd);
        return new CaptureSource(
            string.IsNullOrEmpty(process) ? null : process,
            string.IsNullOrEmpty(title) ? null : title,
            hash);
    }

    /// <summary>Returns a short SHA-256 hex prefix of the handle value.</summary>
    public static string HashHandle(nint hwnd)
    {
        long value = hwnd.ToInt64();
        byte[] bytes = BitConverter.GetBytes(value);
        byte[] digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest, 0, 8);
    }

    /// <summary>Resolves capture-source provenance for the current foreground window.</summary>
    public static CaptureSource BuildForegroundSource()
    {
        nint hwnd = User32.GetForegroundWindow();
        return BuildSource(hwnd);
    }
}

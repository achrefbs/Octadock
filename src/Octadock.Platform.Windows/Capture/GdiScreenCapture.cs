using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Octadock.Core.Geometry;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Capture;

/// <summary>
/// Low-level GDI screen grabber. Creates a top-down 32bpp DIB section, BitBlts a
/// region of the virtual desktop (or PrintWindows a specific window) into it and
/// returns straight BGRA bytes. Reliable across every Windows build; used as the
/// default working path and as the WGC fallback.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GdiScreenCapture
{
    /// <summary>
    /// Captures a rectangle of the virtual desktop into a fresh BGRA buffer.
    /// <paramref name="region"/> is in physical pixels on the virtual desktop.
    /// </summary>
    public static byte[] CaptureRegion(PixelRect region, bool includeCursor, out int stride)
    {
        stride = region.Width * 4;
        if (region.Width <= 0 || region.Height <= 0)
        {
            return [];
        }

        nint screenDc = User32.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            return [];
        }

        nint memDc = nint.Zero;
        nint dib = nint.Zero;
        nint oldObj = nint.Zero;
        try
        {
            memDc = Gdi32.CreateCompatibleDC(screenDc);
            if (memDc == nint.Zero)
            {
                return [];
            }

            dib = CreateTopDownDib(memDc, region.Width, region.Height, out nint bits);
            if (dib == nint.Zero || bits == nint.Zero)
            {
                return [];
            }

            oldObj = Gdi32.SelectObject(memDc, dib);

            // CAPTUREBLT includes layered windows; SRCCOPY copies pixels verbatim.
            bool ok = Gdi32.BitBlt(
                memDc,
                0,
                0,
                region.Width,
                region.Height,
                screenDc,
                region.X,
                region.Y,
                NativeConstants.SRCCOPY | NativeConstants.CAPTUREBLT);
            if (!ok)
            {
                return [];
            }

            if (includeCursor)
            {
                DrawCursor(memDc, region);
            }

            return CopyBits(bits, region.Width, region.Height, stride);
        }
        finally
        {
            if (memDc != nint.Zero && oldObj != nint.Zero)
            {
                Gdi32.SelectObject(memDc, oldObj);
            }

            if (dib != nint.Zero)
            {
                Gdi32.DeleteObject(dib);
            }

            if (memDc != nint.Zero)
            {
                Gdi32.DeleteDC(memDc);
            }

            User32.ReleaseDC(nint.Zero, screenDc);
        }
    }

    /// <summary>
    /// Captures a single window using <c>PrintWindow(PW_RENDERFULLCONTENT)</c>,
    /// which renders occluded and DirectComposition content on Windows 8.1+.
    /// </summary>
    public static byte[] CaptureWindow(
        nint hwnd,
        PixelRect bounds,
        bool includeCursor,
        out int width,
        out int height,
        out int stride)
    {
        width = bounds.Width;
        height = bounds.Height;
        stride = width * 4;
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        nint screenDc = User32.GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            return [];
        }

        nint memDc = nint.Zero;
        nint dib = nint.Zero;
        nint oldObj = nint.Zero;
        try
        {
            memDc = Gdi32.CreateCompatibleDC(screenDc);
            if (memDc == nint.Zero)
            {
                return [];
            }

            dib = CreateTopDownDib(memDc, width, height, out nint bits);
            if (dib == nint.Zero || bits == nint.Zero)
            {
                return [];
            }

            oldObj = Gdi32.SelectObject(memDc, dib);

            bool ok = User32.PrintWindow(hwnd, memDc, NativeConstants.PW_RENDERFULLCONTENT);
            if (!ok)
            {
                // Fall back to a straight screen BitBlt at the window's location.
                ok = Gdi32.BitBlt(
                    memDc,
                    0,
                    0,
                    width,
                    height,
                    screenDc,
                    bounds.X,
                    bounds.Y,
                    NativeConstants.SRCCOPY | NativeConstants.CAPTUREBLT);
                if (!ok)
                {
                    return [];
                }
            }

            if (includeCursor)
            {
                DrawCursor(memDc, bounds);
            }

            return CopyBits(bits, width, height, stride);
        }
        finally
        {
            if (memDc != nint.Zero && oldObj != nint.Zero)
            {
                Gdi32.SelectObject(memDc, oldObj);
            }

            if (dib != nint.Zero)
            {
                Gdi32.DeleteObject(dib);
            }

            if (memDc != nint.Zero)
            {
                Gdi32.DeleteDC(memDc);
            }

            User32.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static nint CreateTopDownDib(nint dc, int width, int height, out nint bits)
    {
        var info = new BITMAPINFO
        {
            BmiHeader = new BITMAPINFOHEADER
            {
                BiSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                BiWidth = width,

                // Negative height => top-down DIB (row 0 is the top row), matching
                // CapturedFrame's top-down expectation.
                BiHeight = -height,
                BiPlanes = 1,
                BiBitCount = 32,
                BiCompression = NativeConstants.BI_RGB,
            },
        };

        return Gdi32.CreateDIBSection(dc, ref info, NativeConstants.DIB_RGB_COLORS, out bits, nint.Zero, 0);
    }

    private static byte[] CopyBits(nint bits, int width, int height, int stride)
    {
        int size = stride * height;
        byte[] buffer = new byte[size];
        Marshal.Copy(bits, buffer, 0, size);

        // A BI_RGB 32bpp DIB stores BGRX; the X (alpha) byte is undefined. Force it
        // opaque so downstream BGRA consumers treat the frame as fully visible.
        for (int i = 3; i < size; i += 4)
        {
            buffer[i] = 0xFF;
        }

        return buffer;
    }

    private static void DrawCursor(nint hdc, PixelRect region)
    {
        var ci = new CURSORINFO { CbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!User32.GetCursorInfo(ref ci) || (ci.Flags & NativeConstants.CURSOR_SHOWING) == 0 || ci.HCursor == nint.Zero)
        {
            return;
        }

        // Offset the cursor hotspot into region-local coordinates.
        int x = ci.PtScreenPos.X - region.X;
        int y = ci.PtScreenPos.Y - region.Y;

        if (User32.GetIconInfo(ci.HCursor, out ICONINFO iconInfo))
        {
            try
            {
                x -= iconInfo.XHotspot;
                y -= iconInfo.YHotspot;
            }
            finally
            {
                if (iconInfo.HbmColor != nint.Zero)
                {
                    Gdi32.DeleteObject(iconInfo.HbmColor);
                }

                if (iconInfo.HbmMask != nint.Zero)
                {
                    Gdi32.DeleteObject(iconInfo.HbmMask);
                }
            }
        }

        User32.DrawIconEx(hdc, x, y, ci.HCursor, 0, 0, 0, nint.Zero, NativeConstants.DI_NORMAL);
    }
}

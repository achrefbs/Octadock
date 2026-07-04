using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Monitors;

/// <summary>
/// Enumerates monitors and resolves the active monitor / work areas using
/// per-monitor-DPI-aware Win32 calls. All rectangles are physical pixels on the
/// virtual desktop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MonitorService : IMonitorService
{
    private readonly ILogger<MonitorService> _logger;

    /// <summary>Creates a new monitor service.</summary>
    public MonitorService(ILogger<MonitorService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler? MonitorsChanged;

    /// <summary>
    /// Signals that the display topology changed. The WPF app raises this in
    /// response to <c>WM_DISPLAYCHANGE</c>/<c>WM_DPICHANGED</c> so consumers can
    /// re-query monitors. Enumeration is always live, so no cache is invalidated.
    /// </summary>
    public void RaiseMonitorsChanged() => MonitorsChanged?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc />
    public IReadOnlyList<DisplayInfo> GetMonitors()
    {
        var monitors = new List<DisplayInfo>();
        var handles = new List<nint>();

        bool Callback(nint hMonitor, nint hdc, ref RECT lprc, nint data)
        {
            handles.Add(hMonitor);
            return true;
        }

        if (!User32.EnumDisplayMonitors(nint.Zero, nint.Zero, Callback, nint.Zero))
        {
            _logger.LogWarning("EnumDisplayMonitors failed; falling back to primary monitor only.");
        }

        for (int index = 0; index < handles.Count; index++)
        {
            if (TryBuildDisplayInfo(handles[index], index, out DisplayInfo info))
            {
                monitors.Add(info);
            }
        }

        if (monitors.Count == 0)
        {
            monitors.Add(CreateFallbackPrimary());
        }

        return monitors;
    }

    /// <inheritdoc />
    public DisplayInfo GetPrimary()
    {
        IReadOnlyList<DisplayInfo> monitors = GetMonitors();
        foreach (DisplayInfo monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }

        return monitors[0];
    }

    /// <inheritdoc />
    public DisplayInfo GetActiveMonitor()
    {
        // 1) Monitor under the cursor. For tray/HUD captures this lets users pick
        // the target screen by moving the pointer there before invoking capture.
        if (User32.GetCursorPos(out POINT cursor))
        {
            nint hMon = User32.MonitorFromPoint(cursor, NativeConstants.MONITOR_DEFAULTTONULL);
            if (hMon != nint.Zero && TryResolveHandle(hMon, out DisplayInfo fromCursor))
            {
                return fromCursor;
            }
        }

        // 2) Monitor holding the foreground window.
        nint foreground = User32.GetForegroundWindow();
        if (foreground != nint.Zero)
        {
            nint hMon = User32.MonitorFromWindow(foreground, NativeConstants.MONITOR_DEFAULTTONULL);
            if (hMon != nint.Zero && TryResolveHandle(hMon, out DisplayInfo fromWindow))
            {
                return fromWindow;
            }
        }

        // 3) Primary.
        return GetPrimary();
    }

    /// <inheritdoc />
    public DisplayInfo GetMonitorFromPoint(PixelPoint point)
    {
        var pt = new POINT(point.X, point.Y);
        nint hMon = User32.MonitorFromPoint(pt, NativeConstants.MONITOR_DEFAULTTONEAREST);
        if (hMon != nint.Zero && TryResolveHandle(hMon, out DisplayInfo info))
        {
            return info;
        }

        return GetPrimary();
    }

    /// <inheritdoc />
    public DisplayInfo? FindById(MonitorId id)
    {
        foreach (DisplayInfo monitor in GetMonitors())
        {
            if (string.Equals(monitor.Id.Value, id.Value, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public DisplayInfo? Resolve(string? monitorToken)
    {
        if (string.IsNullOrWhiteSpace(monitorToken))
        {
            return null;
        }

        IReadOnlyList<DisplayInfo> monitors = GetMonitors();
        string token = monitorToken.Trim();

        // Integer token => zero-based index into the enumeration order.
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            return index >= 0 && index < monitors.Count ? monitors[index] : null;
        }

        // Otherwise treat as a device-name id (case-insensitive).
        foreach (DisplayInfo monitor in monitors)
        {
            if (string.Equals(monitor.Id.Value, token, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(monitor.DeviceName, token, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public PixelRect VirtualDesktopBounds
    {
        get
        {
            PixelRect union = PixelRect.Empty;
            foreach (DisplayInfo monitor in GetMonitors())
            {
                union = union.Union(monitor.Bounds);
            }

            return union;
        }
    }

    private bool TryResolveHandle(nint hMonitor, out DisplayInfo info)
    {
        int index = ResolveMonitorIndex(hMonitor);
        if (TryBuildDisplayInfo(hMonitor, index, out DisplayInfo built))
        {
            IReadOnlyList<DisplayInfo> all = GetMonitors();
            for (int i = 0; i < all.Count; i++)
            {
                if (IsSameMonitor(all[i], built))
                {
                    info = all[i];
                    return true;
                }
            }

            info = built;
            return true;
        }

        info = default!;
        return false;
    }

    private static bool IsSameMonitor(DisplayInfo left, DisplayInfo right)
        => (!string.IsNullOrWhiteSpace(left.Id.Value) &&
            string.Equals(left.Id.Value, right.Id.Value, StringComparison.OrdinalIgnoreCase)) ||
           (!string.IsNullOrWhiteSpace(left.DeviceName) &&
            string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase)) ||
           (left.Bounds == right.Bounds && left.WorkArea == right.WorkArea);

    private int ResolveMonitorIndex(nint hMonitor)
    {
        var handles = new List<nint>();

        bool Callback(nint handle, nint hdc, ref RECT lprc, nint data)
        {
            handles.Add(handle);
            return true;
        }

        if (!User32.EnumDisplayMonitors(nint.Zero, nint.Zero, Callback, nint.Zero))
        {
            _logger.LogDebug("EnumDisplayMonitors failed while resolving monitor index.");
            return 0;
        }

        for (int i = 0; i < handles.Count; i++)
        {
            if (handles[i] == hMonitor)
            {
                return i;
            }
        }

        return 0;
    }

    private bool TryBuildDisplayInfo(nint hMonitor, int index, out DisplayInfo info)
    {
        var mi = new MONITORINFOEXW
        {
            CbSize = global::System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEXW>(),
        };

        if (!User32.GetMonitorInfoW(hMonitor, ref mi))
        {
            _logger.LogWarning("GetMonitorInfoW failed for monitor handle {Handle}.", hMonitor);
            info = default!;
            return false;
        }

        PixelRect bounds = ToRect(mi.RcMonitor);
        PixelRect work = ToRect(mi.RcWork);
        bool isPrimary = (mi.DwFlags & NativeConstants.MONITORINFOF_PRIMARY) != 0;
        string device = mi.SzDevice ?? string.Empty;

        double scale = GetDpiScale(hMonitor);

        info = new DisplayInfo(
            new MonitorId(device),
            index,
            bounds,
            work,
            scale,
            isPrimary,
            device);
        return true;
    }

    private double GetDpiScale(nint hMonitor)
    {
        int hr = Shcore.GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);
        if (hr == 0 && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        _logger.LogDebug("GetDpiForMonitor failed (hr=0x{Hr:X8}); assuming 96 DPI.", hr);
        return 1.0;
    }

    private static DisplayInfo CreateFallbackPrimary()
    {
        // Best-effort synthetic primary if enumeration returned nothing.
        int width = Math.Max(1, GetSystemMetricSafe(0));  // SM_CXSCREEN
        int height = Math.Max(1, GetSystemMetricSafe(1)); // SM_CYSCREEN
        var bounds = new PixelRect(0, 0, width, height);
        return new DisplayInfo(new MonitorId("\\\\.\\DISPLAY1"), 0, bounds, bounds, 1.0, true, "\\\\.\\DISPLAY1");
    }

    private static int GetSystemMetricSafe(int index)
    {
        try
        {
            return GetSystemMetrics(index);
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
    }

    [global::System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static PixelRect ToRect(RECT r) => PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}

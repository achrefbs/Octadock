using Octadock.Core.Capture;
using Octadock.Core.Geometry;

namespace Octadock.Core.Abstractions;

/// <summary>
/// Enumerates monitors and resolves the active monitor / work areas. Implemented
/// with per-monitor-DPI-aware Win32 calls; all rectangles are physical pixels.
/// </summary>
public interface IMonitorService
{
    /// <summary>All connected monitors, ordered by index.</summary>
    IReadOnlyList<DisplayInfo> GetMonitors();

    /// <summary>The primary monitor.</summary>
    DisplayInfo GetPrimary();

    /// <summary>
    /// The "active" monitor for a new capture: the one under the cursor,
    /// falling back to the one containing the foreground window, then the primary.
    /// </summary>
    DisplayInfo GetActiveMonitor();

    /// <summary>The monitor whose bounds contain the given virtual-desktop point.</summary>
    DisplayInfo GetMonitorFromPoint(PixelPoint point);

    /// <summary>Resolves a monitor by stored id, returning null when not found.</summary>
    DisplayInfo? FindById(MonitorId id);

    /// <summary>Resolves a monitor from an automation token (0-based index or id string).</summary>
    DisplayInfo? Resolve(string? monitorToken);

    /// <summary>The union of all monitor bounds (the virtual desktop).</summary>
    PixelRect VirtualDesktopBounds { get; }

    /// <summary>Raised when the display topology changes (attach/detach/resolution/DPI).</summary>
    event EventHandler? MonitorsChanged;
}

/// <summary>
/// Still-capture engine. The primary implementation uses
/// Windows.Graphics.Capture with a DXGI Desktop Duplication fallback. All
/// methods hide/ exclude Octadock's own windows before grabbing frames.
/// </summary>
public interface ICaptureEngine
{
    /// <summary>Captures a rectangular region of the virtual desktop.</summary>
    Task<CapturedFrame> CaptureAreaAsync(AreaCaptureRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures a single window by handle.</summary>
    Task<CapturedFrame> CaptureWindowAsync(WindowCaptureRequest request, CancellationToken cancellationToken = default);

    /// <summary>Captures one or all monitors.</summary>
    Task<CapturedFrame> CaptureFullscreenAsync(FullscreenCaptureRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Enumerates and hit-tests top-level windows for the window picker.</summary>
public interface IWindowPicker
{
    /// <summary>Visible, capturable top-level windows in z-order (topmost first).</summary>
    IReadOnlyList<CandidateWindow> EnumerateWindows();

    /// <summary>The topmost candidate window under a virtual-desktop point, if any.</summary>
    CandidateWindow? WindowAt(PixelPoint point);
}

/// <summary>
/// Wraps <c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c> so Octadock's
/// own overlays, shelf and pins are omitted from supported capture APIs.
/// </summary>
public interface ICaptureExclusion
{
    /// <summary>True when the OS build supports full capture exclusion (Win10 2004+).</summary>
    bool IsSupported { get; }

    /// <summary>Marks (or unmarks) a window for exclusion from screen capture.</summary>
    bool SetExcluded(WindowHandle window, bool excluded);
}

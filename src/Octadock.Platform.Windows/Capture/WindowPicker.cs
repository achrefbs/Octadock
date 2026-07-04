using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Capture;

/// <summary>
/// Enumerates and hit-tests top-level windows for the window picker.
/// <c>EnumWindows</c> yields windows in top-to-bottom z-order, so the
/// enumeration index doubles as <see cref="CandidateWindow.ZOrder"/> (0 =
/// topmost). Invisible, cloaked, tool and zero-area windows are filtered out.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowPicker : IWindowPicker
{
    private readonly IMonitorService _monitors;
    private readonly ILogger<WindowPicker> _logger;

    /// <summary>Creates the window picker.</summary>
    public WindowPicker(IMonitorService monitors, ILogger<WindowPicker> logger)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<CandidateWindow> EnumerateWindows()
    {
        var candidates = new List<CandidateWindow>();
        int zOrder = 0;

        bool Callback(nint hwnd, nint lParam)
        {
            if (TryBuildCandidate(hwnd, zOrder, out CandidateWindow candidate))
            {
                candidates.Add(candidate);
                zOrder++;
            }

            return true; // keep enumerating
        }

        if (!User32.EnumWindows(Callback, nint.Zero))
        {
            _logger.LogDebug("EnumWindows returned false (error {Error}).", global::System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        return candidates;
    }

    /// <inheritdoc />
    public CandidateWindow? WindowAt(PixelPoint point)
    {
        // Candidates are already sorted topmost-first, so the first hit wins.
        foreach (CandidateWindow candidate in EnumerateWindows())
        {
            if (!candidate.IsMinimized && candidate.Bounds.Contains(point))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryBuildCandidate(nint hwnd, int zOrder, out CandidateWindow candidate)
    {
        candidate = default!;

        if (!User32.IsWindowVisible(hwnd))
        {
            return false;
        }

        if (WindowUtilities.IsCloaked(hwnd))
        {
            return false;
        }

        if (WindowUtilities.IsToolWindow(hwnd))
        {
            return false;
        }

        string title = WindowUtilities.GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        bool minimized = User32.IsIconic(hwnd);
        PixelRect bounds = WindowUtilities.GetWindowBounds(hwnd, includeShadow: false);

        // Minimized windows report an off-screen rect; still surface them (some
        // pickers list them) but they must have had a real title. Non-minimized
        // windows must have positive area.
        if (!minimized && bounds.Area <= 0)
        {
            return false;
        }

        string process = WindowUtilities.GetProcessName(hwnd);
        MonitorId monitorId = ResolveMonitor(bounds);

        candidate = new CandidateWindow(
            new WindowHandle(hwnd.ToInt64()),
            title,
            process,
            bounds,
            monitorId,
            minimized,
            zOrder);
        return true;
    }

    private MonitorId ResolveMonitor(PixelRect bounds)
    {
        if (bounds.Area <= 0)
        {
            return _monitors.GetPrimary().Id;
        }

        DisplayInfo display = _monitors.GetMonitorFromPoint(bounds.Center);
        return display.Id;
    }
}

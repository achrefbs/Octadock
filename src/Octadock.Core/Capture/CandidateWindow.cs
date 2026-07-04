using Octadock.Core.Geometry;

namespace Octadock.Core.Capture;

/// <summary>
/// A window offered by the window picker as a capture target. The picker
/// enumerates top-level windows, filters out invisible/tool windows and
/// resolves the one under the cursor for highlighting.
/// </summary>
public sealed record CandidateWindow(
    WindowHandle Handle,
    string Title,
    string ProcessName,
    PixelRect Bounds,
    MonitorId Monitor,
    bool IsMinimized,
    int ZOrder);

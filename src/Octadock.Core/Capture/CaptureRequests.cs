using Octadock.Core.Geometry;

namespace Octadock.Core.Capture;

/// <summary>Options shared by all capture requests.</summary>
public abstract record CaptureRequestBase
{
    /// <summary>Whether to render the mouse cursor into the capture.</summary>
    public bool IncludeCursor { get; init; }
}

/// <summary>Request to capture a rectangular region of the virtual desktop.</summary>
public sealed record AreaCaptureRequest : CaptureRequestBase
{
    /// <summary>The region to capture, in physical pixels on the virtual desktop.</summary>
    public required PixelRect Region { get; init; }
}

/// <summary>Request to capture a single window by handle.</summary>
public sealed record WindowCaptureRequest : CaptureRequestBase
{
    public required WindowHandle Window { get; init; }

    /// <summary>Include the drop shadow / DWM frame around the window.</summary>
    public bool IncludeShadow { get; init; }
}

/// <summary>Request to capture one or all monitors at full resolution.</summary>
public sealed record FullscreenCaptureRequest : CaptureRequestBase
{
    /// <summary>The monitor to capture. When <c>null</c>, the active monitor is used.</summary>
    public MonitorId? Monitor { get; init; }

    /// <summary>Capture every monitor stitched into one image.</summary>
    public bool AllMonitors { get; init; }
}

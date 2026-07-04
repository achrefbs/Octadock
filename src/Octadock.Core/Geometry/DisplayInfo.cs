namespace Octadock.Core.Geometry;

/// <summary>
/// Immutable description of a connected monitor as seen by Octadock's monitor
/// service. All rectangles are in physical pixels on the virtual desktop.
/// </summary>
/// <param name="Id">Stable device identifier used in capture metadata.</param>
/// <param name="Index">Zero-based ordering index at enumeration time.</param>
/// <param name="Bounds">Full monitor bounds in physical pixels.</param>
/// <param name="WorkArea">
/// Usable area excluding the taskbar/app bars, in physical pixels. The Capture
/// Shelf anchors against this so it never sits under the taskbar.
/// </param>
/// <param name="DpiScale">Scale factor where 1.0 = 96 DPI, 1.5 = 150%.</param>
/// <param name="IsPrimary">True for the primary monitor.</param>
/// <param name="DeviceName">Human-readable device name when available.</param>
public sealed record DisplayInfo(
    MonitorId Id,
    int Index,
    PixelRect Bounds,
    PixelRect WorkArea,
    double DpiScale,
    bool IsPrimary,
    string DeviceName)
{
    /// <summary>Effective DPI (96 * scale).</summary>
    public double Dpi => 96.0 * DpiScale;

    /// <summary>Converts a DIP rectangle expressed relative to this monitor to physical pixels.</summary>
    public PixelRect ToPixels(DipRect dip)
    {
        PixelRect scaled = dip.ToPixels(DpiScale);
        return scaled.Offset(Bounds.X, Bounds.Y);
    }
}

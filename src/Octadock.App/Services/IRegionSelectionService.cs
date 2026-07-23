using Octadock.Core.Geometry;

namespace Octadock.App.Services;

/// <summary>
/// The result of an interactive selection: the chosen region in physical pixels on
/// the virtual desktop, or a cancellation.
/// </summary>
/// <param name="Confirmed">False when the user pressed Escape / dismissed the overlay.</param>
/// <param name="Region">The selected region (physical pixels) when confirmed.</param>
/// <param name="WindowHandleHex">
/// For window mode, the hex HWND of the picked window, so the coordinator can use
/// window capture rather than a region grab.
/// </param>
public readonly record struct RegionSelection(bool Confirmed, PixelRect Region, string? WindowHandleHex = null)
{
    /// <summary>A cancelled selection.</summary>
    public static readonly RegionSelection Cancelled = new(false, PixelRect.Empty);
}

/// <summary>
/// Presents the transparent selection overlay(s) so the user can draw an area, pick
/// a window, or choose a fullscreen monitor. Implemented by the Capture Shelf /
/// overlay module (<c>Octadock.App.CaptureUx</c>) and resolved optionally by the
/// capture coordinator and OCR service. When it is not registered, the coordinator
/// falls back to a sensible default (active-monitor fullscreen) so the app still
/// functions during bring-up.
/// </summary>
public interface IRegionSelectionService
{
    /// <summary>Shows the area-selection overlay and returns the chosen region.</summary>
    Task<RegionSelection> SelectAreaAsync(CancellationToken cancellationToken = default);

    /// <summary>Shows the window picker and returns the picked window (as a region + HWND).</summary>
    Task<RegionSelection> SelectWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>Hides every selection overlay immediately (called right before a capture grab).</summary>
    void HideAll();
}

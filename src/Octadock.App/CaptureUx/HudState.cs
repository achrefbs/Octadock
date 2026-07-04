using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// Small in-memory state the all-in-one HUD remembers between invocations: the last
/// committed selection rectangle (physical pixels) and its aspect ratio, plus the
/// user's fixed-size / locked-aspect preferences. Held by the singleton
/// <see cref="HudService"/> so it survives the HUD window being closed and reopened.
/// </summary>
public sealed class HudState
{
    /// <summary>The last confirmed selection rectangle, in physical pixels.</summary>
    public PixelRect? LastRegion { get; set; }

    /// <summary>Whether the user wants captures constrained to <see cref="FixedWidth"/>×<see cref="FixedHeight"/>.</summary>
    public bool FixedSizeEnabled { get; set; }

    public int FixedWidth { get; set; }

    public int FixedHeight { get; set; }

    /// <summary>Whether the selection overlay should lock to the remembered aspect ratio.</summary>
    public bool LockAspectEnabled { get; set; }

    /// <summary>Seeds the HUD from automation coordinates or size-only parameters.</summary>
    public void ApplyPreload(PixelRect? region, int? width, int? height)
    {
        if (region is { IsEmpty: false } r)
        {
            LastRegion = r;
            FixedWidth = r.Width;
            FixedHeight = r.Height;
            FixedSizeEnabled = true;
            return;
        }

        bool hasWidth = width is > 0;
        bool hasHeight = height is > 0;
        if (!hasWidth && !hasHeight)
        {
            return;
        }

        if (hasWidth)
        {
            FixedWidth = width!.Value;
        }

        if (hasHeight)
        {
            FixedHeight = height!.Value;
        }

        FixedSizeEnabled = FixedWidth > 0 && FixedHeight > 0;
    }

    /// <summary>The last aspect ratio (width / height), or null when unknown.</summary>
    public double? LastAspectRatio =>
        LastRegion is { } r && r.Height > 0 ? (double)r.Width / r.Height : null;

    /// <summary>A short human label for the last aspect, e.g. "16:9" or "1280 × 720".</summary>
    public string DescribeLast()
    {
        if (LastRegion is not { } r || r.IsEmpty)
        {
            return "none";
        }

        return $"{r.Width} × {r.Height}";
    }
}

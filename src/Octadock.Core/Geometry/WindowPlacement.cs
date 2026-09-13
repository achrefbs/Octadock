using Octadock.Core.Settings;

namespace Octadock.Core.Geometry;

/// <summary>Screen placement in physical pixels; never converts desktop origins to DIPs.</summary>
public static class WindowPlacement
{
    public static PixelRect Fit(PixelRect bounds, PixelRect workArea, int margin = 0)
    {
        if (workArea.IsEmpty) throw new ArgumentException("Display work area must be positive.", nameof(workArea));
        margin = Math.Clamp(margin, 0, Math.Max(0, Math.Min(workArea.Width, workArea.Height) / 4));
        int width = Math.Clamp(bounds.Width, 1, Math.Max(1, workArea.Width - 2 * margin));
        int height = Math.Clamp(bounds.Height, 1, Math.Max(1, workArea.Height - 2 * margin));
        return new PixelRect(
            Math.Clamp(bounds.X, workArea.X + margin, workArea.Right - margin - width),
            Math.Clamp(bounds.Y, workArea.Y + margin, workArea.Bottom - margin - height), width, height);
    }

    public static DockAnchor SaveAnchor(PixelPoint point, PixelRect workArea) => new()
    {
        X = Math.Clamp((point.X - workArea.X) / (double)Math.Max(1, workArea.Width), 0, 1),
        Y = Math.Clamp((point.Y - workArea.Y) / (double)Math.Max(1, workArea.Height), 0, 1),
    };

    public static PixelPoint? RestoreAnchor(DockSettings settings, DisplayInfo display)
    {
        DockAnchor? saved = settings.MonitorAnchors?.FirstOrDefault(pair =>
            string.Equals(pair.Key, display.Id.Value, StringComparison.OrdinalIgnoreCase)).Value;
        if (saved is not null && double.IsFinite(saved.X) && double.IsFinite(saved.Y))
        {
            return new PixelPoint(
                display.WorkArea.X + Math.Min(display.WorkArea.Width - 1, (int)Math.Round(Math.Clamp(saved.X, 0, 1) * display.WorkArea.Width)),
                display.WorkArea.Y + Math.Min(display.WorkArea.Height - 1, (int)Math.Round(Math.Clamp(saved.Y, 0, 1) * display.WorkArea.Height)));
        }

        var legacy = new PixelPoint(settings.AnchorX, settings.AnchorY);
        return settings.HasCustomAnchor && display.Bounds.Contains(legacy) ? legacy : null;
    }
}

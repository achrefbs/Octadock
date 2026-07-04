namespace Octadock.Core.Geometry;

/// <summary>
/// A rectangle in device-independent pixels (WPF units, 96 DPI baseline).
/// The UI layer works in DIPs; capture coordinates are converted to
/// <see cref="PixelRect"/> using the owning monitor's scale factor.
/// </summary>
public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public static readonly DipRect Empty = new(0, 0, 0, 0);

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static DipRect FromCorners(double x1, double y1, double x2, double y2)
        => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    /// <summary>
    /// Converts DIPs to physical pixels using <paramref name="dpiScale"/>
    /// (1.0 = 96 DPI, 1.5 = 150%). Rounds to the nearest whole pixel.
    /// </summary>
    public PixelRect ToPixels(double dpiScale)
    {
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        int x = (int)Math.Round(X * dpiScale, MidpointRounding.AwayFromZero);
        int y = (int)Math.Round(Y * dpiScale, MidpointRounding.AwayFromZero);
        int w = (int)Math.Round(Width * dpiScale, MidpointRounding.AwayFromZero);
        int h = (int)Math.Round(Height * dpiScale, MidpointRounding.AwayFromZero);
        return new PixelRect(x, y, w, h);
    }

    public override string ToString() => $"[{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}]";
}

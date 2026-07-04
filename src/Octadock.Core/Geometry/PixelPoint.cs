namespace Octadock.Core.Geometry;

/// <summary>
/// A point expressed in physical pixels relative to the virtual-screen origin
/// (top-left of the primary monitor). Octadock stores capture coordinates in
/// physical pixels to stay correct across mixed-DPI multi-monitor setups.
/// </summary>
public readonly record struct PixelPoint(int X, int Y)
{
    public static readonly PixelPoint Zero = new(0, 0);

    public PixelPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    public override string ToString() => $"({X}, {Y})";
}

namespace Octadock.Core.Primitives;

/// <summary>
/// A double-precision point in image-pixel space, used by annotation payloads
/// (freehand strokes, arrow/line endpoints) so vector data is stored
/// independently of any UI framework's point type.
/// </summary>
public readonly record struct PointD(double X, double Y)
{
    public static readonly PointD Zero = new(0, 0);

    public double DistanceTo(PointD other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}

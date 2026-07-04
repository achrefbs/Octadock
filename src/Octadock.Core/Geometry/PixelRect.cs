namespace Octadock.Core.Geometry;

/// <summary>
/// An axis-aligned rectangle in physical pixels relative to the virtual-screen
/// origin. This is the canonical rectangle type for capture regions, monitor
/// bounds and work areas throughout Octadock.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public static readonly PixelRect Empty = new(0, 0, 0, 0);

    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public PixelPoint Location => new(X, Y);
    public PixelSize Size => new(Width, Height);
    public PixelPoint Center => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>True when the rectangle has no positive area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Number of pixels the rectangle covers.</summary>
    public long Area => Size.Area;

    /// <summary>Builds a rectangle from left/top/right/bottom edges.</summary>
    public static PixelRect FromEdges(int left, int top, int right, int bottom)
        => new(left, top, right - left, bottom - top);

    /// <summary>
    /// Builds a rectangle from two arbitrary corner points, normalizing so the
    /// result always has non-negative width/height. Useful for drag selection
    /// where the user may drag up-and-left.
    /// </summary>
    public static PixelRect FromCorners(PixelPoint a, PixelPoint b)
    {
        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X, b.X);
        int bottom = Math.Max(a.Y, b.Y);
        return FromEdges(left, top, right, bottom);
    }

    /// <summary>Returns a copy guaranteed to have non-negative dimensions.</summary>
    public PixelRect Normalized()
    {
        int left = Math.Min(X, Right);
        int top = Math.Min(Y, Bottom);
        return new PixelRect(left, top, Math.Abs(Width), Math.Abs(Height));
    }

    public bool Contains(PixelPoint p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public bool Contains(PixelRect other)
        => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    public bool IntersectsWith(PixelRect other)
        => other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;

    /// <summary>Returns the intersection, or <see cref="Empty"/> if disjoint.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right > left && bottom > top ? FromEdges(left, top, right, bottom) : Empty;
    }

    /// <summary>Returns the smallest rectangle containing both inputs.</summary>
    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        int left = Math.Min(Left, other.Left);
        int top = Math.Min(Top, other.Top);
        int right = Math.Max(Right, other.Right);
        int bottom = Math.Max(Bottom, other.Bottom);
        return FromEdges(left, top, right, bottom);
    }

    /// <summary>Grows (or, with negative values, shrinks) the rectangle on all sides.</summary>
    public PixelRect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + (2 * dx), Height + (2 * dy));

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>Clamps this rectangle so it stays inside <paramref name="bounds"/>.</summary>
    public PixelRect ClampTo(PixelRect bounds) => Intersect(bounds);

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}

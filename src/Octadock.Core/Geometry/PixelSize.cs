namespace Octadock.Core.Geometry;

/// <summary>A width/height pair expressed in physical pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public static readonly PixelSize Empty = new(0, 0);

    /// <summary>True when either dimension is zero or negative.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Total pixel count (width * height), guarded against overflow.</summary>
    public long Area => (long)Math.Max(0, Width) * Math.Max(0, Height);

    public override string ToString() => $"{Width}x{Height}";
}

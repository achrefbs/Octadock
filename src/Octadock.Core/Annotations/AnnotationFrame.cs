namespace Octadock.Core.Annotations;

/// <summary>
/// The bounding rectangle of an annotation object in image-pixel space
/// (origin at the top-left of the base capture). Doubles allow sub-pixel
/// placement while editing.
/// </summary>
public readonly record struct AnnotationFrame(double X, double Y, double Width, double Height)
{
    public static readonly AnnotationFrame Empty = new(0, 0, 0, 0);

    public double Right => X + Width;
    public double Bottom => Y + Height;

    public AnnotationFrame Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}

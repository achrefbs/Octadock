using Octadock.Core.Annotations;
using Octadock.Core.Primitives;

namespace Octadock.App.Editing;

internal static class AnnotationEndpoints
{
    internal static (PointD Start, PointD End) Get(AnnotationObject obj)
        => obj.Payload.Points.Count >= 2
            ? (obj.Payload.Points[0], obj.Payload.Points[1])
            : (new PointD(obj.Frame.X, obj.Frame.Y), new PointD(obj.Frame.Right, obj.Frame.Bottom));

    internal static AnnotationObject Move(AnnotationObject obj, bool moveStart, PointD position)
    {
        (PointD start, PointD end) = Get(obj);
        if (moveStart)
        {
            start = position;
        }
        else
        {
            end = position;
        }

        return obj with
        {
            Frame = new AnnotationFrame(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)),
            Payload = obj.Payload with { Points = [start, end] },
        };
    }
}

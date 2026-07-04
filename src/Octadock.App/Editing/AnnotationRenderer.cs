using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Octadock.Core.Annotations;
using Octadock.Core.Primitives;
using WpfPoint = System.Windows.Point;

namespace Octadock.App.Editing;

/// <summary>
/// Renders <see cref="AnnotationObject"/>s into WPF drawing primitives in
/// image-pixel coordinates. The same renderer drives the live editor overlay and
/// the flattened export, so what the user sees is exactly what is exported.
/// </summary>
/// <remarks>
/// The renderer draws into a <see cref="DrawingContext"/> at 1:1 image scale; the
/// hosting canvas applies the on-screen scale transform. Blur and pixelate need
/// the base raster (they resample the region beneath them), which is supplied to
/// <see cref="Render"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class AnnotationRenderer
{
    /// <summary>
    /// Draws every object in stacking order into <paramref name="dc"/>. The
    /// document's objects are already sorted by z-index.
    /// </summary>
    public static void Render(DrawingContext dc, AnnotationDocument document, BitmapSource baseImage)
    {
        foreach (AnnotationObject obj in document.Objects)
        {
            RenderObject(dc, obj, baseImage);
        }
    }

    /// <summary>Draws a single object into <paramref name="dc"/>.</summary>
    public static void RenderObject(DrawingContext dc, AnnotationObject obj, BitmapSource baseImage)
    {
        double opacity = Math.Clamp(obj.Style.Opacity, 0, 1);
        bool pushedOpacity = opacity < 1.0;
        if (pushedOpacity)
        {
            dc.PushOpacity(opacity);
        }

        try
        {
            switch (obj.Type)
            {
                case AnnotationObjectType.Rectangle:
                    DrawRectangle(dc, obj);
                    break;
                case AnnotationObjectType.Ellipse:
                    DrawEllipse(dc, obj);
                    break;
                case AnnotationObjectType.Line:
                    DrawLine(dc, obj);
                    break;
                case AnnotationObjectType.Arrow:
                    DrawArrow(dc, obj);
                    break;
                case AnnotationObjectType.Text:
                    DrawText(dc, obj);
                    break;
                case AnnotationObjectType.Highlight:
                    DrawHighlight(dc, obj);
                    break;
                case AnnotationObjectType.Blur:
                case AnnotationObjectType.Pixelate:
                    DrawObscure(dc, obj, baseImage);
                    break;
                case AnnotationObjectType.Counter:
                    DrawCounter(dc, obj);
                    break;
                case AnnotationObjectType.Freehand:
                    DrawFreehand(dc, obj);
                    break;
                case AnnotationObjectType.Crop:
                    // Crop is realized by resizing the canvas, not by drawing.
                    break;
            }
        }
        finally
        {
            if (pushedOpacity)
            {
                dc.Pop();
            }
        }
    }

    private static Pen? StrokePen(AnnotationObject obj)
    {
        if (obj.Style.Stroke is not { } stroke || obj.Style.LineWidth <= 0)
        {
            return null;
        }

        var pen = new Pen(new SolidColorBrush(stroke.ToWpf()), obj.Style.LineWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Brush? FillBrush(AnnotationObject obj)
    {
        if (obj.Style.Fill is not { } fill)
        {
            return null;
        }

        var brush = new SolidColorBrush(fill.ToWpf());
        brush.Freeze();
        return brush;
    }

    private static void DrawRectangle(DrawingContext dc, AnnotationObject obj)
    {
        Rect rect = obj.Frame.ToRect();
        double radius = Math.Max(0, obj.Style.CornerRadius);
        dc.DrawRoundedRectangle(FillBrush(obj), StrokePen(obj), rect, radius, radius);
    }

    private static void DrawEllipse(DrawingContext dc, AnnotationObject obj)
    {
        Rect rect = obj.Frame.ToRect();
        var center = new WpfPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        dc.DrawEllipse(FillBrush(obj), StrokePen(obj), center, rect.Width / 2, rect.Height / 2);
    }

    private static void DrawLine(DrawingContext dc, AnnotationObject obj)
    {
        (WpfPoint start, WpfPoint end) = Endpoints(obj);
        Pen? pen = StrokePen(obj);
        if (pen is not null)
        {
            dc.DrawLine(pen, start, end);
        }
    }

    private static void DrawArrow(DrawingContext dc, AnnotationObject obj)
    {
        (WpfPoint start, WpfPoint end) = Endpoints(obj);
        Pen? pen = StrokePen(obj);
        if (pen is null)
        {
            return;
        }

        dc.DrawLine(pen, start, end);

        if (obj.Style.ArrowHead == ArrowHeadStyle.None)
        {
            return;
        }

        // Arrowhead geometry: two barbs swept back from the tip.
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 0.001)
        {
            return;
        }

        double ux = dx / len;
        double uy = dy / len;

        double headLength = Math.Max(obj.Style.LineWidth * 3.5, 12);
        double halfWidth = obj.Style.ArrowHead switch
        {
            ArrowHeadStyle.Thin => headLength * 0.35,
            ArrowHeadStyle.Triangle => headLength * 0.6,
            _ => headLength * 0.5,
        };

        // Base of the head, back along the shaft from the tip.
        var baseCenter = new WpfPoint(end.X - (ux * headLength), end.Y - (uy * headLength));
        // Perpendicular unit vector.
        double px = -uy;
        double py = ux;
        var left = new WpfPoint(baseCenter.X + (px * halfWidth), baseCenter.Y + (py * halfWidth));
        var right = new WpfPoint(baseCenter.X - (px * halfWidth), baseCenter.Y - (py * halfWidth));

        var stroke = obj.Style.Stroke ?? RgbaColor.Black;
        var fill = new SolidColorBrush(stroke.ToWpf());
        fill.Freeze();

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(end, isFilled: true, isClosed: true);
            ctx.LineTo(left, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(right, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        dc.DrawGeometry(fill, obj.Style.ArrowHead == ArrowHeadStyle.Triangle ? null : StrokePen(obj), geometry);
    }

    private static void DrawText(DrawingContext dc, AnnotationObject obj)
    {
        string text = obj.Payload.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        FormattedText formatted = BuildFormattedText(obj, text);
        Rect rect = obj.Frame.ToRect();
        if (rect.Width > 1)
        {
            formatted.MaxTextWidth = rect.Width;
        }

        // Optional background fill behind the text block.
        Brush? fill = FillBrush(obj);
        if (fill is not null)
        {
            dc.DrawRectangle(fill, null, rect);
        }

        dc.DrawText(formatted, new WpfPoint(rect.X, rect.Y));
    }

    /// <summary>Builds the formatted text used both to render and to measure the text tool.</summary>
    public static FormattedText BuildFormattedText(AnnotationObject obj, string text)
    {
        var typeface = new Typeface(
            new FontFamily(obj.Style.FontFamily),
            obj.Style.Italic ? FontStyles.Italic : FontStyles.Normal,
            obj.Style.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        var color = obj.Style.Stroke ?? RgbaColor.Black;
        var brush = new SolidColorBrush(color.ToWpf());
        brush.Freeze();

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            obj.Style.FontSize,
            brush,
            1.0)
        {
            TextAlignment = obj.Style.TextAlignment switch
            {
                Core.Annotations.TextAlignment.Center => System.Windows.TextAlignment.Center,
                Core.Annotations.TextAlignment.Right => System.Windows.TextAlignment.Right,
                _ => System.Windows.TextAlignment.Left,
            },
        };

        return formatted;
    }

    private static void DrawHighlight(DrawingContext dc, AnnotationObject obj)
    {
        Rect rect = obj.Frame.ToRect();

        // A highlighter is a translucent wash. Prefer the fill; fall back to a
        // semi-transparent yellow so the tool still reads if only a stroke is set.
        RgbaColor color = obj.Style.Fill ?? (obj.Style.Stroke ?? new RgbaColor(255, 235, 59));
        if (color.A == 255)
        {
            color = color.WithOpacity(0.35);
        }

        var brush = new SolidColorBrush(color.ToWpf());
        brush.Freeze();
        dc.DrawRectangle(brush, null, rect);
    }

    private static void DrawObscure(DrawingContext dc, AnnotationObject obj, BitmapSource baseImage)
    {
        Rect rect = obj.Frame.ToRect();
        Int32Rect source = ClampToImage(rect, baseImage);
        if (source.Width <= 0 || source.Height <= 0)
        {
            return;
        }

        var cropped = new CroppedBitmap(baseImage, source);
        var destination = new Rect(source.X, source.Y, source.Width, source.Height);

        if (obj.Type == AnnotationObjectType.Pixelate)
        {
            DrawPixelated(dc, cropped, destination, obj.Style.Radius);
        }
        else
        {
            DrawBlurred(dc, cropped, destination, obj.Style.Radius);
        }
    }

    private static void DrawBlurred(DrawingContext dc, BitmapSource cropped, Rect rect, double sigma)
    {
        // Render the cropped region through a BlurEffect into a bitmap, then draw
        // that bitmap into the region. Effects cannot be applied to a raw
        // DrawingContext.DrawImage, so we bake it via a DrawingVisual.
        var visual = new DrawingVisual
        {
            Effect = new BlurEffect
            {
                Radius = Math.Clamp(sigma, 1, 100),
                KernelType = KernelType.Gaussian,
            },
        };

        using (DrawingContext vdc = visual.RenderOpen())
        {
            vdc.DrawImage(cropped, new Rect(0, 0, cropped.PixelWidth, cropped.PixelHeight));
        }

        var baked = new RenderTargetBitmap(
            Math.Max(1, cropped.PixelWidth),
            Math.Max(1, cropped.PixelHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        baked.Render(visual);
        baked.Freeze();

        dc.DrawImage(baked, rect);
    }

    private static void DrawPixelated(DrawingContext dc, BitmapSource cropped, Rect rect, double blockSize)
    {
        double block = Math.Max(4, blockSize);
        int smallW = Math.Max(1, (int)Math.Round(cropped.PixelWidth / block));
        int smallH = Math.Max(1, (int)Math.Round(cropped.PixelHeight / block));

        // Down-scale to a tiny bitmap, then bake it back up at the region size with
        // nearest-neighbor scaling to produce chunky "pixelated" blocks. The
        // scaling mode must be set on the visual doing the up-scale, so we go
        // through a DrawingVisual + RenderTargetBitmap.
        var down = new TransformedBitmap(
            cropped,
            new ScaleTransform((double)smallW / cropped.PixelWidth, (double)smallH / cropped.PixelHeight));
        down.Freeze();

        int outW = Math.Max(1, (int)Math.Round(rect.Width));
        int outH = Math.Max(1, (int)Math.Round(rect.Height));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext vdc = visual.RenderOpen())
        {
            vdc.DrawImage(down, new Rect(0, 0, outW, outH));
        }

        var baked = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
        baked.Render(visual);
        baked.Freeze();

        dc.DrawImage(baked, rect);
    }

    private static void DrawCounter(DrawingContext dc, AnnotationObject obj)
    {
        Rect rect = obj.Frame.ToRect();
        double diameter = Math.Max(rect.Width, rect.Height);
        if (diameter <= 0)
        {
            diameter = Math.Max(24, obj.Style.FontSize * 1.6);
        }

        var center = new WpfPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        RgbaColor fillColor = obj.Style.Fill ?? RgbaColor.Accent;
        var fill = new SolidColorBrush(fillColor.ToWpf());
        fill.Freeze();

        dc.DrawEllipse(fill, StrokePen(obj), center, diameter / 2, diameter / 2);

        int value = obj.Payload.CounterValue ?? 1;
        var typeface = new Typeface(
            new FontFamily(obj.Style.FontFamily),
            FontStyles.Normal,
            FontWeights.Bold,
            FontStretches.Normal);

        var textColor = obj.Style.Stroke ?? RgbaColor.White;
        var brush = new SolidColorBrush(textColor.ToWpf());
        brush.Freeze();

        var formatted = new FormattedText(
            value.ToString(CultureInfo.CurrentCulture),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            diameter * 0.5,
            brush,
            1.0);

        dc.DrawText(
            formatted,
            new WpfPoint(center.X - (formatted.Width / 2), center.Y - (formatted.Height / 2)));
    }

    private static void DrawFreehand(DrawingContext dc, AnnotationObject obj)
    {
        IReadOnlyList<PointD> points = obj.Payload.Points;
        if (points.Count < 2)
        {
            return;
        }

        Pen? pen = StrokePen(obj);
        if (pen is null)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0].ToWpf(), isFilled: false, isClosed: false);
            for (int i = 1; i < points.Count; i++)
            {
                ctx.LineTo(points[i].ToWpf(), isStroked: true, isSmoothJoin: true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static (WpfPoint Start, WpfPoint End) Endpoints(AnnotationObject obj)
    {
        IReadOnlyList<PointD> points = obj.Payload.Points;
        if (points.Count >= 2)
        {
            return (points[0].ToWpf(), points[1].ToWpf());
        }

        // Fall back to the frame diagonal.
        Rect rect = obj.Frame.ToRect();
        return (new WpfPoint(rect.X, rect.Y), new WpfPoint(rect.Right, rect.Bottom));
    }

    private static Int32Rect ClampToImage(Rect rect, BitmapSource image)
    {
        int x = Math.Max(0, (int)Math.Floor(rect.X));
        int y = Math.Max(0, (int)Math.Floor(rect.Y));
        int right = Math.Min(image.PixelWidth, (int)Math.Ceiling(rect.Right));
        int bottom = Math.Min(image.PixelHeight, (int)Math.Ceiling(rect.Bottom));
        return new Int32Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}

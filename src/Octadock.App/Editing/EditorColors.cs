using System.Windows.Media;
using Octadock.Core.Annotations;
using Octadock.Core.Primitives;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace Octadock.App.Editing;

/// <summary>
/// Conversions between the framework-independent annotation primitives
/// (<see cref="RgbaColor"/>, <see cref="PointD"/>, <see cref="AnnotationFrame"/>)
/// and their WPF equivalents. Kept in one place so the overlay renderer, the
/// export flattener and the property controls all agree on the mapping.
/// </summary>
internal static class EditorColors
{
    /// <summary>Converts a straight RGBA color to a WPF color (alpha preserved).</summary>
    public static WpfColor ToWpf(this RgbaColor color)
        => WpfColor.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Converts a WPF color to a straight RGBA color.</summary>
    public static RgbaColor ToRgba(this WpfColor color)
        => new(color.R, color.G, color.B, color.A);

    /// <summary>Builds a frozen solid brush for the given color, or <c>null</c> when the color is null.</summary>
    public static SolidColorBrush? ToBrush(this RgbaColor? color)
    {
        if (color is not { } c)
        {
            return null;
        }

        var brush = new SolidColorBrush(c.ToWpf());
        brush.Freeze();
        return brush;
    }

    /// <summary>Converts an image-space point to a WPF point.</summary>
    public static WpfPoint ToWpf(this PointD point) => new(point.X, point.Y);

    /// <summary>Converts a WPF point to an image-space point.</summary>
    public static PointD ToPointD(this WpfPoint point) => new(point.X, point.Y);

    /// <summary>Converts an annotation frame to a WPF rectangle.</summary>
    public static System.Windows.Rect ToRect(this AnnotationFrame frame)
        => new(frame.X, frame.Y, Math.Max(0, frame.Width), Math.Max(0, frame.Height));
}

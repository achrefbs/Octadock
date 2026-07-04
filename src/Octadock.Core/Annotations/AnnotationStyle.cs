using Octadock.Core.Primitives;

namespace Octadock.Core.Annotations;

/// <summary>
/// Visual style for an annotation object. Not every field applies to every
/// object type; renderers read only the fields relevant to the object's
/// <see cref="AnnotationObjectType"/>.
/// </summary>
public sealed record AnnotationStyle
{
    /// <summary>Stroke/line color. <c>null</c> means no stroke.</summary>
    public RgbaColor? Stroke { get; init; } = RgbaColor.Accent;

    /// <summary>Fill color. <c>null</c> means unfilled.</summary>
    public RgbaColor? Fill { get; init; }

    /// <summary>Stroke width in image pixels.</summary>
    public double LineWidth { get; init; } = 4.0;

    /// <summary>Overall object opacity in the range 0..1.</summary>
    public double Opacity { get; init; } = 1.0;

    // --- Text ---
    public string FontFamily { get; init; } = "Segoe UI";

    public double FontSize { get; init; } = 24.0;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public TextAlignment TextAlignment { get; init; } = TextAlignment.Left;

    // --- Arrows / lines ---
    public ArrowHeadStyle ArrowHead { get; init; } = ArrowHeadStyle.Standard;

    // --- Blur / pixelate ---
    /// <summary>Blur sigma or pixelate block size, in image pixels.</summary>
    public double Radius { get; init; } = 12.0;

    // --- Rounded rectangles ---
    public double CornerRadius { get; init; }

    /// <summary>The editor's default style for a freshly created object.</summary>
    public static AnnotationStyle Default => new();
}

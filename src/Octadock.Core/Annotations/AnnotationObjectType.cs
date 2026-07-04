namespace Octadock.Core.Annotations;

/// <summary>
/// The kinds of annotation object Octadock's editor supports. The P0 editor
/// ships crop, arrow, rectangle, text, blur, pixelate and highlight; the
/// remaining values are modeled now so the project format is forward-compatible
/// with the P1/P2 tools (ellipse, line, counter, freehand, image).
/// </summary>
public enum AnnotationObjectType
{
    Arrow = 0,
    Rectangle,
    Ellipse,
    Line,
    Text,
    Blur,
    Pixelate,
    Highlight,
    Crop,
    Counter,
    Freehand,
    Image,
}

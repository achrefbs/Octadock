using Octadock.Core.Annotations;

namespace Octadock.App.Editing;

/// <summary>
/// The editor tools the toolbar exposes. <see cref="Select"/> and <see cref="Crop"/>
/// are editor gestures; the remaining values map 1:1 onto an
/// <see cref="AnnotationObjectType"/> so drawing a shape creates the matching object.
/// The P0 set is Select, Crop, Arrow, Rectangle, Text, Blur, Pixelate and
/// Highlighter; the others are modeled so the toolbar and format stay
/// forward-compatible.
/// </summary>
public enum EditorTool
{
    Select = 0,
    Crop,
    Arrow,
    Rectangle,
    Ellipse,
    Line,
    Text,
    Blur,
    Pixelate,
    Highlighter,
    Counter,
    Freehand,
}

/// <summary>Helpers mapping <see cref="EditorTool"/> to the annotation model.</summary>
internal static class EditorToolExtensions
{
    /// <summary>
    /// The annotation object type a drawing tool creates, or <c>null</c> for the
    /// non-drawing editor gestures (<see cref="EditorTool.Select"/>,
    /// <see cref="EditorTool.Crop"/>).
    /// </summary>
    public static AnnotationObjectType? ToObjectType(this EditorTool tool) => tool switch
    {
        EditorTool.Arrow => AnnotationObjectType.Arrow,
        EditorTool.Rectangle => AnnotationObjectType.Rectangle,
        EditorTool.Ellipse => AnnotationObjectType.Ellipse,
        EditorTool.Line => AnnotationObjectType.Line,
        EditorTool.Text => AnnotationObjectType.Text,
        EditorTool.Blur => AnnotationObjectType.Blur,
        EditorTool.Pixelate => AnnotationObjectType.Pixelate,
        EditorTool.Highlighter => AnnotationObjectType.Highlight,
        EditorTool.Counter => AnnotationObjectType.Counter,
        EditorTool.Freehand => AnnotationObjectType.Freehand,
        _ => null,
    };

    /// <summary>True when the tool draws a new annotation object on drag.</summary>
    public static bool IsDrawing(this EditorTool tool) => tool.ToObjectType() is not null;
}

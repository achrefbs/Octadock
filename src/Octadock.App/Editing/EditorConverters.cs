using System.Globalization;
using System.Windows.Data;

namespace Octadock.App.Editing;

/// <summary>Maps an <see cref="EditorTool"/> to a short glyph for the toolbar button.</summary>
public sealed class ToolGlyphConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EditorTool.Select => "⇱",       // arrow (select)
        EditorTool.Crop => "✂",          // scissors (crop)
        EditorTool.Arrow => "↗",         // north-east arrow
        EditorTool.Rectangle => "▭",     // rectangle
        EditorTool.Ellipse => "◯",       // circle
        EditorTool.Line => "╱",          // diagonal line
        EditorTool.Text => "T",
        EditorTool.Highlighter => "✎",   // pencil-ish
        EditorTool.Blur => "◌",          // dotted circle
        EditorTool.Pixelate => "▓",      // shaded block
        EditorTool.Counter => "①",       // circled 1
        EditorTool.Freehand => "✏",      // pencil
        _ => "?",
    };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>A human-readable tooltip for each tool.</summary>
public sealed class ToolLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EditorTool.Select => "Select / Move",
        EditorTool.Crop => "Crop",
        EditorTool.Arrow => "Arrow",
        EditorTool.Rectangle => "Rectangle",
        EditorTool.Ellipse => "Ellipse",
        EditorTool.Line => "Line",
        EditorTool.Text => "Text",
        EditorTool.Highlighter => "Highlighter",
        EditorTool.Blur => "Blur",
        EditorTool.Pixelate => "Pixelate",
        EditorTool.Counter => "Step counter",
        EditorTool.Freehand => "Freehand",
        _ => value?.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// True when the two bound tools are equal. Used with a MultiBinding of
/// [ActiveTool, thisTool] to highlight the active toolbar button.
/// </summary>
public sealed class ToolIsActiveConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Length == 2
        && values[0] is EditorTool active
        && values[1] is EditorTool tool
        && active == tool;

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

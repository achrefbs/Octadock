using System.Globalization;
using System.Windows.Data;
using MahApps.Metro.IconPacks;

namespace Octadock.App.Editing;

/// <summary>Maps an <see cref="EditorTool"/> to its Lucide toolbar icon.</summary>
public sealed class ToolGlyphConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        EditorTool.Select => PackIconLucideKind.MousePointer2,
        EditorTool.Crop => PackIconLucideKind.Crop,
        EditorTool.Arrow => PackIconLucideKind.ArrowUpRight,
        EditorTool.Rectangle => PackIconLucideKind.Square,
        EditorTool.Ellipse => PackIconLucideKind.Circle,
        EditorTool.Line => PackIconLucideKind.Slash,
        EditorTool.Text => PackIconLucideKind.Type,
        EditorTool.Highlighter => PackIconLucideKind.Highlighter,
        EditorTool.Blur => PackIconLucideKind.Droplets,
        EditorTool.Pixelate => PackIconLucideKind.Grid2x2,
        EditorTool.Counter => PackIconLucideKind.ListOrdered,
        EditorTool.Freehand => PackIconLucideKind.Pencil,
        _ => PackIconLucideKind.Circle,
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

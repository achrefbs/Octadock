using System.Windows.Media;
using Octadock.Core.Primitives;

namespace Octadock.App.Editing;

/// <summary>A named color swatch shown in the editor's color picker strip.</summary>
public sealed record ColorSwatch(RgbaColor Color)
{
    /// <summary>A frozen brush for binding to the swatch button.</summary>
    public Brush Brush { get; } = CreateBrush(Color);

    /// <summary>The hex representation, used as the tooltip.</summary>
    public string Hex => Color.ToHex();

    private static Brush CreateBrush(RgbaColor color)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>The default editor palette (accent, primaries and neutrals).</summary>
    public static IReadOnlyList<ColorSwatch> DefaultPalette { get; } =
    [
        new ColorSwatch(RgbaColor.Accent),
        new ColorSwatch(new RgbaColor(0xE8, 0x1A, 0x1A)),   // red
        new ColorSwatch(new RgbaColor(0xF5, 0x9E, 0x0B)),   // amber
        new ColorSwatch(new RgbaColor(0xFA, 0xCC, 0x15)),   // yellow
        new ColorSwatch(new RgbaColor(0x22, 0xC5, 0x5E)),   // green
        new ColorSwatch(new RgbaColor(0x06, 0xB6, 0xD4)),   // cyan
        new ColorSwatch(new RgbaColor(0x8B, 0x5C, 0xF6)),   // violet
        new ColorSwatch(new RgbaColor(0xEC, 0x48, 0x99)),   // pink
        new ColorSwatch(RgbaColor.Black),
        new ColorSwatch(RgbaColor.White),
    ];
}

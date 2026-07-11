using System.Windows;
using System.Windows.Media;

namespace Octadock.App.Theming;

/// <summary>
/// Design-system tokens for code-built WPF surfaces that cannot consume XAML
/// styles directly. Keep these aligned with Resources/Themes/*.xaml.
/// </summary>
internal static class OctadockDesignTokens
{
    public static class Brushes
    {
        public static readonly SolidColorBrush Canvas = Frozen(0xFF, 0x07, 0x0B, 0x14);
        public static readonly SolidColorBrush Surface = Frozen(0xFF, 0x0C, 0x12, 0x20);
        public static readonly SolidColorBrush SurfaceRaised = Frozen(0xFF, 0x14, 0x1E, 0x32);
        public static readonly SolidColorBrush SurfaceOverlay = Frozen(0xFF, 0x1B, 0x29, 0x42);
        public static readonly SolidColorBrush DockSurface = Frozen(0xD9, 0x0C, 0x12, 0x20);
        public static readonly SolidColorBrush PreviewShell = Frozen(0xDE, 0x08, 0x0D, 0x15);
        public static readonly SolidColorBrush PreviewChrome = Frozen(0x22, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush PreviewPanel = Frozen(0x16, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush GlassBorder = Frozen(0x30, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Text = Frozen(0xFF, 0xF2, 0xF6, 0xFC);
        public static readonly SolidColorBrush TextMuted = Frozen(0xFF, 0x8D, 0xA0, 0xBC);
        public static readonly SolidColorBrush Accent = Frozen(0xFF, 0x2D, 0xD4, 0xBF);
        public static readonly SolidColorBrush Cloud = Frozen(0xFF, 0xA7, 0x8B, 0xFA);
        public static readonly SolidColorBrush Danger = Frozen(0xFF, 0xFB, 0x71, 0x85);
        public static readonly SolidColorBrush Warning = Frozen(0xFF, 0xFB, 0xBF, 0x24);
        public static readonly SolidColorBrush NeutralAccent = Frozen(0xFF, 0xE5, 0xE7, 0xEB);
        public static readonly SolidColorBrush Field = Frozen(0x24, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Rule = Frozen(0x26, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Menu = Frozen(0xF8, 0x0A, 0x11, 0x19);
        public static readonly SolidColorBrush MenuHover = Frozen(0x22, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush DangerHover = Frozen(0x42, 0xFF, 0x5F, 0x57);
        public static readonly SolidColorBrush ActionHover = Frozen(0x2A, 0x45, 0xE6, 0xFF);
        public static readonly SolidColorBrush ActiveAction = Frozen(0x36, 0x45, 0xE6, 0xFF);
        public static readonly SolidColorBrush RowHover = Frozen(0x16, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush RowSelected = Frozen(0x30, 0x2D, 0xD4, 0xBF);
    }

    public static class Radius
    {
        public static readonly CornerRadius Window = new(12);
        public static readonly CornerRadius Panel = new(14);
        public static readonly CornerRadius Rail = new(10);
        public static readonly CornerRadius Control = new(8);
        public static readonly CornerRadius Small = new(7);
    }

    private static SolidColorBrush Frozen(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}

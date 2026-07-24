using System.Windows;
using System.Windows.Media;

namespace Octadock.App.Theming;

/// <summary>
/// Design-system tokens for code-built WPF surfaces that cannot consume XAML
/// styles directly. Values are resolved from the active resource dictionary so
/// light and high-contrast palettes are honored instead of freezing the dark
/// palette into code.
/// </summary>
internal static class OctadockDesignTokens
{
    public static class Brushes
    {
        public static Brush Canvas => Resolve("Octadock.Brush.Canvas", SystemColors.WindowBrush);
        public static Brush Surface => Resolve("Octadock.Brush.Surface", SystemColors.ControlBrush);
        public static Brush SurfaceRaised => Resolve("Octadock.Brush.SurfaceRaised", SystemColors.ControlBrush);
        public static Brush SurfaceOverlay => Resolve("Octadock.Brush.SurfaceOverlay", SystemColors.ControlBrush);
        public static Brush BorderStrong => Resolve("Octadock.Brush.BorderStrong", SystemColors.WindowTextBrush);
        public static Brush DockSurface => Resolve("Octadock.Brush.GlassSurface", SystemColors.ControlBrush);
        public static Brush GlassBorder => Resolve("Octadock.Brush.GlassBorder", SystemColors.WindowTextBrush);
        public static Brush GlassBorderStrong => Resolve("Octadock.Brush.GlassBorderStrong", SystemColors.WindowTextBrush);
        public static Brush GlassHighlight => Resolve("Octadock.Brush.GlassHighlight", SystemColors.WindowTextBrush);
        public static Brush Text => Resolve("Octadock.Brush.Text", SystemColors.WindowTextBrush);
        public static Brush TextMuted => Resolve("Octadock.Brush.TextMuted", SystemColors.GrayTextBrush);
        public static Brush TextSecondaryStrong => Resolve("Octadock.Brush.TextSecondaryStrong", SystemColors.WindowTextBrush);
        public static Brush Accent => Resolve("Octadock.Brush.Accent", SystemColors.HighlightBrush);
        public static Brush AccentText => Resolve("Octadock.Brush.AccentText", SystemColors.HighlightTextBrush);
        public static Brush Cloud => Resolve("Octadock.Brush.Cloud", SystemColors.HighlightBrush);
        public static Brush Danger => Resolve("Octadock.Brush.Danger", SystemColors.HotTrackBrush);
        public static Brush Warning => Resolve("Octadock.Brush.Warning", SystemColors.HighlightBrush);
        public static Brush NeutralAccent => Resolve("Octadock.Brush.TextSecondaryStrong", SystemColors.WindowTextBrush);
        public static Brush Field => Resolve("Octadock.Brush.InputBackground", SystemColors.WindowBrush);
        public static Brush MediaBackdrop => Resolve("Octadock.Brush.MediaBackdrop", SystemColors.WindowBrush);
        public static Brush Rule => Resolve("Octadock.Brush.Border", SystemColors.WindowTextBrush);
        public static Brush Menu => Resolve("Octadock.Brush.SurfaceRaised", SystemColors.MenuBrush);
        public static Brush MenuHover => Resolve("Octadock.Brush.Hover", SystemColors.HighlightBrush);
        public static Brush DangerHover => Resolve("Octadock.Brush.DangerSoft", SystemColors.HighlightBrush);
        public static Brush ActionHover => Resolve("Octadock.Brush.Hover", SystemColors.HighlightBrush);
        public static Brush ActiveAction => Resolve("Octadock.Brush.SelectionSubtle", SystemColors.HighlightBrush);
        public static Brush RowHover => Resolve("Octadock.Brush.GlassRowHover", SystemColors.HighlightBrush);
        public static Brush RowSelected => Resolve("Octadock.Brush.SelectionSubtle", SystemColors.HighlightBrush);
        public static Brush Hover => Resolve("Octadock.Brush.Hover", SystemColors.HighlightBrush);
        public static Brush Pressed => Resolve("Octadock.Brush.Pressed", SystemColors.HighlightBrush);
        public static Brush CaptureDim => Resolve("Octadock.Brush.CaptureDim", SystemColors.ControlDarkBrush);
        public static Brush EditorCropDim => Resolve("Octadock.Brush.EditorCropDim", SystemColors.ControlDarkBrush);
        public static Brush CaptureHandle => Resolve("Octadock.Brush.CaptureHandle", SystemColors.HighlightTextBrush);
    }

    private static Brush Resolve(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;
}

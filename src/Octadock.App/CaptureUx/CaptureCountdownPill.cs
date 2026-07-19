using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Octadock.App.Theming;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// A tiny capture-excluded countdown shown after a self-timer region is selected and
/// before the actual screenshot is taken.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CaptureCountdownPill : ToolWindowBase
{
    private static Brush GlassBackground => OctadockDesignTokens.Brushes.PreviewChrome;
    private static Brush GlassBorder => OctadockDesignTokens.Brushes.GlassBorderStrong;
    private static Brush TextBrush => OctadockDesignTokens.Brushes.Text;
    private static Brush MutedBrush => OctadockDesignTokens.Brushes.TextSecondaryStrong;
    private static Brush AccentBrush => OctadockDesignTokens.Brushes.Accent;

    private readonly IMonitorService _monitors;
    private readonly TextBlock _number;
    private readonly TextBlock _label;
    private bool _closed;

    public CaptureCountdownPill(IMonitorService monitors)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        Width = 96;
        Height = 96;
        Topmost = true;

        _number = new TextBlock
        {
            Text = "3",
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 38,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };

        _label = new TextBlock
        {
            Text = "Capture",
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = MutedBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, -4, 0, 0),
        };

        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(_number);
        stack.Children.Add(_label);

        Content = new Border
        {
            Width = 96,
            Height = 96,
            CornerRadius = new CornerRadius(48),
            Background = GlassBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                Children =
                {
                    new System.Windows.Shapes.Ellipse
                    {
                        Stroke = AccentBrush,
                        StrokeThickness = 3,
                        Margin = new Thickness(8),
                        Opacity = 0.82,
                    },
                    stack,
                },
            },
        };

        SizeChanged += (_, _) => Reposition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    public void ShowOnActiveMonitor()
    {
        _closed = false;
        Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Reposition);
    }

    public void Update(int remainingSeconds)
    {
        int remaining = Math.Max(0, remainingSeconds);
        _number.Text = remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _label.Text = remaining == 1 ? "second" : "seconds";
        Reposition();
    }

    private void Reposition()
    {
        if (_closed || Hwnd == IntPtr.Zero)
        {
            return;
        }

        DisplayInfo monitor = _monitors.GetActiveMonitor();
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        int widthPx = Math.Max(1, (int)Math.Ceiling(ActualWidth * scale));
        int heightPx = Math.Max(1, (int)Math.Ceiling(ActualHeight * scale));
        PixelRect work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        int x = work.X + ((work.Width - widthPx) / 2);
        int y = work.Y + (int)Math.Round(work.Height * 0.22) - (heightPx / 2);
        NativeMethods.MovePhysical(Hwnd, x, y);
    }
}

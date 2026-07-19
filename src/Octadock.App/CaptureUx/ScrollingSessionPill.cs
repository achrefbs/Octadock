using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Octadock.App.Theming;
using Octadock.App.Windows;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The scrolling-capture session pill: a small, glassy, NO-ACTIVATE status
/// window shown beside the selected viewport while frames are stitched. It
/// gives the session the visibility it previously lacked — live stitched
/// height, an explicit Finish, and a Cancel — without ever taking keyboard
/// focus away from the window the user is scrolling.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class ScrollingSessionPill : ToolWindowBase
{
    private static Brush GlassBackground => OctadockDesignTokens.Brushes.DockSurface;
    private static Brush GlassBorder => OctadockDesignTokens.Brushes.GlassBorderStrong;
    private static Brush TextBrush => OctadockDesignTokens.Brushes.Text;
    private static Brush AccentBrush => OctadockDesignTokens.Brushes.Accent;

    private readonly TextBlock _status;

    /// <summary>Raised when the user clicks the finish (check) button.</summary>
    public event EventHandler? FinishRequested;

    /// <summary>Raised when the user clicks the cancel (cross) button.</summary>
    public event EventHandler? CancelRequested;

    public ScrollingSessionPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _status = new TextBlock
        {
            Foreground = TextBrush,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
        };

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
        };

        Button finish = MakeGlyphButton("\uE73E", "Finish and stitch what you scrolled");
        finish.Click += (_, _) => FinishRequested?.Invoke(this, EventArgs.Empty);

        Button cancel = MakeGlyphButton("\uE711", "Cancel the scrolling capture");
        cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(_status);
        row.Children.Add(finish);
        row.Children.Add(cancel);

        Content = new Border
        {
            Background = GlassBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 8, 6),
            Child = row,
        };
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The user must keep OS focus on the window they are scrolling; the
        // pill's buttons still work without activation.
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    /// <summary>Shows the pill just outside the capture region (never inside it).</summary>
    public void ShowNear(PixelRect region, DisplayInfo monitor)
    {
        Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int pillW = (int)Math.Ceiling(ActualWidth * scale);
            int pillH = (int)Math.Ceiling(ActualHeight * scale);

            int x = region.X + ((region.Width - pillW) / 2);
            x = Math.Clamp(x, monitor.WorkArea.X + 8, Math.Max(monitor.WorkArea.X + 8, monitor.WorkArea.Right - pillW - 8));

            // Above the region when there is room; otherwise below; never inside.
            int y = region.Y - pillH - 12;
            if (y < monitor.WorkArea.Y + 8)
            {
                y = Math.Min(region.Bottom + 12, monitor.WorkArea.Bottom - pillH - 8);
            }

            NativeMethods.MovePhysical(Hwnd, x, y);
        });
    }

    /// <summary>Updates the live status line.</summary>
    public void Update(int stitchedPixels, bool hasGrown)
    {
        _status.Text = hasGrown
            ? $"Stitched {stitchedPixels:N0} px — keep scrolling, ✓ when done"
            : "Scroll the selected area now";
    }

    private static Button MakeGlyphButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = TextBrush,
            },
            ToolTip = tooltip,
            Width = 26,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
        };

        // Flat template so the pill stays clean (no Win32 chrome on hover).
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.Name = "Bd";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            OctadockDesignTokens.Brushes.Hover,
            "Bd"));
        template.Triggers.Add(hover);
        button.Template = template;
        return button;
    }
}

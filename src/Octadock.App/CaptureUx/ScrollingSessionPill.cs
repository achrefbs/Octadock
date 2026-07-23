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

        // Shared transient-pill capsule: glass, strong hairline, floating
        // elevation, following the theme (and reduced transparency) live.
        var shell = new Border { Child = row };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        Content = shell;
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

    // The shared glass rail glyph style supplies the hover/pressed/focus/disabled
    // states; local values keep the pill's compact footprint.
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
            Focusable = false,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.HoverActionButton");
        return button;
    }
}

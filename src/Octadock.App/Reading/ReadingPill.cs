using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Octadock.App.CaptureUx;
using Octadock.App.Windows;
using Octadock.Core.Geometry;

namespace Octadock.App.Reading;

/// <summary>
/// The read-aloud playback pill: same glass family as the dictation and
/// recording pills, shown bottom-center while text is being spoken. A pulsing
/// dot + status ("Reading clipboard — 0:42") + pause/resume and stop buttons.
/// NO-ACTIVATE so controlling playback never steals focus from what the user
/// is reading along with.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class ReadingPill : ToolWindowBase
{
    private static readonly SolidColorBrush GlassBackground =
        new(Color.FromArgb(0xE0, 0x0C, 0x12, 0x20));
    private static readonly SolidColorBrush GlassBorder =
        new(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TextBrush =
        new(Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9));
    private static readonly SolidColorBrush SpeakingBrush =
        new(Color.FromArgb(0xFF, 0x2D, 0xD4, 0xBF));

    private readonly System.Windows.Shapes.Ellipse _dot;
    private readonly TextBlock _status;
    private readonly TextBlock _pauseGlyph;
    private DisplayInfo? _currentDisplay;
    private bool _closed;

    /// <summary>Raised when the user clicks pause/resume.</summary>
    public event EventHandler? PauseResumeRequested;

    /// <summary>Raised when the user clicks stop.</summary>
    public event EventHandler? StopRequested;

    public ReadingPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = SpeakingBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 7, 0),
        };

        _status = new TextBlock
        {
            Text = "Reading…",
            Foreground = TextBrush,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        // U+E769 pause / U+E768 play / U+E71A stop (Segoe MDL2).
        (Button pause, _pauseGlyph) = MakeGlyphButton("", "Pause");
        pause.Click += (_, _) => PauseResumeRequested?.Invoke(this, EventArgs.Empty);
        (Button stop, _) = MakeGlyphButton("", "Stop reading");
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_status);
        row.Children.Add(pause);
        row.Children.Add(stop);

        Content = new Border
        {
            Background = GlassBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 8, 6),
            Child = row,
        };

        SizeChanged += (_, _) => RecenterOnCurrentMonitor();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    /// <summary>Shows the pill bottom-center on the given monitor.</summary>
    public void ShowNear(DisplayInfo monitor)
    {
        _currentDisplay = monitor;
        _closed = false;
        Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (_closed || !IsVisible)
            {
                return;
            }

            MoveToMonitor(monitor);
            StartPulse();
        });
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _dot.BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    /// <summary>Sets the status text (UI thread).</summary>
    public void SetStatus(string text) => _status.Text = text;

    /// <summary>Flips the pause button between pause and play glyphs.</summary>
    public void SetPaused(bool paused)
    {
        _pauseGlyph.Text = paused ? "" : "";
        if (paused)
        {
            _dot.BeginAnimation(OpacityProperty, null);
            _dot.Opacity = 0.35;
        }
        else
        {
            StartPulse();
        }
    }

    private void MoveToMonitor(DisplayInfo monitor)
    {
        double scale = monitor.DpiScale;
        int pillW = (int)Math.Ceiling(ActualWidth * scale);
        int pillH = (int)Math.Ceiling(ActualHeight * scale);
        int x = monitor.WorkArea.X + ((monitor.WorkArea.Width - pillW) / 2);
        int y = monitor.WorkArea.Bottom - pillH - 48;
        NativeMethods.MovePhysical(Hwnd, x, y);
    }

    private void RecenterOnCurrentMonitor()
    {
        if (_currentDisplay is { } monitor && IsVisible)
        {
            MoveToMonitor(monitor);
        }
    }

    private void StartPulse()
    {
        var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _dot.BeginAnimation(OpacityProperty, pulse);
    }

    private static (Button Button, TextBlock Glyph) MakeGlyphButton(string glyph, string tooltip)
    {
        var glyphText = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = TextBrush,
        };
        var button = new Button
        {
            Content = glyphText,
            ToolTip = tooltip,
            Width = 26,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
        };

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
            new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            "Bd"));
        template.Triggers.Add(hover);
        button.Template = template;
        return (button, glyphText);
    }
}

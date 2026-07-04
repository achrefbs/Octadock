using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Stt;

/// <summary>
/// The dictation indicator pill: a small, glassy, NO-ACTIVATE window shown at the
/// bottom-center of the active monitor while an utterance is recorded and
/// transcribed. Teal pulsing dot + status text ("Listening… 3.2s", "Transcribing…",
/// "Downloading the speech model… 43%") + a stop button. Capture-excluded via
/// <see cref="ToolWindowBase"/> and styled from the same glass constants as the
/// recording pill so the two feel like one family.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DictationPill : ToolWindowBase
{
    private static readonly SolidColorBrush GlassBackground =
        new(Color.FromArgb(0xE0, 0x11, 0x18, 0x27));
    private static readonly SolidColorBrush GlassBorder =
        new(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TextBrush =
        new(Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9));
    private static readonly SolidColorBrush ListeningBrush =
        new(Color.FromArgb(0xFF, 0x2D, 0xD4, 0xBF));

    private readonly System.Windows.Shapes.Ellipse _dot;
    private readonly TextBlock _status;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private DisplayInfo? _currentDisplay;
    private bool _closed;

    /// <summary>Raised when the user clicks stop.</summary>
    public event EventHandler? StopRequested;

    public DictationPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = ListeningBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 7, 0),
        };

        _status = new TextBlock
        {
            Text = "Listening…",
            Foreground = TextBrush,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        // U+E71A is the Segoe MDL2 "Stop" glyph — the same one the recording pill uses.
        Button stop = MakeGlyphButton("", "Stop and transcribe");
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_status);
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

        _followTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _followTimer.Tick += (_, _) => FollowActiveMonitor();
        SizeChanged += (_, _) => RecenterOnCurrentMonitor();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Clicking stop must not yank focus from the window being dictated into.
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    /// <summary>Shows the pill bottom-center on the active monitor.</summary>
    public void ShowNear(DisplayInfo monitor)
    {
        _currentMonitor = monitor.Id;
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
            _followTimer.Start();
        });
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _followTimer.Stop();
        _dot.BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    /// <summary>Sets the status text (called on the UI thread).</summary>
    public void SetStatus(string text) => _status.Text = text;

    private void FollowActiveMonitor()
    {
        try
        {
            DisplayInfo active = App.Services.GetRequiredService<IMonitorService>().GetActiveMonitor();
            if (active.Id == _currentMonitor)
            {
                return;
            }

            _currentMonitor = active.Id;
            _currentDisplay = active;
            MoveToMonitor(active);
        }
        catch
        {
            // Monitor polling is best-effort; try again on the next tick.
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
        return button;
    }
}

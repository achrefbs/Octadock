using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Theming;
using Octadock.Core.Abstractions;
using Octadock.App.Windows;
using Octadock.Core.Geometry;
using Octadock.Core.Recording;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The recording indicator pill: a small, glassy, NO-ACTIVATE window shown at
/// the top-center of the recorded monitor while a session runs. Red pulsing
/// dot + mm:ss timer + pause + stop — nothing else. <see cref="ToolWindowBase"/>
/// applies the user's own-UI capture policy, so the pill is excluded by default
/// and visible in desktop recordings when the user opts in.
/// This pill is also the styling seed for the planned permanent Octadock dock.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class RecordingPill : ToolWindowBase
{
    private static Brush TextBrush => OctadockDesignTokens.Brushes.Text;
    private static Brush RecordBrush => OctadockDesignTokens.Brushes.Danger;
    private static Brush PausedBrush => OctadockDesignTokens.Brushes.TextMuted;

    private readonly System.Windows.Shapes.Ellipse _dot;
    private readonly TextBlock _time;
    private readonly Button _pause;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private DisplayInfo? _currentDisplay;
    private bool _closed;
    private long _lastShownSecond = -1;

    /// <summary>Raised when the user clicks stop.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Raised when the user clicks pause/resume.</summary>
    public event EventHandler? PauseToggleRequested;

    public RecordingPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = RecordBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 7, 0),
        };

        _time = new TextBlock
        {
            Text = "00:00",
            Foreground = TextBrush,
            FontSize = 13,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        _pause = MakeGlyphButton("", "Pause");
        _pause.Click += (_, _) => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

        Button stop = MakeGlyphButton("", "Stop and save");
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
        row.Children.Add(_time);
        row.Children.Add(_pause);
        row.Children.Add(stop);

        // Shared transient-pill capsule: glass, strong hairline, floating
        // elevation. The style's DynamicResource setters keep theme swaps and
        // reduced transparency automatic.
        var shell = new Border { Child = row };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        Content = shell;

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

        // Clicking pause/stop must not yank focus from what is being recorded.
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    /// <summary>Shows the pill top-center on the recorded monitor.</summary>
    public void ShowOn(DisplayInfo monitor)
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

    /// <summary>Applies an engine progress notification (called on the UI thread).</summary>
    public void Update(RecordingProgress progress)
    {
        switch (progress.State)
        {
            case RecordingState.Countdown:
                _time.Text = progress.CountdownRemaining is { } remaining and > 0
                    ? $"Starting in {remaining}…"
                    : "Starting…";
                break;

            case RecordingState.Paused:
                _dot.Fill = PausedBrush;
                _pause.ToolTip = "Resume";
                SetGlyph(_pause, "");
                ShowElapsed(progress.ElapsedMs);
                break;

            case RecordingState.Recording:
                _dot.Fill = RecordBrush;
                _pause.ToolTip = "Pause";
                SetGlyph(_pause, "");
                ShowElapsed(progress.ElapsedMs);
                break;
        }
    }

    private void ShowElapsed(long elapsedMs)
    {
        long second = elapsedMs / 1000;
        if (second == _lastShownSecond)
        {
            return; // Progress arrives per frame; repaint once per second.
        }

        _lastShownSecond = second;
        _time.Text = TimeSpan.FromSeconds(second).ToString(@"mm\:ss");
    }

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
        int x = monitor.WorkArea.X + ((monitor.WorkArea.Width - pillW) / 2);
        int y = monitor.WorkArea.Y + 10;
        NativeMethods.MovePhysical(Hwnd, x, y);
    }

    private void RecenterOnCurrentMonitor()
    {
        if (_currentDisplay is { } monitor && IsVisible)
        {
            MoveToMonitor(monitor);
        }
    }

    private static bool MotionEnabled
        => Application.Current?.TryFindResource("Octadock.Motion.Enabled") is bool enabled
            ? enabled
            : SystemParameters.ClientAreaAnimation;

    private void StartPulse()
    {
        if (!MotionEnabled)
        {
            _dot.BeginAnimation(OpacityProperty, null);
            _dot.Opacity = 1;
            return;
        }

        var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _dot.BeginAnimation(OpacityProperty, pulse);
    }

    private static void SetGlyph(Button button, string glyph)
    {
        if (button.Content is TextBlock text)
        {
            text.Text = glyph;
        }
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

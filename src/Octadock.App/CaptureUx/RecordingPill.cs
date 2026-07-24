using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.Core.Abstractions;
using Octadock.App.Windows;
using Octadock.Core.Geometry;
using Octadock.Core.Recording;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The recording indicator pill: a compact, NO-ACTIVATE window shown at the
/// top-center of the recorded monitor while a session runs. A state icon and
/// explicit label accompany the timer, pause, and stop actions. <see cref="ToolWindowBase"/>
/// applies the user's own-UI capture policy, so the pill is excluded by default
/// and visible in desktop recordings when the user opts in.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class RecordingPill : ToolWindowBase
{
    private const string FontResource = "Octadock.Font";
    private const string BodyFontSizeResource = "Octadock.FontSize.Body";
    private const string CaptionFontSizeResource = "Octadock.FontSize.Caption";
    private const string IconSizeResource = "Octadock.Icon.Size.16";
    private const string TextResource = "Octadock.Brush.Text";
    private const string MutedTextResource = "Octadock.Brush.TextMuted";
    private const string RecordingResource = "Octadock.Brush.Danger";

    private readonly PackIconLucide _stateIcon;
    private readonly TextBlock _stateLabel;
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

        _stateIcon = MakeStatusIcon(PackIconLucideKind.Circle, RecordingResource);

        _stateLabel = MakeLabel("Recording", isMuted: false);
        _stateLabel.Margin = new Thickness(0, 0, 8, 0);

        _time = new TextBlock
        {
            Text = "00:00",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _time.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _time.SetResourceReference(TextBlock.FontSizeProperty, CaptionFontSizeResource);
        _time.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        _pause = MakeIconButton(PackIconLucideKind.Pause, "Pause");
        _pause.Click += (_, _) => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

        Button stop = MakeIconButton(PackIconLucideKind.Square, "Stop and save");
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(_stateIcon);
        row.Children.Add(_stateLabel);
        row.Children.Add(_time);
        row.Children.Add(_pause);
        row.Children.Add(stop);

        var shell = new Border { Child = row };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        AutomationProperties.SetName(shell, "Screen recording status");
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
        _stateIcon.BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    /// <summary>Applies an engine progress notification (called on the UI thread).</summary>
    public void Update(RecordingProgress progress)
    {
        switch (progress.State)
        {
            case RecordingState.Countdown:
                SetState(
                    PackIconLucideKind.Clock3,
                    "Starting",
                    MutedTextResource,
                    animate: false);
                _time.Text = progress.CountdownRemaining is { } remaining and > 0
                    ? $"{remaining}s"
                    : "soon";
                break;

            case RecordingState.Paused:
                SetState(
                    PackIconLucideKind.CirclePause,
                    "Paused",
                    MutedTextResource,
                    animate: false);
                _pause.ToolTip = "Resume";
                AutomationProperties.SetName(_pause, "Resume");
                SetIcon(_pause, PackIconLucideKind.Play);
                ShowElapsed(progress.ElapsedMs);
                break;

            case RecordingState.Recording:
                SetState(
                    PackIconLucideKind.Circle,
                    "Recording",
                    RecordingResource,
                    animate: true);
                _pause.ToolTip = "Pause";
                AutomationProperties.SetName(_pause, "Pause");
                SetIcon(_pause, PackIconLucideKind.Pause);
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
            _stateIcon.BeginAnimation(OpacityProperty, null);
            _stateIcon.Opacity = 1;
            return;
        }

        var pulse = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _stateIcon.BeginAnimation(OpacityProperty, pulse);
    }

    private void SetState(
        PackIconLucideKind icon,
        string label,
        string foregroundResource,
        bool animate)
    {
        bool stateChanged = _stateIcon.Kind != icon ||
            !string.Equals(_stateLabel.Text, label, StringComparison.Ordinal);
        if (!stateChanged)
        {
            return;
        }

        _stateIcon.Kind = icon;
        _stateIcon.SetResourceReference(PackIconLucide.ForegroundProperty, foregroundResource);
        _stateLabel.Text = label;
        _stateLabel.SetResourceReference(TextBlock.ForegroundProperty, animate ? TextResource : MutedTextResource);

        _stateIcon.BeginAnimation(OpacityProperty, null);
        _stateIcon.Opacity = 1;
        if (animate)
        {
            StartPulse();
        }
    }

    private static void SetIcon(Button button, PackIconLucideKind icon)
    {
        if (button.Content is PackIconLucide packIcon)
        {
            packIcon.Kind = icon;
        }
    }

    private static TextBlock MakeLabel(string text, bool isMuted)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        label.SetResourceReference(TextBlock.FontSizeProperty, BodyFontSizeResource);
        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            isMuted ? MutedTextResource : TextResource);
        return label;
    }

    private static PackIconLucide MakeStatusIcon(PackIconLucideKind kind, string foregroundResource)
    {
        var icon = new PackIconLucide
        {
            Kind = kind,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
        };
        icon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        icon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        icon.SetResourceReference(PackIconLucide.ForegroundProperty, foregroundResource);
        return icon;
    }

    private static Button MakeIconButton(PackIconLucideKind kind, string tooltip)
    {
        var icon = new PackIconLucide { Kind = kind };
        icon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        icon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        icon.SetResourceReference(PackIconLucide.ForegroundProperty, TextResource);

        var button = new Button
        {
            Content = icon,
            ToolTip = tooltip,
            Width = 34,
            Height = 34,
            Margin = new Thickness(1, 0, 0, 0),
            Focusable = false,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.HoverActionButton");
        AutomationProperties.SetName(button, tooltip);
        return button;
    }
}

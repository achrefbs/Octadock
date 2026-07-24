using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;
using Octadock.App.CaptureUx;
using Octadock.App.Windows;
using Octadock.Core.Geometry;

namespace Octadock.App.Reading;

/// <summary>
/// The read-aloud playback pill, shown bottom-center while text is being spoken.
/// A distinct state icon accompanies the status ("Reading clipboard — 0:42")
/// plus pause/resume and stop buttons.
/// NO-ACTIVATE so controlling playback never steals focus from what the user
/// is reading along with.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class ReadingPill : ToolWindowBase
{
    private const string FontResource = "Octadock.Font";
    private const string BodyFontSizeResource = "Octadock.FontSize.Body";
    private const string IconSizeResource = "Octadock.Icon.Size.16";
    private const string TextResource = "Octadock.Brush.Text";
    private const string MutedTextResource = "Octadock.Brush.TextMuted";
    private const string AccentResource = "Octadock.Brush.Accent";

    private readonly PackIconLucide _stateIcon;
    private readonly TextBlock _status;
    private readonly Button _pauseButton;
    private readonly PackIconLucide _pauseIcon;
    private DisplayInfo? _currentDisplay;
    private bool _closed;
    private bool _paused;
    private string _activeStatus = "Reading…";

    /// <summary>Raised when the user clicks pause/resume.</summary>
    public event EventHandler? PauseResumeRequested;

    /// <summary>Raised when the user clicks stop.</summary>
    public event EventHandler? StopRequested;

    public ReadingPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _stateIcon = new PackIconLucide
        {
            Kind = PackIconLucideKind.Volume2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
        };
        _stateIcon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        _stateIcon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        _stateIcon.SetResourceReference(PackIconLucide.ForegroundProperty, AccentResource);

        _status = new TextBlock
        {
            Text = _activeStatus,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _status.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _status.SetResourceReference(TextBlock.FontSizeProperty, BodyFontSizeResource);
        _status.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        (_pauseButton, _pauseIcon) = MakeIconButton(PackIconLucideKind.Pause, "Pause");
        _pauseButton.Click += (_, _) => PauseResumeRequested?.Invoke(this, EventArgs.Empty);
        (Button stop, _) = MakeIconButton(PackIconLucideKind.Square, "Stop reading");
        stop.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(_stateIcon);
        row.Children.Add(_status);
        row.Children.Add(_pauseButton);
        row.Children.Add(stop);

        var shell = new Border { Child = row };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        AutomationProperties.SetName(shell, "Read aloud status");
        Content = shell;

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
        _stateIcon.BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    /// <summary>Sets the status text (UI thread).</summary>
    public void SetStatus(string text)
    {
        _activeStatus = text;
        _status.Text = _paused ? AsPausedStatus(text) : text;
    }

    /// <summary>Flips the pause button between pause and play glyphs.</summary>
    public void SetPaused(bool paused)
    {
        if (_paused == paused)
        {
            return;
        }

        _paused = paused;
        _pauseIcon.Kind = paused ? PackIconLucideKind.Play : PackIconLucideKind.Pause;
        _pauseButton.ToolTip = paused ? "Resume" : "Pause";
        AutomationProperties.SetName(_pauseButton, paused ? "Resume" : "Pause");
        _status.Text = paused ? AsPausedStatus(_activeStatus) : _activeStatus;

        if (paused)
        {
            _stateIcon.BeginAnimation(OpacityProperty, null);
            _stateIcon.Kind = PackIconLucideKind.CirclePause;
            _stateIcon.SetResourceReference(PackIconLucide.ForegroundProperty, MutedTextResource);
            _stateIcon.Opacity = 1;
        }
        else
        {
            _stateIcon.Kind = PackIconLucideKind.Volume2;
            _stateIcon.SetResourceReference(PackIconLucide.ForegroundProperty, AccentResource);
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

    private static string AsPausedStatus(string text)
        => text.StartsWith("Reading", StringComparison.Ordinal)
            ? $"Paused{text["Reading".Length..]}"
            : $"Paused — {text}";

    private static (Button Button, PackIconLucide Icon) MakeIconButton(
        PackIconLucideKind kind,
        string tooltip)
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
        return (button, icon);
    }
}

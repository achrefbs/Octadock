using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Stt;

/// <summary>
/// The dictation indicator pill: a compact, NO-ACTIVATE window shown at the
/// bottom-center of the active monitor while an utterance is recorded and
/// transcribed. A distinct icon and status label communicate the pipeline
/// state without relying on color. In review-before-insert mode the
/// pill becomes an editable transcript with explicit Insert / Discard actions.
/// Capture-excluded via <see cref="ToolWindowBase"/>.
/// </summary>
/// <summary>What the dictation pill renders: listening (pulsing), working
/// (transcribing/inserting, solid), or review (editable transcript).</summary>
internal enum DictationPillPhase
{
    Listening,
    Working,
    Review,
}

[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DictationPill : ToolWindowBase
{
    private const string FontResource = "Octadock.Font";
    private const string BodyFontSizeResource = "Octadock.FontSize.Body";
    private const string CaptionFontSizeResource = "Octadock.FontSize.Caption";
    private const string IconSizeResource = "Octadock.Icon.Size.16";
    private const string TextResource = "Octadock.Brush.Text";
    private const string MutedTextResource = "Octadock.Brush.TextMuted";
    private const string AccentResource = "Octadock.Brush.Accent";
    private const string InputResource = "Octadock.Brush.InputBackground";
    private const string BorderResource = "Octadock.Brush.GlassBorderStrong";

    private readonly PackIconLucide _stateIcon;
    private readonly TextBlock _status;
    private readonly TextBlock _transcript;
    private readonly System.Windows.Documents.Run _stableRun;
    private readonly System.Windows.Documents.Run _volatileRun;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private DisplayInfo? _currentDisplay;
    private bool _closed;
    private bool _speechActive = true;
    private DictationPillPhase _phase = DictationPillPhase.Listening;
    private readonly Button _discardButton;
    private readonly Button _stopButton;
    private readonly TextBox _reviewBox;
    private readonly StackPanel _reviewRow;

    /// <summary>Raised when the user clicks stop.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Raised when the user clicks discard (stop without inserting).</summary>
    public event EventHandler? DiscardRequested;

    /// <summary>Raised when the user confirms the reviewed transcript (Insert button).</summary>
    public event EventHandler? InsertReviewRequested;

    public DictationPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _stateIcon = MakeStatusIcon(PackIconLucideKind.Mic, AccentResource);

        _status = new TextBlock
        {
            Text = "Listening…",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _status.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _status.SetResourceReference(TextBlock.FontSizeProperty, BodyFontSizeResource);
        _status.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        // Live transcript: stable text at full opacity, the volatile (still
        // re-decoding) tail dimmed. Hidden until the first partial arrives.
        _stableRun = new System.Windows.Documents.Run();
        _stableRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, TextResource);
        _volatileRun = new System.Windows.Documents.Run();
        _volatileRun.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, MutedTextResource);
        _transcript = new TextBlock
        {
            MaxWidth = 520,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(24, 2, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        _transcript.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _transcript.SetResourceReference(TextBlock.FontSizeProperty, CaptionFontSizeResource);
        _transcript.Inlines.Add(_stableRun);
        _transcript.Inlines.Add(_volatileRun);

        _discardButton = MakeIconButton(PackIconLucideKind.Trash2, "Discard dictation");
        _discardButton.Click += (_, _) => DiscardRequested?.Invoke(this, EventArgs.Empty);

        _stopButton = MakeIconButton(PackIconLucideKind.Square, "Stop and transcribe");
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(_stateIcon);
        row.Children.Add(_status);
        row.Children.Add(_discardButton);
        row.Children.Add(_stopButton);

        var column = new StackPanel { Orientation = Orientation.Vertical };
        column.Children.Add(row);
        column.Children.Add(_transcript);

        // Review-before-insert: an editable transcript with explicit Insert and
        // Discard actions. Hidden until ShowReviewTranscript().
        _reviewBox = new TextBox
        {
            MinWidth = 380,
            MaxWidth = 520,
            MaxHeight = 140,
            Margin = new Thickness(24, 8, 8, 0),
            Padding = new Thickness(10, 8, 10, 8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
        _reviewBox.SetResourceReference(Control.FontFamilyProperty, FontResource);
        _reviewBox.SetResourceReference(Control.FontSizeProperty, CaptionFontSizeResource);
        _reviewBox.SetResourceReference(Control.BackgroundProperty, InputResource);
        _reviewBox.SetResourceReference(Control.ForegroundProperty, TextResource);
        _reviewBox.SetResourceReference(TextBox.CaretBrushProperty, TextResource);
        _reviewBox.SetResourceReference(Control.BorderBrushProperty, BorderResource);
        AutomationProperties.SetName(_reviewBox, "Dictation transcript");
        _reviewBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DiscardRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        Button insertReview = MakeTextButton(
            "_Insert",
            PackIconLucideKind.Check,
            "Insert the reviewed transcript at the cursor");
        insertReview.Click += (_, _) => InsertReviewRequested?.Invoke(this, EventArgs.Empty);
        Button discardReview = MakeTextButton(
            "_Discard",
            PackIconLucideKind.Trash2,
            "Discard the reviewed transcript without inserting");
        discardReview.Click += (_, _) => DiscardRequested?.Invoke(this, EventArgs.Empty);

        _reviewRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(24, 8, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        _reviewRow.Children.Add(insertReview);
        _reviewRow.Children.Add(discardReview);

        column.Children.Add(_reviewBox);
        column.Children.Add(_reviewRow);

        var shell = new Border { Child = column };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        AutomationProperties.SetName(shell, "Dictation status");
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
        _stateIcon.BeginAnimation(OpacityProperty, null);
        base.OnClosed(e);
    }

    /// <summary>Sets the status text (called on the UI thread).</summary>
    public void SetStatus(string text) => _status.Text = text;

    /// <summary>
    /// Updates the live transcript line: stable text renders solid, the
    /// volatile tail dimmed. The line appears with the first non-empty partial
    /// and shows the trailing end when the transcript outgrows the pill.
    /// </summary>
    public void SetTranscript(string stable, string volatilePart)
    {
        _stableRun.Text = stable;
        _volatileRun.Text = volatilePart.Length > 0 && stable.Length > 0
            ? " " + volatilePart
            : volatilePart;
        _transcript.Visibility = stable.Length > 0 || volatilePart.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>The listening icon pulses while the VAD hears speech and dims in silence.</summary>
    public void SetSpeechActive(bool active)
    {
        if (_speechActive == active)
        {
            return;
        }

        _speechActive = active;
        if (_phase != DictationPillPhase.Listening)
        {
            return; // The phase owns the dot outside listening.
        }

        if (active)
        {
            StartPulse();
        }
        else
        {
            _stateIcon.BeginAnimation(OpacityProperty, null);
            _stateIcon.Opacity = 0.55;
        }
    }

    /// <summary>
    /// Switches the state icon and keeps the visible status text as the primary
    /// state description.
    /// </summary>
    public void SetPhase(DictationPillPhase phase)
    {
        if (_phase == phase)
        {
            return;
        }

        _phase = phase;
        switch (phase)
        {
            case DictationPillPhase.Listening:
                SetStateIcon(PackIconLucideKind.Mic, AccentResource);
                if (_speechActive)
                {
                    StartPulse();
                }

                break;
            case DictationPillPhase.Working:
                SetStateIcon(PackIconLucideKind.LoaderCircle, MutedTextResource);
                break;
            case DictationPillPhase.Review:
                SetStateIcon(PackIconLucideKind.FilePenLine, AccentResource);
                break;
        }
    }

    /// <summary>The (possibly user-edited) review transcript.</summary>
    public string EditedTranscript => _reviewBox.Text;

    /// <summary>
    /// Switches the pill into review-before-insert: the live transcript line
    /// and the stop/discard glyphs give way to an editable text box with
    /// explicit Insert / Discard buttons. The pill drops its no-activate band
    /// and takes keyboard focus; the controller restores the dictation
    /// target’s foreground before inserting.
    /// </summary>
    public void ShowReviewTranscript(string transcript)
    {
        SetPhase(DictationPillPhase.Review);
        SetStatus("Review and edit, then choose Insert or Discard");
        _transcript.Visibility = Visibility.Collapsed;
        _stopButton.Visibility = Visibility.Collapsed;
        _discardButton.Visibility = Visibility.Collapsed;
        _reviewBox.Text = transcript;
        _reviewBox.Visibility = Visibility.Visible;
        _reviewRow.Visibility = Visibility.Visible;

        if (Hwnd != IntPtr.Zero)
        {
            int style = NativeMethods.GetWindowLong(Hwnd, NativeMethods.GwlExStyle);
            _ = NativeMethods.SetWindowLong(Hwnd, NativeMethods.GwlExStyle, style & ~NativeMethods.WsExNoActivate);
        }

        Activate();
        _reviewBox.Focus();
        _reviewBox.CaretIndex = _reviewBox.Text.Length;
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

    private void SetStateIcon(PackIconLucideKind kind, string foregroundResource)
    {
        _stateIcon.BeginAnimation(OpacityProperty, null);
        _stateIcon.Opacity = 1;
        _stateIcon.Kind = kind;
        _stateIcon.SetResourceReference(PackIconLucide.ForegroundProperty, foregroundResource);
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

    private static Button MakeTextButton(
        string text,
        PackIconLucideKind iconKind,
        string tooltip)
    {
        var icon = new PackIconLucide { Kind = iconKind, Margin = new Thickness(0, 0, 6, 0) };
        icon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        icon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        icon.SetResourceReference(PackIconLucide.ForegroundProperty, TextResource);

        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        label.SetResourceReference(TextBlock.FontSizeProperty, CaptionFontSizeResource);
        label.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(icon);
        content.Children.Add(label);

        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            MinWidth = 80,
            Height = 34,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.GlassFooterAction");
        AutomationProperties.SetName(button, tooltip);
        return button;
    }
}

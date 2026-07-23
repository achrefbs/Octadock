using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
using Octadock.App.Theming;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Stt;

/// <summary>
/// The dictation indicator pill: a small, glassy, NO-ACTIVATE window shown at the
/// bottom-center of the active monitor while an utterance is recorded and
/// transcribed. The dot mirrors the pipeline state (teal pulse while listening,
/// solid amber while transcribing/inserting, solid teal in review) next to a
/// status text; a stop and a discard button. In review-before-insert mode the
/// pill becomes an editable transcript with explicit Insert / Discard actions.
/// Capture-excluded via <see cref="ToolWindowBase"/> and styled from the same
/// glass constants as the recording pill so the two feel like one family.
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
    private static Brush GlassBackground => OctadockDesignTokens.Brushes.DockSurface;
    private static Brush GlassBorder => OctadockDesignTokens.Brushes.GlassBorderStrong;
    private static Brush TextBrush => OctadockDesignTokens.Brushes.Text;
    private static Brush ListeningBrush => OctadockDesignTokens.Brushes.Accent;
    private static Brush WorkingBrush => OctadockDesignTokens.Brushes.Warning;
    private static Brush FieldBrush => OctadockDesignTokens.Brushes.Field;
    private static Brush VolatileTextBrush => OctadockDesignTokens.Brushes.TextMuted;

    private readonly System.Windows.Shapes.Ellipse _dot;
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

        // Live transcript: stable text at full opacity, the volatile (still
        // re-decoding) tail dimmed. Hidden until the first partial arrives.
        _stableRun = new System.Windows.Documents.Run { Foreground = TextBrush };
        _volatileRun = new System.Windows.Documents.Run { Foreground = VolatileTextBrush };
        _transcript = new TextBlock
        {
            FontSize = 12.5,
            MaxWidth = 520,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(18, 3, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        _transcript.Inlines.Add(_stableRun);
        _transcript.Inlines.Add(_volatileRun);

        // U+E74D is the Segoe MDL2 "Delete" glyph.
        _discardButton = MakeGlyphButton("", "Discard dictation");
        _discardButton.Click += (_, _) => DiscardRequested?.Invoke(this, EventArgs.Empty);

        // U+E71A is the Segoe MDL2 "Stop" glyph — the same one the recording pill uses.
        _stopButton = MakeGlyphButton("", "Stop and transcribe");
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_dot);
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
            Margin = new Thickness(18, 6, 8, 0),
            Padding = new Thickness(6, 4, 6, 4),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = FieldBrush,
            Foreground = TextBrush,
            CaretBrush = TextBrush,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            FontSize = 12.5,
            Visibility = Visibility.Collapsed,
        };
        _reviewBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DiscardRequested?.Invoke(this, EventArgs.Empty);
            }
        };

        Button insertReview = MakeTextButton("_Insert", "Insert the reviewed transcript at the cursor");
        insertReview.Click += (_, _) => InsertReviewRequested?.Invoke(this, EventArgs.Empty);
        Button discardReview = MakeTextButton("_Discard", "Discard the reviewed transcript without inserting");
        discardReview.Click += (_, _) => DiscardRequested?.Invoke(this, EventArgs.Empty);

        _reviewRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(18, 6, 8, 0),
            Visibility = Visibility.Collapsed,
        };
        _reviewRow.Children.Add(insertReview);
        _reviewRow.Children.Add(discardReview);

        column.Children.Add(_reviewBox);
        column.Children.Add(_reviewRow);

        Content = new Border
        {
            Background = GlassBackground,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 8, 6),
            Child = column,
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

    /// <summary>Dot pulses while the VAD hears speech and dims steady in silence.</summary>
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
            _dot.BeginAnimation(OpacityProperty, null);
            _dot.Opacity = 0.35;
        }
    }

    /// <summary>
    /// Switches the state dot: teal pulse while listening, solid amber while
    /// transcribing/inserting/cancelling, solid teal in review — the pipeline
    /// state stays readable at a glance.
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
                _dot.Fill = ListeningBrush;
                if (_speechActive)
                {
                    StartPulse();
                }

                break;
            case DictationPillPhase.Working:
                _dot.BeginAnimation(OpacityProperty, null);
                _dot.Fill = WorkingBrush;
                _dot.Opacity = 1.0;
                break;
            case DictationPillPhase.Review:
                _dot.BeginAnimation(OpacityProperty, null);
                _dot.Fill = ListeningBrush;
                _dot.Opacity = 1.0;
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

        button.Template = MakeButtonTemplate();
        return button;
    }

    private static Button MakeTextButton(string text, string tooltip)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = TextBrush,
            },
            ToolTip = tooltip,
            MinWidth = 64,
            Height = 24,
            Margin = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brushes.Transparent,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        button.Template = MakeButtonTemplate();
        return button;
    }

    private static ControlTemplate MakeButtonTemplate()
    {
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
        return template;
    }
}

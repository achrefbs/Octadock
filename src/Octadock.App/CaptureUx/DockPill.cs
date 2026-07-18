using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.App.Theming;
using Octadock.App.Tray;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The permanent Octadock dock: a small glass control bar that lives at the bottom
/// center of the primary monitor. Idle it is a breathing capture mark with the
/// wordmark; on hover it expands into the highest-frequency capture, voice,
/// Context, reviewed-AI, and settings actions.
/// Secondary utilities stay in the tray so the expanded dock remains a
/// calibrated instrument rather than a flat wall of actions. Draggable;
/// never takes keyboard focus; capture-excluded.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DockPill : ToolWindowBase
{
    private static readonly SolidColorBrush GlassBorder = OctadockDesignTokens.Brushes.GlassBorder;
    private static readonly SolidColorBrush TextBrush = OctadockDesignTokens.Brushes.Text;
    private static readonly SolidColorBrush AccentBrush = OctadockDesignTokens.Brushes.Accent;
    private static readonly SolidColorBrush CloudBrush = OctadockDesignTokens.Brushes.Cloud;
    private static readonly SolidColorBrush RecordBrush = OctadockDesignTokens.Brushes.Danger;

    private readonly Viewbox _logo;
    private readonly System.Windows.Shapes.Path _logoGlyph;
    private readonly TextBlock _wordmark;
    private readonly TextBlock _licenseBadge;
    private readonly StackPanel _actions;
    private readonly Border _root;
    private readonly System.Windows.Threading.DispatcherTimer _collapseTimer;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private System.Windows.Threading.DispatcherTimer? _licenseTimer;
    private Octadock.Core.Licensing.ILicenseGate? _licenseGate;
    private EventHandler<Octadock.Core.Licensing.LicenseState>? _licenseRefusedHandler;
    private PixelPoint _anchorCenter;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private bool _dragging;
    private bool _didDrag;
    private System.Windows.Point _dragStart;
    private PixelPoint _anchorAtDragStart;

    public DockPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _logoGlyph = new System.Windows.Shapes.Path
        {
            Data = CreateLogoGeometry(),
            Width = 20,
            Height = 20,
            Stretch = Stretch.None,
            Fill = AccentBrush,
        };
        var logoCanvas = new Canvas
        {
            Width = 20,
            Height = 20,
            IsHitTestVisible = false,
        };
        logoCanvas.Children.Add(_logoGlyph);
        _logo = new Viewbox
        {
            Width = 17,
            Height = 17,
            Stretch = Stretch.Uniform,
            Child = logoCanvas,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            Focusable = false,
            IsHitTestVisible = false,
        };

        _wordmark = new TextBlock
        {
            Text = "Octadock",
            Foreground = TextBrush,
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };

        // Ambient trial/license badge (WS5, R31). Collapsed during an early trial or a
        // valid license; appears only as the trial nears its end / has ended / is revoked.
        _licenseBadge = new TextBlock
        {
            Foreground = AccentBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Visibility = Visibility.Collapsed,
        };

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
        };
        BuildActions();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_logo);
        row.Children.Add(_wordmark);
        row.Children.Add(_licenseBadge);
        row.Children.Add(_actions);

        _root = new Border
        {
            Background = OctadockDesignTokens.Brushes.DockSurface,
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(9, 6, 8, 6),
            Child = row,
        };
        Content = _root;

        MouseEnter += (_, _) => Expand();
        MouseLeave += (_, _) => _collapseTimer!.Start();
        _collapseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(420),
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver)
            {
                Collapse();
            }
        };

        // Follow the cursor's monitor (Wispr Flow-style): if the
        // cursor has moved to another monitor and the dock is idle, hop to that
        // monitor's bottom-center. Always on.
        _followTimer = new System.Windows.Threading.DispatcherTimer
        {
            // Monitor changes are human-scale events. Polling every frame-like
            // interval kept the UI thread and DWM active while Octadock was idle.
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _followTimer.Tick += (_, _) => FollowActiveMonitor();

        // Drag anywhere on the capsule chrome (not the buttons).
        MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _didDrag = false;
            _dragStart = e.GetPosition(this);
            _anchorAtDragStart = _anchorCenter;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (_dragging && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                System.Windows.Point now = e.GetPosition(this);
                int dx = (int)Math.Round((now.X - _dragStart.X) * scale);
                int dy = (int)Math.Round((now.Y - _dragStart.Y) * scale);
                if (dx != 0 || dy != 0)
                {
                    _didDrag = true;
                }

                _anchorCenter = new PixelPoint(_anchorAtDragStart.X + dx, _anchorAtDragStart.Y + dy);
                Reanchor();
            }
        };
        MouseLeftButtonUp += (_, _) =>
        {
            bool moved = _dragging && _didDrag;
            _dragging = false;
            _didDrag = false;
            ReleaseMouseCapture();

            // Persist the new position only after a real drag so a plain click
            // never overwrites the saved anchor.
            if (moved)
            {
                PersistAnchor(_anchorCenter);
            }
        };
        SizeChanged += (_, _) => Reanchor();

        WireLicenseBadge();
    }

    private void WireLicenseBadge()
    {
        try
        {
            _licenseGate = App.Services.GetService(typeof(Octadock.Core.Licensing.ILicenseGate))
                as Octadock.Core.Licensing.ILicenseGate;
            if (_licenseGate is null)
            {
                return;
            }

            RefreshLicenseBadge();
            _licenseRefusedHandler = (_, _) => Dispatcher.BeginInvoke(RefreshLicenseBadge);
            _licenseGate.Refused += _licenseRefusedHandler;

            _licenseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _licenseTimer.Tick += (_, _) => RefreshLicenseBadge();
            _licenseTimer.Start();
        }
        catch (Exception)
        {
            // Ambient chrome only: a licensing hiccup must never break the dock.
        }
    }

    /// <summary>
    /// Builds the approved V2 optical mark on its native 20-by-20 grid. Keeping
    /// the geometry in WPF (rather than rasterizing the logo) preserves the
    /// tuned one-pixel port spacing at every monitor scale and lets the existing
    /// breathing and recording-state treatments animate a single visual.
    /// </summary>
    private static CombinedGeometry CreateLogoGeometry()
    {
        var mark = new GeometryGroup { FillRule = FillRule.Nonzero };

        // Eight ports, paired from the outside in, from the approved 20 px master.
        mark.Children.Add(new RectangleGeometry(new Rect(0.75, 11, 4, 2), 1, 1));
        mark.Children.Add(new RectangleGeometry(new Rect(15.25, 11, 4, 2), 1, 1));
        mark.Children.Add(new RectangleGeometry(new Rect(2.75, 12, 1.5, 5), 0.75, 0.75));
        mark.Children.Add(new RectangleGeometry(new Rect(15.75, 12, 1.5, 5), 0.75, 0.75));
        mark.Children.Add(new RectangleGeometry(new Rect(5.25, 12, 1.5, 5.5), 0.75, 0.75));
        mark.Children.Add(new RectangleGeometry(new Rect(13.25, 12, 1.5, 5.5), 0.75, 0.75));
        mark.Children.Add(new RectangleGeometry(new Rect(7.75, 12, 1.5, 5.5), 0.75, 0.75));
        mark.Children.Add(new RectangleGeometry(new Rect(10.75, 12, 1.5, 5.5), 0.75, 0.75));

        mark.Children.Add(Geometry.Parse(
            "M6,3 H14 C15.93,3 17.5,4.57 17.5,6.5 V12 " +
            "C17.5,12.83 16.83,13.5 16,13.5 H4 " +
            "C3.17,13.5 2.5,12.83 2.5,12 V6.5 C2.5,4.57 4.07,3 6,3 Z"));

        Geometry dockCutout = Geometry.Parse(
            "M6,6 H13 C14.1,6 15,6.9 15,8 C15,9.1 14.1,10 13,10 H6 Z");
        var result = new CombinedGeometry(GeometryCombineMode.Exclude, mark, dockCutout);
        result.Freeze();
        return result;
    }

    private void RefreshLicenseBadge()
    {
        if (_licenseGate is null)
        {
            return;
        }

        try
        {
            SetLicenseStatus(_licenseGate.State, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>Shows the trial/license badge only as the trial nears its end / has ended / is revoked.</summary>
    public void SetLicenseStatus(Octadock.Core.Licensing.LicenseState state, DateTimeOffset nowUtc)
    {
        (string? text, Brush tint, string tip) = DescribeBadge(state, nowUtc);
        if (text is null)
        {
            _licenseBadge.Visibility = Visibility.Collapsed;
            _licenseBadge.ToolTip = null;
            return;
        }

        _licenseBadge.Text = text;
        _licenseBadge.Foreground = tint;
        _licenseBadge.ToolTip = tip;
        _licenseBadge.Visibility = Visibility.Visible;
    }

    private static (string? Text, Brush Tint, string Tip) DescribeBadge(
        Octadock.Core.Licensing.LicenseState state, DateTimeOffset nowUtc)
    {
        switch (state.Mode)
        {
            case Octadock.Core.Licensing.LicenseMode.Trial:
                int days = Octadock.Core.Licensing.LicenseStatusFormatter.DaysLeft(state.TrialEndsUtc, nowUtc);
                return days <= 7
                    ? ($"Trial {days}d", AccentBrush, $"Trial — {days} day(s) left. Enter a license key in Settings → Account.")
                    : (null, AccentBrush, string.Empty);
            case Octadock.Core.Licensing.LicenseMode.TrialExpired:
                return ("Trial ended", RecordBrush, "Your trial ended — enter a license key in Settings → Account.");
            case Octadock.Core.Licensing.LicenseMode.TrialFrozen:
                return ("Clock?", RecordBrush, "Trial paused — this PC's clock looks wrong.");
            case Octadock.Core.Licensing.LicenseMode.Revoked:
                return ("Revoked", RecordBrush, "License revoked — enter a valid key in Settings → Account.");
            default:
                return (null, AccentBrush, string.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.MakeNoActivateToolWindow(Hwnd);

        // WPF layered windows do not provide reliable backdrop blur across the
        // supported Windows versions. The shared pseudo-glass recipe keeps the
        // capsule legible under RDP and falls back to solid surfaces when the
        // user disables transparency.
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _collapseTimer.Stop();
        _followTimer.Stop();
        _licenseTimer?.Stop();
        if (_licenseGate is not null && _licenseRefusedHandler is not null)
        {
            _licenseGate.Refused -= _licenseRefusedHandler;
        }

        _logo.BeginAnimation(OpacityProperty, null);
        _dragging = false;
        _didDrag = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Shows the dock. A position the user previously dragged to is restored when
    /// it still lands on a connected monitor; otherwise the dock sits bottom-center
    /// on the given monitor's work area.
    /// </summary>
    public void ShowOn(DisplayInfo monitor)
    {
        _currentMonitor = monitor.Id;
        _anchorCenter = ResolveSavedAnchor()
            ?? new PixelPoint(
                monitor.WorkArea.X + (monitor.WorkArea.Width / 2),
                monitor.WorkArea.Bottom - 34);
        Show();
        StartBreathing();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Reanchor);
        _followTimer.Start();
    }

    /// <summary>
    /// Returns the persisted anchor center when the user has one saved and it still
    /// lies within a connected monitor's bounds; otherwise null so the caller falls
    /// back to the default placement (a monitor may have been detached since).
    /// </summary>
    private static PixelPoint? ResolveSavedAnchor()
    {
        try
        {
            DockSettings dock = App.Services.GetRequiredService<ISettingsService>().Current.Dock;
            if (!dock.HasCustomAnchor)
            {
                return null;
            }

            var anchor = new PixelPoint(dock.AnchorX, dock.AnchorY);
            IReadOnlyList<DisplayInfo> monitors = App.Services.GetRequiredService<IMonitorService>().GetMonitors();
            foreach (DisplayInfo m in monitors)
            {
                if (m.Bounds.Contains(anchor))
                {
                    return anchor;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Fire-and-forget persistence of the dragged anchor; never throws to the caller.</summary>
    private static void PersistAnchor(PixelPoint anchor)
    {
        try
        {
            ISettingsService settings = App.Services.GetRequiredService<ISettingsService>();
            _ = settings.UpdateAsync(s => s with
            {
                Dock = s.Dock with
                {
                    HasCustomAnchor = true,
                    AnchorX = anchor.X,
                    AnchorY = anchor.Y,
                },
            });
        }
        catch (Exception)
        {
            // Persisting the dock position is best-effort; a failure must not
            // disrupt dragging.
        }
    }

    /// <summary>Dims the dock while the recording pill owns the screen.</summary>
    public void SetRecording(bool recording)
    {
        _logoGlyph.Fill = recording ? RecordBrush : AccentBrush;
        Opacity = recording ? 0.45 : 1.0;
        if (recording)
        {
            Collapse();
        }
    }

    private void Expand()
    {
        _collapseTimer.Stop();
        if (_actions.Visibility == Visibility.Visible)
        {
            return;
        }

        _wordmark.Visibility = Visibility.Collapsed;
        _actions.Visibility = Visibility.Visible;
        if (!SystemParameters.ClientAreaAnimation)
        {
            _actions.Opacity = 1;
            _actions.RenderTransform = Transform.Identity;
            return;
        }

        _actions.Opacity = 0;
        _actions.RenderTransform = new TranslateTransform(8, 0);
        _actions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        ((TranslateTransform)_actions.RenderTransform).BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private void Collapse()
    {
        _actions.Visibility = Visibility.Collapsed;
        _wordmark.Visibility = Visibility.Visible;
    }

    /// <summary>Keeps the capsule centered on its anchor as its size changes.</summary>
    private void Reanchor()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(ActualWidth * scale);
        int h = (int)Math.Ceiling(ActualHeight * scale);
        NativeMethods.MovePhysical(Hwnd, _anchorCenter.X - (w / 2), _anchorCenter.Y - (h / 2));
    }

    /// <summary>
    /// Wispr Flow-style monitor following: if the cursor's monitor differs from
    /// the dock's and the dock is idle (not mid-drag, not expanded, not hovered),
    /// hop to that monitor's bottom-center. Following always overrides a saved
    /// custom position; the placement is deliberately just bottom-center.
    /// </summary>
    private void FollowActiveMonitor()
    {
        // Never move while the user is interacting with the dock.
        if (_dragging || IsMouseOver || _actions.Visibility == Visibility.Visible)
        {
            return;
        }

        try
        {
            DisplayInfo active = App.Services.GetRequiredService<IMonitorService>().GetActiveMonitor();
            if (active.Id == _currentMonitor)
            {
                return;
            }

            _currentMonitor = active.Id;
            _anchorCenter = new PixelPoint(
                active.WorkArea.X + (active.WorkArea.Width / 2),
                active.WorkArea.Bottom - 34);
            Reanchor();
        }
        catch (Exception)
        {
            // Monitor enumeration is best-effort; a failure must not stop the timer
            // from trying again next tick.
        }
    }

    private void StartBreathing()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            _logo.Opacity = 1;
            return;
        }

        var breathe = new DoubleAnimation(1.0, 0.45, TimeSpan.FromMilliseconds(1300))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(1),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _logo.BeginAnimation(OpacityProperty, breathe);
    }

    /// <summary>
    /// Command Deck grouping: capture family · record · text &amp; voice ·
    /// library · settings, separated so each cluster reads as one intent.
    /// </summary>
    private void BuildActions()
    {
        // Capture is the primary cluster. Manual scrolling capture stays in the
        // HUD so the permanent bar remains short.
        AddAction(PackIconLucideKind.ScanLine, "Capture area", () => Coordinator.CaptureAreaAsync(DefaultAction()), guardPaused: true);
        AddAction(PackIconLucideKind.AppWindow, "Capture window", () => Coordinator.CaptureWindowAsync(DefaultAction()), guardPaused: true);
        AddAction(PackIconLucideKind.Monitor, "Capture full screen", () => Coordinator.CaptureFullscreenAsync(DefaultAction(), null, false), guardPaused: true);
        AddSeparator();

        // Local text and voice.
        AddAction(PackIconLucideKind.ScanText, "Extract text from a region (local OCR)", () =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            return App.Services.GetRequiredService<IOcrService>()
                .CaptureRegionTextAsync(settings.Current.Ocr.OutputMode, null);
        }, guardPaused: true);
        AddAction(PackIconLucideKind.Mic, "Dictate (local model by default)", () =>
            App.Services.GetRequiredService<DictationController>().ToggleAsync(), AccentBrush);
        AddSeparator();

        // Recording remains deliberately separated and honestly labeled.
        AddAction(PackIconLucideKind.CircleDot, "Record screen (Beta · MP4 video only)", () =>
        {
            RecordingController recorder = App.Services.GetRequiredService<RecordingController>();
            return !recorder.IsRecording && GuardPaused()
                ? Task.CompletedTask
                : recorder.ToggleAsync();
        }, RecordBrush);
        AddSeparator();

        // Personal libraries stay directly reachable from the capsule. They also
        // remain in the tray and keep their shortcuts, but are not buried there.
        AddAction(PackIconLucideKind.Eye, "Show or hide capture Shelf", () =>
        {
            App.Services.GetRequiredService<IShelfService>().ToggleVisibility();
            return Task.CompletedTask;
        });
        AddAction(PackIconLucideKind.History, "Open capture history", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowHistory();
            return Task.CompletedTask;
        });
        AddAction(PackIconLucideKind.ClipboardList, "Open clipboard history", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowClipboardHistory();
            return Task.CompletedTask;
        });

        // Context stays reachable without turning the capsule into a launcher.
        AddAction(PackIconLucideKind.Layers, "Open Context", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowContext();
            return Task.CompletedTask;
        });
        AddSeparator();

        AddAction(PackIconLucideKind.Settings2, "Settings", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowSettings();
            return Task.CompletedTask;
        });
    }

    private static ICaptureCoordinator Coordinator => App.Services.GetRequiredService<ICaptureCoordinator>();

    private static PostCaptureAction DefaultAction()
        => App.Services.GetRequiredService<ISettingsService>().Current.Capture.DefaultAction;

    private void AddSeparator()
        => _actions.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Background = GlassBorder,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

    private void AddAction(PackIconLucideKind kind, string tooltip, Func<Task> action, Brush? tint = null, bool guardPaused = false)
    {
        Button button = MakeButton(new PackIconLucide
        {
            Kind = kind,
            Width = 17,
            Height = 17,
            Foreground = tint ?? TextBrush,
        }, tooltip);
        button.Click += async (_, _) =>
        {
            Collapse();
            if (guardPaused && GuardPaused())
            {
                return;
            }

            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Services.GetService<INotificationService>()?.Notify(
                    "Action failed", ex.Message, NotificationKind.Error);
            }
        };
        _actions.Children.Add(button);
    }

    private static bool GuardPaused()
    {
        try
        {
            TrayIconController? tray = App.Services.GetService<TrayIconController>();
            if (tray?.IsPaused != true)
            {
                return false;
            }

            App.Services.GetService<INotificationService>()?.Notify(
                "Octadock is paused",
                "Resume capture from the tray menu.",
                NotificationKind.Info);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Button MakeButton(UIElement content, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Width = 30,
            Height = 28,
            Margin = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
        };
        ToolTipService.SetInitialShowDelay(button, 350);

        // The dock's actions are icon-only; name each for screen readers so the
        // product's face is operable by assistive tech, not just by hovering.
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.Name = "Bd";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            OctadockDesignTokens.Brushes.SurfaceOverlay,
            "Bd"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(
            Border.BackgroundProperty,
            OctadockDesignTokens.Brushes.ActiveAction,
            "Bd"));
        template.Triggers.Add(pressed);
        button.Template = template;
        return button;
    }

}

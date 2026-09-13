using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.App.Theming;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The permanent Octadock dock: a small glass control bar that lives at the bottom
/// center of the primary monitor. Idle it is a breathing capture mark with the
/// wordmark; on hover it expands into the highest-frequency capture actions
/// (area, window, full screen), voice, Shelf, History, and one More menu that
/// holds the lower-frequency capture modes and secondary destinations.
/// Secondary utilities stay in the tray so the expanded dock remains a
/// calibrated instrument rather than a flat wall of actions. Draggable;
/// never takes keyboard focus; capture-excluded.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DockPill : ToolWindowBase
{
    private const string DockSurfaceResource = "Octadock.Brush.GlassSurface";
    private const string GlassBorderResource = "Octadock.Brush.GlassBorder";
    private const string TextResource = "Octadock.Brush.Text";
    private const string AccentResource = "Octadock.Brush.Accent";
    private const string DangerResource = "Octadock.Brush.Danger";

    // The one edge rhythm shared with the Capture Shelf: the dock's bottom edge
    // rests 12 DIP above the work-area bottom, the same inset the Shelf keeps
    // from the screen edge, so both sit on a single rail instead of floating.
    private const double RestingEdgeGapDip = 12;
    private const double IdleHalfHeightDip = 17;

    private readonly Viewbox _logo;
    private readonly System.Windows.Shapes.Path _logoGlyph;
    private readonly TextBlock _wordmark;
    private readonly WrapPanel _actions;
    private readonly Border _root;
    private readonly System.Windows.Threading.DispatcherTimer _collapseTimer;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private PixelPoint _anchorCenter;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private bool _dragging;
    private bool _didDrag;
    private PixelPoint _dragStart;
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
        };
        _logoGlyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, AccentResource);
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
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };
        _wordmark.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        _actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
        };
        BuildActions();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_logo);
        row.Children.Add(_wordmark);
        row.Children.Add(_actions);

        _root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(12, 8, 11, 8),
            Child = row,
        };
        _root.SetResourceReference(Border.BackgroundProperty, DockSurfaceResource);
        _root.SetResourceReference(Border.BorderBrushProperty, GlassBorderResource);
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
            if (!_dragging && !IsMouseOver)
            {
                Collapse();
            }
        };

        // Follow the cursor's monitor (Wispr Flow-style): if the
        // cursor has moved to another monitor and the dock is idle, hop to that
        // monitor's saved position. Always on.
        _followTimer = new System.Windows.Threading.DispatcherTimer
        {
            // Monitor changes are human-scale events. Polling every frame-like
            // interval kept the UI thread and DWM active while Octadock was idle.
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _followTimer.Tick += (_, _) => FollowActiveMonitor();

        // Pointer deltas must be in desktop pixels: window-relative coordinates
        // feed our own movement back into the next event and cause oscillation.
        MouseLeftButtonDown += (_, e) =>
        {
            _collapseTimer.Stop();
            _dragging = true;
            _didDrag = false;
            _dragStart = NativeMethods.GetCursorPixel();
            _anchorAtDragStart = _anchorCenter;
            CaptureMouse();
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                FinishDrag();
                return;
            }
            PixelPoint now = NativeMethods.GetCursorPixel();
            int dx = now.X - _dragStart.X;
            int dy = now.Y - _dragStart.Y;
            if (!_didDrag && Math.Abs(dx) < 4 && Math.Abs(dy) < 4) return;
            _didDrag = true;
            _anchorCenter = new PixelPoint(_anchorAtDragStart.X + dx, _anchorAtDragStart.Y + dy);
            Reanchor();
            e.Handled = true;
        };
        MouseLeftButtonUp += (_, _) => FinishDrag();
        LostMouseCapture += (_, _) => FinishDrag();
        SizeChanged += (_, _) => Reanchor();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded, Reanchor);
    }

    private void FinishDrag()
    {
        if (!_dragging) return;
        bool moved = _didDrag;
        _dragging = false;
        _didDrag = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (moved)
        {
            DisplayInfo display = App.Services.GetRequiredService<IMonitorService>().GetMonitorFromPoint(_anchorCenter);
            _currentMonitor = display.Id;
            PersistAnchor(_anchorCenter, display);
        }
        if (!IsMouseOver) _collapseTimer.Start();
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
        _anchorCenter = ResolveSavedAnchor(monitor)
            ?? new PixelPoint(
                monitor.WorkArea.X + (monitor.WorkArea.Width / 2),
                monitor.WorkArea.Bottom - RestingAnchorOffset(monitor));
        Show();
        StartBreathing();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Reanchor);
        _followTimer.Start();
    }

    /// <summary>
    /// Physical-pixel distance from the work-area bottom to the resting anchor
    /// center: the shared edge gap plus half the idle capsule height, scaled for
    /// the monitor's DPI so the rail stays 12 DIP on every display.
    /// </summary>
    private static int RestingAnchorOffset(DisplayInfo monitor)
    {
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        return (int)Math.Round((RestingEdgeGapDip + IdleHalfHeightDip) * scale);
    }

    /// <summary>
    /// Returns the persisted anchor center when the user has one saved and it still
    /// lies within a connected monitor's bounds; otherwise null so the caller falls
    /// back to the default placement (a monitor may have been detached since).
    /// </summary>
    private static PixelPoint? ResolveSavedAnchor(DisplayInfo monitor)
        => WindowPlacement.RestoreAnchor(
            App.Services.GetRequiredService<ISettingsService>().Current.Dock, monitor);

    private static async void PersistAnchor(PixelPoint anchor, DisplayInfo monitor)
    {
        try
        {
            ISettingsService settings = App.Services.GetRequiredService<ISettingsService>();
            await settings.UpdateAsync(s =>
            {
                var anchors = new Dictionary<string, DockAnchor>(s.Dock.MonitorAnchors ?? [], StringComparer.OrdinalIgnoreCase)
                {
                    [monitor.Id.Value] = WindowPlacement.SaveAnchor(anchor, monitor.WorkArea),
                };
                return s with { Dock = s.Dock with
                {
                    HasCustomAnchor = true, AnchorX = anchor.X, AnchorY = anchor.Y,
                    MonitorAnchors = anchors,
                }};
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning("Could not save dock position: {0}", ex.Message);
        }
    }

    /// <summary>Dims the dock while the recording pill owns the screen.</summary>
    public void SetRecording(bool recording)
    {
        _logoGlyph.SetResourceReference(
            System.Windows.Shapes.Shape.FillProperty,
            recording ? DangerResource : AccentResource);
        Opacity = recording ? 0.45 : 1.0;
        if (recording)
        {
            Collapse();
        }
    }

    private void Expand()
    {
        _collapseTimer.Stop();
        if (_dragging || _actions.Visibility == Visibility.Visible)
        {
            return;
        }

        var display = App.Services.GetRequiredService<IMonitorService>().GetMonitorFromPoint(_anchorCenter);
        _actions.MaxWidth = Math.Max(64, display.WorkArea.Width / Math.Max(1, display.DpiScale) - 100);
        _wordmark.Visibility = Visibility.Collapsed;
        _actions.Visibility = Visibility.Visible;
        if (!MotionEnabled)
        {
            _actions.Opacity = 1;
            _actions.RenderTransform = Transform.Identity;
            return;
        }

        _actions.Opacity = 0;
        _actions.RenderTransform = new TranslateTransform(8, 0);
        _actions.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, MotionDuration("Octadock.Motion.Duration.Fast", 120)));
        ((TranslateTransform)_actions.RenderTransform).BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(8, 0, MotionDuration("Octadock.Motion.Duration.Normal", 200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private static bool MotionEnabled
        => Application.Current?.TryFindResource("Octadock.Motion.Enabled") is bool enabled
            ? enabled
            : SystemParameters.ClientAreaAnimation;

    private static TimeSpan MotionDuration(string resourceKey, double fallbackMilliseconds)
        => Application.Current?.TryFindResource(resourceKey) is Duration duration
            ? duration.TimeSpan
            : TimeSpan.FromMilliseconds(fallbackMilliseconds);

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
        if (Hwnd == IntPtr.Zero || w <= 0 || h <= 0) return;
        DisplayInfo monitor = App.Services.GetRequiredService<IMonitorService>().GetMonitorFromPoint(_anchorCenter);
        PixelRect bounds = WindowPlacement.Fit(
            new PixelRect(_anchorCenter.X - w / 2, _anchorCenter.Y - h / 2, w, h), monitor.WorkArea, 4);
        NativeMethods.MovePhysical(Hwnd, bounds.X, bounds.Y);
    }

    /// <summary>
    /// Wispr Flow-style monitor following: if the cursor's monitor differs from
    /// the dock's and the dock is idle (not mid-drag, not expanded, not hovered),
    /// restore that monitor's own saved position, falling back to bottom-center.
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
            _anchorCenter = ResolveSavedAnchor(active) ?? new PixelPoint(
                active.WorkArea.X + (active.WorkArea.Width / 2),
                active.WorkArea.Bottom - RestingAnchorOffset(active));
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
        if (!MotionEnabled)
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
    /// The minimal Dock: the high-frequency captures (Area, Window, Full screen)
    /// are always-visible buttons, then Dictate, Shelf, and History. Every
    /// lower-frequency capture mode and secondary destination lives in the single
    /// More menu — there is no separate expand-arrow affordance. The reviewed
    /// handoff stays source-bound (Shelf/History/Context/tray), so it is
    /// deliberately not a dock button. Fixed-size/aspect lives in the Advanced
    /// capture settings, not on the dock.
    /// </summary>
    private void BuildActions()
    {
        AddAction(PackIconLucideKind.ScanLine, "Capture area", () =>
            Coordinator.CaptureAreaAsync(DefaultAction()), AccentResource);
        AddAction(PackIconLucideKind.AppWindow, "Capture window", () =>
            Coordinator.CaptureWindowAsync(DefaultAction()));
        AddAction(PackIconLucideKind.Fullscreen, "Capture full screen", () =>
            Coordinator.CaptureFullscreenAsync(DefaultAction(), null, false));
        AddSeparator();

        AddAction(PackIconLucideKind.Mic, "Dictate (local model by default)", () =>
            App.Services.GetRequiredService<DictationController>().ToggleAsync(), AccentResource);
        AddSeparator();

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
        AddSeparator();

        AddMoreButton();
    }

    /// <summary>
    /// The one overflow affordance: lower-frequency capture modes on top,
    /// secondary destinations below. No capture mode earns its own arrow.
    /// </summary>
    private void AddMoreButton()
    {
        Button more = MakeButton(
            MakeIcon(PackIconLucideKind.Ellipsis),
            "More — capture modes, Clipboard, Context, Settings, Account, Exit");
        more.ContextMenu = BuildMoreMenu();
        more.Click += (_, _) => OpenMenu(more);
        _actions.Children.Add(more);
    }

    private static void OpenMenu(Button anchor)
    {
        if (anchor.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private ContextMenu BuildMoreMenu()
    {
        var menu = new ContextMenu();

        // Lower-frequency capture modes; Area/Window/Full screen stay on the dock.
        AddMenuItem(menu, "All monitors", () => Coordinator.CaptureFullscreenAsync(DefaultAction(), null, true));
        AddMenuItem(menu, "Previous area", () => Coordinator.CapturePreviousAreaAsync(DefaultAction()));
        AddMenuItem(menu, "Timer", () => App.Services.GetRequiredService<CaptureCoordinator>()
            .CaptureSelfTimerAsync(DefaultAction(), null));
        AddMenuItem(menu, "Scrolling — manual vertical (Beta)", () => Coordinator.CaptureScrollingAsync(DefaultAction()));
        AddMenuItem(menu, "OCR — extract text from a region (local)", () =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            return App.Services.GetRequiredService<IOcrService>()
                .CaptureRegionTextAsync(settings.Current.Ocr.OutputMode, null);
        });
        // Truthful wording: MP4 video with optional microphone/system audio, still Beta.
        AddMenuItem(menu, "Record (Beta)", () => App.Services.GetRequiredService<RecordingController>().ToggleAsync());
        menu.Items.Add(new Separator());

        AddMenuItem(menu, "Clipboard history", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowClipboardHistory();
            return Task.CompletedTask;
        });
        AddMenuItem(menu, "Context", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowContext();
            return Task.CompletedTask;
        });
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Settings…", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowSettings();
            return Task.CompletedTask;
        });
        AddMenuItem(menu, "About Octadock", () =>
        {
            App.Services.GetRequiredService<WindowPresenter>().ShowAbout();
            return Task.CompletedTask;
        });
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Exit Octadock", () =>
        {
            Application.Current?.Shutdown();
            return Task.CompletedTask;
        });
        return menu;
    }

    private void AddMenuItem(ContextMenu menu, string header, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        System.Windows.Automation.AutomationProperties.SetName(item, header);
        item.Click += async (_, _) =>
        {
            Collapse();
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                NotifyActionFailed(ex);
            }
        };
        menu.Items.Add(item);
    }

    private static void NotifyActionFailed(Exception ex)
        => App.Services.GetService<INotificationService>()?.Notify("Action failed", ex.Message, NotificationKind.Error);

    private static ICaptureCoordinator Coordinator => App.Services.GetRequiredService<ICaptureCoordinator>();

    private static PostCaptureAction DefaultAction()
        => App.Services.GetRequiredService<ISettingsService>().Current.Capture.DefaultAction;

    private void AddSeparator()
    {
        var separator = new Border
        {
            Width = 1,
            Height = 18,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        separator.SetResourceReference(Border.BackgroundProperty, GlassBorderResource);
        _actions.Children.Add(separator);
    }

    private void AddAction(
        PackIconLucideKind kind,
        string tooltip,
        Func<Task> action,
        string? tintResource = null)
    {
        Button button = MakeButton(MakeIcon(kind, tintResource), tooltip);
        button.Click += async (_, _) =>
        {
            Collapse();
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                NotifyActionFailed(ex);
            }
        };
        _actions.Children.Add(button);
    }

    private static PackIconLucide MakeIcon(PackIconLucideKind kind, string? tintResource = null)
    {
        var icon = new PackIconLucide
        {
            Kind = kind,
            Width = 16,
            Height = 16,
        };
        icon.SetResourceReference(PackIconLucide.ForegroundProperty, tintResource ?? TextResource);
        return icon;
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
            Focusable = false,
        };
        ToolTipService.SetInitialShowDelay(button, 350);

        // The dock's actions are icon-only; name each for screen readers so the
        // product's face is operable by assistive tech, not just by hovering.
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);

        // The shared glass rail glyph style supplies the hover/pressed/focus/
        // disabled states; local values keep the dock's tuned hit-targets.
        button.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.HoverActionButton");
        return button;
    }

}

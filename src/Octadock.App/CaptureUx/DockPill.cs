using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The permanent Octadock Dock: a stable, compact action rail that follows the
/// active monitor. High-frequency actions are always visible, so nothing appears
/// unexpectedly on hover and capture modes are never hidden behind a chevron.
/// Secondary capture tools live on the Capture Shelf through
/// <see cref="ICaptureActionService"/>. Draggable; never takes keyboard focus;
/// capture visibility follows the user's capture setting.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DockPill : ToolWindowBase
{
    private const string DockSurfaceResource = "Octadock.Brush.GlassSurface";
    private const string GlassBorderResource = "Octadock.Brush.GlassBorder";
    private const string TextResource = "Octadock.Brush.Text";
    private const string AccentResource = "Octadock.Brush.Accent";
    private const string DangerResource = "Octadock.Brush.Danger";

    private readonly Viewbox _logo;
    private readonly System.Windows.Shapes.Path _logoGlyph;
    private readonly TextBlock _licenseBadge;
    private readonly StackPanel _actions;
    private readonly Border _root;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private System.Windows.Threading.DispatcherTimer? _licenseTimer;
    private Octadock.Core.Licensing.ILicenseGate? _licenseGate;
    private Octadock.Core.Licensing.ActivationService? _activationService;
    private EventHandler<Octadock.Core.Licensing.LicenseState>? _licenseRefusedHandler;
    private PixelPoint _anchorCenter;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private bool _dragging;
    private bool _didDrag;
    private System.Windows.Point _dragStart;
    private PixelPoint _anchorAtDragStart;
    private Button? _recordButton;
    private PackIconLucide? _recordIcon;

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
            Margin = new Thickness(2, 0, 5, 0),
            Focusable = false,
            IsHitTestVisible = false,
        };

        // Ambient trial/license badge (WS5, R31). Collapsed during an early trial or a
        // valid license; appears only as the trial nears its end / has ended / is revoked.
        _licenseBadge = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 3, 0),
            Visibility = Visibility.Collapsed,
        };
        _licenseBadge.SetResourceReference(TextBlock.ForegroundProperty, AccentResource);

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        BuildActions();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_logo);
        row.Children.Add(_actions);
        row.Children.Add(_licenseBadge);

        _root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(5),
            Child = row,
        };
        _root.SetResourceReference(Border.BackgroundProperty, DockSurfaceResource);
        _root.SetResourceReference(Border.BorderBrushProperty, GlassBorderResource);
        Content = _root;

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

            _activationService = App.Services.GetService(typeof(Octadock.Core.Licensing.ActivationService))
                as Octadock.Core.Licensing.ActivationService;
            if (_activationService is not null)
            {
                _activationService.EntitlementStored += OnEntitlementStored;
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

    private void OnEntitlementStored(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(RefreshLicenseBadge);

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
        (string? text, string tintResource, string tip) = DescribeBadge(state, nowUtc);
        if (text is null)
        {
            _licenseBadge.Visibility = Visibility.Collapsed;
            _licenseBadge.ToolTip = null;
            return;
        }

        _licenseBadge.Text = text;
        _licenseBadge.SetResourceReference(TextBlock.ForegroundProperty, tintResource);
        _licenseBadge.ToolTip = tip;
        _licenseBadge.Visibility = Visibility.Visible;
    }

    private static (string? Text, string TintResource, string Tip) DescribeBadge(
        Octadock.Core.Licensing.LicenseState state, DateTimeOffset nowUtc)
    {
        switch (state.Mode)
        {
            case Octadock.Core.Licensing.LicenseMode.Trial:
                int days = Octadock.Core.Licensing.LicenseStatusFormatter.DaysLeft(state.TrialEndsUtc, nowUtc);
                return days <= 7
                    ? ($"Trial {days}d", AccentResource, $"Trial — {days} day(s) left. Enter a license key in Settings → Account.")
                    : (null, AccentResource, string.Empty);
            case Octadock.Core.Licensing.LicenseMode.TrialExpired:
                return ("Trial ended", DangerResource, "Your trial ended — enter a license key in Settings → Account.");
            case Octadock.Core.Licensing.LicenseMode.TrialFrozen:
                return ("Clock?", DangerResource, "Trial paused — this PC's clock looks wrong.");
            case Octadock.Core.Licensing.LicenseMode.Revoked:
                return ("Revoked", DangerResource, "License revoked — enter a valid key in Settings → Account.");
            default:
                return (null, AccentResource, string.Empty);
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
        _followTimer.Stop();
        _licenseTimer?.Stop();
        if (_licenseGate is not null && _licenseRefusedHandler is not null)
        {
            _licenseGate.Refused -= _licenseRefusedHandler;
        }

        if (_activationService is not null)
        {
            _activationService.EntitlementStored -= OnEntitlementStored;
        }

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

    /// <summary>Turns the Record action into an explicit Stop state while recording.</summary>
    public void SetRecording(bool recording)
    {
        _logoGlyph.SetResourceReference(
            System.Windows.Shapes.Shape.FillProperty,
            recording ? DangerResource : AccentResource);

        if (_recordIcon is not null)
        {
            _recordIcon.Kind = recording ? PackIconLucideKind.Square : PackIconLucideKind.Video;
            _recordIcon.SetResourceReference(
                PackIconLucide.ForegroundProperty,
                recording ? DangerResource : TextResource);
        }

        if (_recordButton is not null)
        {
            string label = recording ? "Stop screen recording (Beta)" : "Start screen recording (Beta)";
            SetAccessibleLabel(_recordButton, label);
        }
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
        if (_dragging || IsMouseOver)
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

    /// <summary>
    /// The stable Dock rail: Area, Window, Full screen and Record are direct
    /// capture actions, followed by Dictate and Shelf. Secondary capture tools
    /// are intentionally absent; <see cref="CaptureActionCatalog.ShelfActions"/>
    /// is the single placement seam for the Capture Shelf.
    /// </summary>
    private void BuildActions()
    {
        foreach (CaptureActionDefinition definition in CaptureActionCatalog.DockActions)
        {
            PackIconLucideKind iconKind = definition.Action switch
            {
                CaptureAction.Area => PackIconLucideKind.ScanLine,
                CaptureAction.Window => PackIconLucideKind.AppWindow,
                CaptureAction.FullScreen => PackIconLucideKind.Fullscreen,
                CaptureAction.Record => PackIconLucideKind.Video,
                _ => throw new InvalidOperationException(
                    $"{definition.Action} is not a supported Dock capture action."),
            };

            PackIconLucide icon = MakeIcon(iconKind);
            Button button = AddAction(
                icon,
                definition.AutomationName,
                () => App.Services.GetRequiredService<ICaptureActionService>()
                    .ExecuteAsync(definition.Action));

            if (definition.Action == CaptureAction.Record)
            {
                _recordButton = button;
                _recordIcon = icon;
            }
        }

        AddGroupGap();

        AddAction(
            MakeIcon(PackIconLucideKind.Mic),
            "Start or stop dictation (local model by default)",
            () => App.Services.GetRequiredService<DictationController>().ToggleAsync());
        AddAction(MakeIcon(PackIconLucideKind.PanelRight), "Show or hide capture Shelf", () =>
        {
            App.Services.GetRequiredService<IShelfService>().ToggleVisibility();
            return Task.CompletedTask;
        });
    }

    private static void NotifyActionFailed(Exception ex)
        => App.Services.GetService<INotificationService>()?.Notify("Action failed", ex.Message, NotificationKind.Error);

    private void AddGroupGap()
    {
        _actions.Children.Add(new Border
        {
            Width = 5,
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        });
    }

    private Button AddAction(
        UIElement content,
        string tooltip,
        Func<Task> action)
    {
        Button button = MakeButton(content, tooltip);
        button.Click += async (_, _) =>
        {
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
        return button;
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
            Width = 34,
            Height = 32,
            Margin = new Thickness(0),
            Focusable = false,
        };
        ToolTipService.SetInitialShowDelay(button, 350);

        SetAccessibleLabel(button, tooltip);

        // The shared glass rail glyph style supplies the hover/pressed/focus/
        // disabled states; local values keep the dock's tuned hit-targets.
        button.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.HoverActionButton");
        return button;
    }

    private static void SetAccessibleLabel(Button button, string label)
    {
        button.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
    }

}

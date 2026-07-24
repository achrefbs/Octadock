using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The Capture Shelf window. A borderless, own-UI-policy-aware, always-on-top surface that
/// docks to the configured corner (<see cref="ShelfSettings.Anchor"/>, default
/// bottom-left) of the active monitor's <em>work area</em> — so it sits above the
/// taskbar — with <see cref="ShelfSettings.MarginDip"/> of breathing room. It hosts the
/// <see cref="ShelfViewModel"/>'s stacked cards, repositions itself when the display
/// topology changes or its own size changes, suspends the auto-close timer while hovered,
/// and hides when the shelf empties.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public partial class ShelfWindow : ToolWindowBase
{
    private readonly ShelfViewModel _viewModel;
    private readonly IMonitorService _monitors;
    private readonly ISettingsService _settings;
    private readonly ShelfCaptureActionDispatcher _captureActions;
    private readonly DispatcherTimer _followTimer;
    private ShelfPeekState _peekState;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private HwndSource? _source;

    private const int WmNcHitTest = 0x0084;
    internal const double EdgeTabHitWidthDip = 32;
    internal const double EdgeTabHitHeightDip = 32;
    internal const double EdgeTabVisualWidthDip = 22;
    internal const double EdgeTabVisualHeightDip = 30;
    private static readonly IntPtr HtTransparent = new(-1);

    /// <summary>Creates the shelf window bound to its view model.</summary>
    internal ShelfWindow(
        ShelfViewModel viewModel,
        IMonitorService monitors,
        ISettingsService settings,
        ShelfCaptureActionDispatcher captureActions)
    {
        _viewModel = viewModel;
        _monitors = monitors;
        _settings = settings;
        _captureActions = captureActions;

        InitializeComponent();
        DataContext = _viewModel;
        AllowDrop = true;
        _followTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _followTimer.Tick += (_, _) => FollowActiveMonitor();

        _viewModel.Emptied += OnEmptied;
        _monitors.MonitorsChanged += OnMonitorsChanged;
        _settings.Changed += OnSettingsChanged;

        Loaded += OnLoaded;
        SizeChanged += (_, _) => Reposition();
        MouseEnter += (_, _) => _viewModel.SetHoverSuspended(true);
        MouseLeave += (_, _) => _viewModel.SetHoverSuspended(false);
        DragEnter += OnFileDragOver;
        DragOver += OnFileDragOver;
        Drop += OnFileDrop;
        Closed += OnClosed;
    }

    /// <summary>The view model driving the shelf.</summary>
    public ShelfViewModel ViewModel => _viewModel;

    /// <summary>The five compact secondary modes rendered by the Shelf action strip.</summary>
    internal IReadOnlyList<ShelfCaptureActionDescriptor> CaptureActions
        => ShelfCaptureActionCatalog.CompactActions;

    /// <summary>The screen corner currently occupied by the fixed edge tab.</summary>
    internal ShelfAnchor EffectiveAnchor => _settings.Current.Shelf.Anchor;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
        UpdatePeekVisual();
        Reposition();
        _followTimer.Start();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(Hwnd);
        _source?.AddHook(WindowProc);
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // WPF applies the per-monitor DPI change before the queued layout pass.
        // Re-anchor afterwards so right/bottom corners use the new physical size
        // instead of the pre-transition SizeToContent measurement.
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(Reposition));
    }

    /// <summary>Re-anchors the window to the configured corner of the active monitor.</summary>
    public void Reposition()
    {
        if (Hwnd == IntPtr.Zero)
        {
            return;
        }

        DisplayInfo monitor = _monitors.GetActiveMonitor();
        _currentMonitor = monitor.Id;
        ShelfAnchor anchor = _settings.Current.Shelf.Anchor;
        PixelPoint position = CalculatePosition(anchor, monitor);

        // SizeToContent owns the size; move-only so WPF's layout and our anchor agree.
        NativeMethods.MovePhysical(Hwnd, position.X, position.Y);
    }

    private PixelPoint CalculatePosition(ShelfAnchor anchor, DisplayInfo monitor)
    {
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;

        // Current window size in physical pixels.
        int widthPx = (int)Math.Round(ActualWidth * scale);
        int heightPx = (int)Math.Round(ActualHeight * scale);
        if (widthPx <= 0 || heightPx <= 0)
        {
            // Fall back to the desired size before the first arrange pass completes.
            widthPx = (int)Math.Round(
                Math.Max(ActualWidth, _viewModel.ShellWidth + EdgeTabHitWidthDip) * scale);
            heightPx = (int)Math.Round(Math.Max(ActualHeight, 160) * scale);
        }

        int marginPx = (int)Math.Round(_settings.Current.Shelf.MarginDip * scale);
        int tabWidthPx = Math.Max(1, (int)Math.Round(EdgeTabHitWidthDip * scale));
        int tabHeightPx = Math.Max(1, (int)Math.Round(EdgeTabHitHeightDip * scale));
        return CalculateWindowPositionForEdgeTab(
            anchor,
            monitor.WorkArea,
            widthPx,
            heightPx,
            tabWidthPx,
            tabHeightPx,
            marginPx);
    }

    internal static PixelPoint CalculateAnchoredPosition(
        ShelfAnchor anchor,
        PixelRect work,
        int widthPx,
        int heightPx,
        int marginPx)
    {
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);
        marginPx = Math.Max(0, marginPx);

        int x = anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft
            ? work.X + marginPx
            : work.Right - widthPx - marginPx;
        int y = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight
            ? work.Y + marginPx
            : work.Bottom - heightPx - marginPx;

        // Keep the Shelf fully inside the work area even on small/portrait
        // monitors or when the work area starts at a negative coordinate.
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - widthPx));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - heightPx));
        return new PixelPoint(x, y);
    }

    /// <summary>
    /// Positions the entire Shelf by the attached edge tab, not by a changing
    /// expanded/collapsed window rectangle. The tab's physical screen point is
    /// therefore invariant across SizeToContent transitions.
    /// </summary>
    internal static PixelPoint CalculateWindowPositionForEdgeTab(
        ShelfAnchor anchor,
        PixelRect work,
        int windowWidthPx,
        int windowHeightPx,
        int tabWidthPx,
        int tabHeightPx,
        int marginPx)
    {
        windowWidthPx = Math.Max(1, windowWidthPx);
        windowHeightPx = Math.Max(1, windowHeightPx);
        tabWidthPx = Math.Clamp(tabWidthPx, 1, windowWidthPx);
        tabHeightPx = Math.Clamp(tabHeightPx, 1, windowHeightPx);

        PixelPoint tabPosition = CalculateAnchoredPosition(
            anchor,
            work,
            tabWidthPx,
            tabHeightPx,
            marginPx);
        int localTabX = IsLeftAnchor(anchor) ? 0 : windowWidthPx - tabWidthPx;
        int localTabY = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight
            ? 0
            : windowHeightPx - tabHeightPx;
        return new PixelPoint(tabPosition.X - localTabX, tabPosition.Y - localTabY);
    }

    private void OnMonitorsChanged(object? sender, EventArgs e)
    {
        _currentMonitor = MonitorId.Unknown;
        Dispatcher.BeginInvoke(new Action(Reposition));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdatePeekVisual();
            Reposition();
        });
    }

    private void FollowActiveMonitor()
    {
        // Moving an HWND across DPI boundaries while the user is interacting can
        // fight WM_DPICHANGED and drag/drop. Monitor changes therefore snap only
        // while the Shelf is idle, matching the Dock pill's follow behavior.
        if (!IsVisible ||
            IsMouseOver ||
            Mouse.Captured is not null ||
            PeekButton.IsKeyboardFocusWithin)
        {
            return;
        }

        try
        {
            DisplayInfo active = _monitors.GetActiveMonitor();
            if (active.Id == _currentMonitor)
            {
                return;
            }

            _currentMonitor = active.Id;
            PixelPoint position = CalculatePosition(_settings.Current.Shelf.Anchor, active);
            NativeMethods.MovePhysical(Hwnd, position.X, position.Y);
        }
        catch (Exception)
        {
            // Display enumeration is best-effort. Keep the timer alive and retry
            // on its next low-frequency tick.
        }
    }

    /// <summary>Header "Clear all": closes every card (captures stay in history).</summary>
    private void OnClearAll(object sender, RoutedEventArgs e) => _viewModel.CloseAll();

    /// <summary>Header gear: opens Settings on the Shelf tab.</summary>
    private void OnOpenSettings(object sender, RoutedEventArgs e)
        => App.Services.GetService<IWindowPresenter>()?.ShowSettings("Shelf");

    private async void OnCaptureActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button
            {
                DataContext: ShelfCaptureActionDescriptor descriptor,
            })
        {
            return;
        }

        CaptureActionStrip.IsEnabled = false;
        try
        {
            await _captureActions.ExecuteAsync(descriptor.Action).ConfigureAwait(true);
        }
        finally
        {
            CaptureActionStrip.IsEnabled = true;
        }

        e.Handled = true;
    }

    private void OnEmptied(object? sender, EventArgs e)
    {
        // Nothing to show: hide (do not Close) so the singleton window can be reused.
        Hide();
    }

    /// <summary>Gently calls attention to the fixed tab when a hidden Shelf receives another capture.</summary>
    public void NotifyCaptureAdded()
    {
        UpdatePeekVisual();
        if (_peekState == ShelfPeekState.Expanded || !MotionEnabled)
        {
            return;
        }

        var scale = new ScaleTransform(1, 1);
        PeekButton.RenderTransformOrigin = new Point(0.5, 0.5);
        PeekButton.RenderTransform = scale;
        var pulse = new DoubleAnimation(1, 1.08, MotionDuration("Octadock.Motion.Duration.Fast", 120))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    /// <summary>Restores any peeked state without recreating the window.</summary>
    public void RestoreFromPeek()
    {
        if (!IsVisible)
        {
            Show();
        }

        RestorePeek();
    }

    private void OnPeekClick(object sender, RoutedEventArgs e)
    {
        ApplyPeekBehavior();
        e.Handled = true;
    }

    private void ApplyPeekBehavior()
    {
        if (_peekState != ShelfPeekState.Expanded)
        {
            RestorePeek();
            return;
        }

        CollapseToEdge();
    }

    private void CollapseToEdge()
    {
        _peekState = ShelfPeekState.Collapsed;
        UpdatePeekVisual();
        if (!MotionEnabled)
        {
            ShelfChrome.Visibility = Visibility.Collapsed;
            Dispatcher.BeginInvoke(Reposition);
            return;
        }

        var duration = new Duration(MotionDuration("Octadock.Motion.Duration.Fast", 120));
        double edgeOffset = IsLeftAnchor(_settings.Current.Shelf.Anchor) ? -6 : 6;
        var opacity = new DoubleAnimation(1, 0, duration);
        opacity.Completed += (_, _) =>
        {
            ShelfChrome.Visibility = Visibility.Collapsed;
            ShelfChrome.Opacity = 1;
            ShelfScale.ScaleX = 1;
            ShelfScale.ScaleY = 1;
            ShelfTranslate.X = 0;
            Dispatcher.BeginInvoke(Reposition);
        };
        ShelfChrome.BeginAnimation(OpacityProperty, opacity);
        ShelfScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, duration));
        ShelfScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, duration));
        ShelfTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, edgeOffset, duration));
    }

    private void RestorePeek(bool animate = true)
    {
        if (_peekState == ShelfPeekState.Expanded)
        {
            Reposition();
            return;
        }

        _peekState = ShelfPeekState.Expanded;
        ShelfChrome.IsHitTestVisible = true;
        ShelfChrome.Visibility = Visibility.Visible;
        ShelfChrome.BeginAnimation(OpacityProperty, null);
        ShelfScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ShelfScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ShelfTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        UpdatePeekVisual();
        UpdateLayout();
        Reposition();
        if (animate && MotionEnabled)
        {
            double edgeOffset = IsLeftAnchor(_settings.Current.Shelf.Anchor) ? -6 : 6;
            ShelfChrome.Opacity = 0;
            ShelfScale.ScaleX = 0.97;
            ShelfScale.ScaleY = 0.97;
            ShelfTranslate.X = edgeOffset;
            var duration = new Duration(MotionDuration("Octadock.Motion.Duration.Normal", 200));
            ShelfChrome.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
            ShelfScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, duration));
            ShelfScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, duration));
            ShelfTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(edgeOffset, 0, duration));
        }
        else
        {
            ShelfChrome.Opacity = 1;
            ShelfScale.ScaleX = 1;
            ShelfScale.ScaleY = 1;
            ShelfTranslate.X = 0;
        }
    }

    private void UpdatePeekVisual()
    {
        PeekButton.ApplyTemplate();
        ShelfAnchor anchor = _settings.Current.Shelf.Anchor;
        bool left = IsLeftAnchor(anchor);
        bool top = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight;
        bool compact = _peekState == ShelfPeekState.Collapsed;

        PeekButton.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PeekButton.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        ShelfChrome.Margin = left
            ? new Thickness(EdgeTabHitWidthDip, 0, 0, 0)
            : new Thickness(0, 0, EdgeTabHitWidthDip, 0);
        ShelfChrome.RenderTransformOrigin = new Point(left ? 0 : 1, top ? 0 : 1);
        ShelfChrome.CornerRadius = JoinedShellCornerRadius(anchor);

        if (PeekButton.Template.FindName("TabGlass", PeekButton) is Border glass)
        {
            glass.Width = EdgeTabVisualWidthDip;
            glass.Height = EdgeTabVisualHeightDip;
            glass.HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            glass.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            glass.CornerRadius = compact
                ? new CornerRadius(7)
                : left
                    ? new CornerRadius(7, 0, 0, 7)
                    : new CornerRadius(0, 7, 7, 0);
        }

        if (PeekButton.Template.FindName("PeekIcon", PeekButton) is PackIconLucide icon)
        {
            icon.Kind = (left, compact) switch
            {
                (true, false) => PackIconLucideKind.ChevronLeft,
                (true, true) => PackIconLucideKind.ChevronRight,
                (false, false) => PackIconLucideKind.ChevronRight,
                _ => PackIconLucideKind.ChevronLeft,
            };
            icon.SetResourceReference(
                ForegroundProperty,
                !compact
                    ? "Octadock.Brush.TextSecondaryStrong"
                    : "Octadock.Brush.Accent");
        }

        if (PeekButton.Template.FindName("PeekIndicator", PeekButton) is FrameworkElement indicator)
        {
            indicator.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        PeekButton.Width = EdgeTabHitWidthDip;
        PeekButton.Height = EdgeTabHitHeightDip;
        PeekButton.ToolTip = compact
            ? $"Show capture Shelf ({_viewModel.Items.Count} captures)"
            : "Hide capture Shelf";
        System.Windows.Automation.AutomationProperties.SetName(
            PeekButton,
            compact
                ? $"Show capture Shelf; {_viewModel.Items.Count} captures"
                : "Hide capture Shelf");
    }

    private static bool MotionEnabled
        => Application.Current?.TryFindResource("Octadock.Motion.Enabled") is bool enabled
            ? enabled
            : SystemParameters.ClientAreaAnimation;

    private static TimeSpan MotionDuration(string resourceKey, double fallbackMilliseconds)
        => Application.Current?.TryFindResource(resourceKey) is Duration duration
            ? duration.TimeSpan
            : TimeSpan.FromMilliseconds(fallbackMilliseconds);

    private static bool IsLeftAnchor(ShelfAnchor anchor)
        => anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft;

    private static CornerRadius JoinedShellCornerRadius(ShelfAnchor anchor) => anchor switch
    {
        ShelfAnchor.TopLeft => new CornerRadius(0, 12, 12, 12),
        ShelfAnchor.TopRight => new CornerRadius(12, 0, 12, 12),
        ShelfAnchor.BottomRight => new CornerRadius(12, 12, 0, 12),
        _ => new CornerRadius(12, 12, 12, 0),
    };

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest)
        {
            return IntPtr.Zero;
        }

        int packed = unchecked((int)lParam.ToInt64());
        var point = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
        if (ContainsScreenPoint(PeekButton, point) ||
            (ShelfChrome.Visibility == Visibility.Visible &&
             ShelfChrome.IsHitTestVisible &&
             ContainsScreenPoint(ShelfChrome, point)))
        {
            return IntPtr.Zero;
        }

        // SizeToContent includes the strip reserved for the edge tab. Pass
        // pointer input through the unpainted part of that strip instead of
        // creating an invisible click-blocking rectangle beside the cards.
        handled = true;
        return HtTransparent;
    }

    private static bool ContainsScreenPoint(FrameworkElement element, Point point)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        Point topLeft = element.PointToScreen(new Point(0, 0));
        DpiScale dpi = VisualTreeHelper.GetDpi(element);
        return new Rect(
                topLeft.X,
                topLeft.Y,
                element.ActualWidth * dpi.DpiScaleX,
                element.ActualHeight * dpi.DpiScaleY)
            .Contains(point);
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        // The Shelf accepts Octadock captures and recordings only. Arbitrary
        // files are refused at the drag-over boundary so the cursor never
        // suggests an import that no longer exists.
        e.Effects = ShelfDragPayload.TryGetCaptureId(e.Data, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (ShelfDragPayload.TryGetCaptureId(e.Data, out Guid captureId) &&
            _viewModel.TryActivateCapture(captureId))
        {
            // Returning an Octadock capture to its own Shelf is a reorder/no-op,
            // never an external import with a fresh ID.
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _followTimer.Stop();
        _source?.RemoveHook(WindowProc);
        _source = null;
        _viewModel.Emptied -= OnEmptied;
        _monitors.MonitorsChanged -= OnMonitorsChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    private enum ShelfPeekState
    {
        Expanded = 0,
        Collapsed,
    }
}

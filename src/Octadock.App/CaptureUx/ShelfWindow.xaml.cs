using System.Diagnostics;
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
    private readonly DispatcherTimer _followTimer;
    private ShelfAnchor? _temporaryAnchor;
    private ShelfPeekState _peekState;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private bool _peekPressed;
    private bool _peekDragged;
    private PixelPoint _peekPressCursor;
    private PixelRect _peekPressWindow;
    private DispatcherTimer? _moveAnimation;
    private HwndSource? _source;

    private const int WmNcHitTest = 0x0084;
    private const double EdgeTabWidthDip = 32;
    private static readonly IntPtr HtTransparent = new(-1);

    /// <summary>Creates the shelf window bound to its view model.</summary>
    public ShelfWindow(
        ShelfViewModel viewModel,
        IMonitorService monitors,
        ISettingsService settings)
    {
        _viewModel = viewModel;
        _monitors = monitors;
        _settings = settings;

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

    /// <summary>The screen corner currently occupied by the fixed edge tab.</summary>
    internal ShelfAnchor EffectiveAnchor => _temporaryAnchor ?? _settings.Current.Shelf.Anchor;

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
        ShelfAnchor anchor = _temporaryAnchor ?? _settings.Current.Shelf.Anchor;
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
            widthPx = (int)Math.Round(Math.Max(ActualWidth, 260) * scale);
            heightPx = (int)Math.Round(Math.Max(ActualHeight, 160) * scale);
        }

        int marginPx = (int)Math.Round(_settings.Current.Shelf.MarginDip * scale);
        return CalculateAnchoredPosition(anchor, monitor.WorkArea, widthPx, heightPx, marginPx);
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

    private void OnMonitorsChanged(object? sender, EventArgs e)
    {
        _currentMonitor = MonitorId.Unknown;
        Dispatcher.BeginInvoke(new Action(Reposition));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_peekState == ShelfPeekState.Moved)
            {
                _temporaryAnchor = null;
                _peekState = ShelfPeekState.Expanded;
                UpdatePeekVisual();
            }

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
            _peekPressed ||
            _moveAnimation is not null)
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
            ShelfAnchor anchor = _temporaryAnchor ?? _settings.Current.Shelf.Anchor;
            PixelPoint position = CalculatePosition(anchor, active);
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

    private void OnEmptied(object? sender, EventArgs e)
    {
        // Nothing to show: hide (do not Close) so the singleton window can be reused.
        Hide();
    }

    /// <summary>Gently calls attention to the fixed tab when a hidden Shelf receives another capture.</summary>
    public void NotifyCaptureAdded()
    {
        UpdatePeekVisual();
        if (_peekState == ShelfPeekState.Expanded || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var scale = new ScaleTransform(1, 1);
        PeekButton.RenderTransformOrigin = new Point(0.5, 0.5);
        PeekButton.RenderTransform = scale;
        var pulse = new DoubleAnimation(1, 1.16, TimeSpan.FromMilliseconds(130))
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

    private void OnPeekMouseDown(object sender, MouseButtonEventArgs e)
    {
        _peekPressed = true;
        _peekDragged = false;
        _peekPressCursor = NativeMethods.GetCursorPixel();
        _peekPressWindow = NativeMethods.GetPhysicalWindowRect(Hwnd);
        PeekButton.CaptureMouse();
        e.Handled = true;
    }

    private void OnPeekMouseMove(object sender, MouseEventArgs e)
    {
        if (!_peekPressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        PixelPoint cursor = NativeMethods.GetCursorPixel();
        int dx = cursor.X - _peekPressCursor.X;
        int dy = cursor.Y - _peekPressCursor.Y;
        if (!_peekDragged && Math.Abs(dx) < 7 && Math.Abs(dy) < 7)
        {
            return;
        }

        if (!_peekDragged)
        {
            _peekDragged = true;
            if (_peekState is ShelfPeekState.Collapsed or ShelfPeekState.Faded)
            {
                RestorePeek(animate: false);
                UpdateLayout();
                _peekPressCursor = cursor;
                _peekPressWindow = NativeMethods.GetPhysicalWindowRect(Hwnd);
                dx = 0;
                dy = 0;
            }
            else if (_peekState == ShelfPeekState.Moved)
            {
                _peekState = ShelfPeekState.Expanded;
            }
        }

        NativeMethods.MovePhysical(
            Hwnd,
            _peekPressWindow.X + dx,
            _peekPressWindow.Y + dy);
        e.Handled = true;
    }

    private void OnPeekMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_peekPressed)
        {
            return;
        }

        bool dragged = _peekDragged;
        _peekPressed = false;
        _peekDragged = false;
        if (PeekButton.IsMouseCaptured)
        {
            PeekButton.ReleaseMouseCapture();
        }

        if (dragged)
        {
            DisplayInfo monitor = _monitors.GetActiveMonitor();
            ShelfAnchor target = FindNearestAnchor(NativeMethods.GetCursorPixel(), monitor.WorkArea);
            _temporaryAnchor = target == _settings.Current.Shelf.Anchor ? null : target;
            _peekState = _temporaryAnchor is null ? ShelfPeekState.Expanded : ShelfPeekState.Moved;
            UpdatePeekVisual();
            AnimateToAnchor(target, monitor);
        }
        else
        {
            ApplyPeekBehavior();
        }

        e.Handled = true;
    }

    private void OnPeekLostCapture(object sender, MouseEventArgs e)
    {
        if (!_peekPressed)
        {
            return;
        }

        _peekPressed = false;
        _peekDragged = false;
        Reposition();
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
        if (!SystemParameters.ClientAreaAnimation)
        {
            ShelfChrome.Visibility = Visibility.Collapsed;
            Dispatcher.BeginInvoke(Reposition);
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(145));
        var opacity = new DoubleAnimation(1, 0, duration);
        opacity.Completed += (_, _) =>
        {
            ShelfChrome.Visibility = Visibility.Collapsed;
            ShelfChrome.Opacity = 1;
            ShelfScale.ScaleX = 1;
            ShelfScale.ScaleY = 1;
            ShelfTranslate.Y = 0;
            Dispatcher.BeginInvoke(Reposition);
        };
        ShelfChrome.BeginAnimation(OpacityProperty, opacity);
        ShelfScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.9, duration));
        ShelfScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.9, duration));
        ShelfTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 8, duration));
    }

    private void FadeInPlace()
    {
        _peekState = ShelfPeekState.Faded;
        ShelfChrome.IsHitTestVisible = false;
        UpdatePeekVisual();
        ShelfChrome.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(ShelfChrome.Opacity, 0.1, TimeSpan.FromMilliseconds(150)));
    }

    private void MoveToClearCorner()
    {
        DisplayInfo monitor = _monitors.GetActiveMonitor();
        ShelfAnchor current = _temporaryAnchor ?? _settings.Current.Shelf.Anchor;
        ShelfAnchor target = ChooseClearAnchor(current, NativeMethods.GetCursorPixel(), monitor.WorkArea);
        _temporaryAnchor = target;
        _peekState = ShelfPeekState.Moved;
        UpdatePeekVisual();
        AnimateToAnchor(target, monitor);
    }

    private void RestorePeek(bool animate = true)
    {
        ShelfPeekState previous = _peekState;
        if (previous == ShelfPeekState.Expanded)
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
        ShelfTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        if (previous == ShelfPeekState.Moved)
        {
            _temporaryAnchor = null;
            UpdatePeekVisual();
            AnimateToAnchor(_settings.Current.Shelf.Anchor, _monitors.GetActiveMonitor());
            return;
        }

        _temporaryAnchor = null;
        UpdateLayout();
        Reposition();
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            ShelfChrome.Opacity = 0;
            ShelfScale.ScaleX = 0.9;
            ShelfScale.ScaleY = 0.9;
            ShelfTranslate.Y = 8;
            var duration = new Duration(TimeSpan.FromMilliseconds(170));
            ShelfChrome.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
            ShelfScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, duration));
            ShelfScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, duration));
            ShelfTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration));
        }
        else
        {
            ShelfChrome.Opacity = 1;
            ShelfScale.ScaleX = 1;
            ShelfScale.ScaleY = 1;
            ShelfTranslate.Y = 0;
        }

        UpdatePeekVisual();
    }

    private void UpdatePeekVisual()
    {
        PeekButton.ApplyTemplate();
        ShelfAnchor anchor = _temporaryAnchor ?? _settings.Current.Shelf.Anchor;
        bool left = anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft;
        bool top = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight;
        bool compact = _peekState == ShelfPeekState.Collapsed;

        PeekButton.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        PeekButton.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        ShelfChrome.Margin = left
            ? new Thickness(EdgeTabWidthDip, 0, 0, 0)
            : new Thickness(0, 0, EdgeTabWidthDip, 0);

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

        if (PeekButton.Template.FindName("PeekCount", PeekButton) is TextBlock count)
        {
            count.Text = _viewModel.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            count.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        PeekButton.Width = EdgeTabWidthDip;
        PeekButton.ToolTip = compact ? "Show capture Shelf" : "Hide capture Shelf";
        System.Windows.Automation.AutomationProperties.SetName(
            PeekButton,
            compact ? "Show capture Shelf" : "Hide capture Shelf");
    }

    private void AnimateToAnchor(ShelfAnchor anchor, DisplayInfo monitor)
    {
        PixelRect from = NativeMethods.GetPhysicalWindowRect(Hwnd);
        PixelPoint to = CalculatePosition(anchor, monitor);
        _moveAnimation?.Stop();
        if (!SystemParameters.ClientAreaAnimation || from.Width <= 0 || from.Height <= 0)
        {
            NativeMethods.MovePhysical(Hwnd, to.X, to.Y);
            return;
        }

        var watch = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _moveAnimation = timer;
        timer.Tick += (_, _) =>
        {
            double progress = Math.Clamp(watch.Elapsed.TotalMilliseconds / 220.0, 0, 1);
            double eased = 1 - Math.Pow(1 - progress, 3);
            int x = (int)Math.Round(from.X + ((to.X - from.X) * eased));
            int y = (int)Math.Round(from.Y + ((to.Y - from.Y) * eased));
            NativeMethods.MovePhysical(Hwnd, x, y);
            if (progress >= 1)
            {
                timer.Stop();
                if (ReferenceEquals(_moveAnimation, timer))
                {
                    _moveAnimation = null;
                }
            }
        };
        timer.Start();
    }

    internal static ShelfAnchor ChooseClearAnchor(ShelfAnchor current, PixelPoint cursor, PixelRect workArea)
    {
        return AllAnchors()
            .Where(anchor => anchor != current)
            .OrderByDescending(anchor => DistanceSquared(cursor, AnchorPoint(anchor, workArea)))
            .First();
    }

    internal static ShelfAnchor FindNearestAnchor(PixelPoint point, PixelRect workArea)
        => AllAnchors()
            .OrderBy(anchor => DistanceSquared(point, AnchorPoint(anchor, workArea)))
            .First();

    private static IEnumerable<ShelfAnchor> AllAnchors()
    {
        yield return ShelfAnchor.BottomLeft;
        yield return ShelfAnchor.BottomRight;
        yield return ShelfAnchor.TopLeft;
        yield return ShelfAnchor.TopRight;
    }

    private static PixelPoint AnchorPoint(ShelfAnchor anchor, PixelRect workArea)
        => new(
            anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft ? workArea.X : workArea.Right,
            anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight ? workArea.Y : workArea.Bottom);

    private static long DistanceSquared(PixelPoint first, PixelPoint second)
    {
        long dx = first.X - second.X;
        long dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }

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
        _moveAnimation?.Stop();
        _moveAnimation = null;
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
        Moved,
        Faded,
    }
}

using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// A capture-excluded overlay covering one monitor that highlights the top-level
/// window under the cursor (outline + title/process label) via
/// <see cref="IWindowPicker.WindowAt"/>. One overlay is created per monitor so each
/// HWND is born on its target display and uses that monitor's DPI transform. Clicking
/// commits the highlighted window as a <see cref="RegionSelectionResult"/> (handle hex
/// + bounds). Escape cancels. A <see cref="DispatcherTimer"/> polls the cursor because
/// the overlay covers every window and the OS delivers move events sparsely over a
/// fully-covered desktop.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class WindowPickerOverlay : ToolWindowBase
{
    private readonly IWindowPicker _picker;
    private readonly DisplayInfo _monitor;
    private readonly DispatcherTimer _pollTimer;
    private readonly ILogger? _logger;

    private CandidateWindow? _current;
    private Matrix _fromDevice = Matrix.Identity; // physical px -> DIP for this window
    private bool _raised;
    private bool _trackFaulted;

    /// <summary>Raised once, when the user clicks a window or cancels.</summary>
    public event EventHandler<RegionSelectionResult>? Completed;

    /// <summary>The monitor this overlay covers.</summary>
    public DisplayInfo Monitor => _monitor;

    /// <summary>
    /// Creates a window picker overlay for one monitor.
    /// <paramref name="primaryDpiScale"/> is used only for the pre-HWND placement hint,
    /// matching the area-selection overlay's mixed-DPI creation path.
    /// </summary>
    public WindowPickerOverlay(
        IWindowPicker picker,
        DisplayInfo monitor,
        double primaryDpiScale = 1.0,
        ILogger? logger = null)
    {
        _picker = picker;
        _monitor = monitor;
        _logger = logger;
        ShowActivated = true;
        InitializeComponent();

        double scale = Scale;
        Width = monitor.Bounds.Width / scale;
        Height = monitor.Bounds.Height / scale;

        double hintScale = primaryDpiScale <= 0 ? 1.0 : primaryDpiScale;
        Left = (monitor.Bounds.X + (monitor.Bounds.Width / 2.0)) / hintScale - (Width / 2.0);
        Top = (monitor.Bounds.Y + (monitor.Bounds.Height / 2.0)) / hintScale - (Height / 2.0);

        _pollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _pollTimer.Tick += (_, _) => Track();

        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += (_, _) => Track();
        KeyDown += OnKeyDown;
        Closed += (_, _) => _pollTimer.Stop();
    }

    private double Scale => _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        double scaleX = newDpi.DpiScaleX <= 0 ? 1.0 : newDpi.DpiScaleX;
        double scaleY = newDpi.DpiScaleY <= 0 ? 1.0 : newDpi.DpiScaleY;
        Width = _monitor.Bounds.Width / scaleX;
        Height = _monitor.Bounds.Height / scaleY;
        NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The picker must receive Enter/Esc and clicks, so it is activatable (not
        // marked WS_EX_NOACTIVATE).
        NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);

        if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget is { } ct)
        {
            _fromDevice = ct.TransformFromDevice; // maps physical device px -> DIP
        }

        PositionHint();
        Focus();
        Keyboard.Focus(this);
        _pollTimer.Start();
        Track();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (NativeMethods.GetPhysicalWindowRect(Hwnd) != _monitor.Bounds)
            {
                NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);
            }
        });
    }

    /// <summary>The window's actual on-screen rect in physical pixels (diagnostics).</summary>
    public PixelRect PhysicalWindowRect => NativeMethods.GetPhysicalWindowRect(Hwnd);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Raise(new RegionSelectionResult(false, PixelRect.Empty, null));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _current is not null)
        {
            Commit(_current);
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Track();
        if (_current is not null)
        {
            Commit(_current);
        }

        e.Handled = true;
    }

    private void Track()
    {
        try
        {
            TrackCore();
            _trackFaulted = false;
        }
        catch (Exception ex)
        {
            if (!_trackFaulted)
            {
                _logger?.LogDebug(ex, "Failed to update the window picker highlight.");
                _trackFaulted = true;
            }

            ClearHighlight();
        }
    }

    private void TrackCore()
    {
        PixelPoint cursor = NativeMethods.GetCursorPixel();
        CandidateWindow? window = _picker.WindowAt(cursor);

        if (window is null)
        {
            _current = null;
            ClearHighlight();
            return;
        }

        _current = window;
        DrawHighlight(window, cursor);
    }

    private void ClearHighlight()
    {
        _current = null;
        Highlight.Visibility = Visibility.Collapsed;
        Label.Visibility = Visibility.Collapsed;
    }

    private void DrawHighlight(CandidateWindow window, PixelPoint cursor)
    {
        PixelRect visible = window.Bounds.Intersect(_monitor.Bounds);
        if (visible.IsEmpty)
        {
            ClearHighlight();
            return;
        }

        // Window bounds are physical pixels on the virtual desktop. Clip to this
        // monitor, translate to overlay-local pixels, then apply this HWND's
        // device->DIP transform. Per-monitor overlays avoid using one DPI transform
        // for a mixed-DPI virtual desktop.
        double localPxX = visible.X - _monitor.Bounds.X;
        double localPxY = visible.Y - _monitor.Bounds.Y;

        Point topLeft = _fromDevice.Transform(new Point(localPxX, localPxY));
        Point bottomRight = _fromDevice.Transform(new Point(localPxX + visible.Width, localPxY + visible.Height));

        double x = topLeft.X;
        double y = topLeft.Y;
        double w = Math.Max(0, bottomRight.X - topLeft.X);
        double h = Math.Max(0, bottomRight.Y - topLeft.Y);

        Canvas.SetLeft(Highlight, x);
        Canvas.SetTop(Highlight, y);
        Highlight.Width = w;
        Highlight.Height = h;
        Highlight.Visibility = Visibility.Visible;

        if (!_monitor.Bounds.Contains(cursor))
        {
            Label.Visibility = Visibility.Collapsed;
            return;
        }

        TitleText.Text = string.IsNullOrWhiteSpace(window.Title) ? "(untitled window)" : window.Title;
        ProcessText.Text = window.ProcessName;
        Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelY = y - Label.DesiredSize.Height - 6;
        if (labelY < 4)
        {
            labelY = y + 6;
        }

        Canvas.SetLeft(Label, Math.Max(4, x));
        Canvas.SetTop(Label, Math.Max(4, labelY));
        Label.Visibility = Visibility.Visible;
    }

    private void Commit(CandidateWindow window)
        => Raise(new RegionSelectionResult(true, window.Bounds, window.Handle.ToHex()));

    private void PositionHint()
    {
        Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(Hint, Math.Max(0, (ActualWidth - Hint.DesiredSize.Width) / 2));
        Canvas.SetTop(Hint, Math.Max(0, ActualHeight - Hint.DesiredSize.Height - 48));
    }

    private void Raise(RegionSelectionResult result)
    {
        if (_raised)
        {
            return;
        }

        _raised = true;
        _pollTimer.Stop();
        Completed?.Invoke(this, result);
    }
}

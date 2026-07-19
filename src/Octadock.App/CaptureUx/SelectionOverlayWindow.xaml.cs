using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Octadock.App.Windows;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// A borderless, capture-excluded overlay that covers exactly one monitor and lets
/// the user draw / move / resize a selection rectangle. It works internally in DIPs
/// (its own client space) and converts the committed rectangle to physical pixels on
/// the virtual desktop using the owning monitor's scale and origin. When freeze-screen
/// is enabled, a pre-captured fullscreen frame is painted as the background so moving
/// content is frozen during selection; that same frame powers the magnifier loupe.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SelectionOverlayWindow : ToolWindowBase
{
    private const double HandleSize = 10.0;
    private const double MinSelectionDip = 4.0;
    private const int LoupeSourcePixels = 28; // physical pixels sampled into the loupe

    private readonly DisplayInfo _monitor;
    private readonly BitmapSource? _frozenFrame; // this monitor's slice (physical px, origin = monitor top-left), or null
    private readonly Rectangle[] _handles = new Rectangle[8];

    // Selection state, in this overlay's DIP space.
    private bool _hasSelection;
    private bool _isDragging;   // initial rubber-band draw
    private bool _isMoving;     // moving an existing selection
    private int _activeHandle = -1; // resize handle index, or -1
    private Point _dragAnchor;  // fixed corner during draw/resize (DIP)
    private Point _moveGrabOffset; // cursor→rect offset while moving (DIP)
    private Rect _selection = Rect.Empty; // DIP

    /// <summary>Raised once, when the user confirms or cancels on this overlay.</summary>
    public event EventHandler<RegionSelectionResult>? Completed;

    /// <summary>The monitor this overlay covers.</summary>
    public DisplayInfo Monitor => _monitor;

    /// <summary>
    /// Creates an overlay for a single monitor, optionally over a frozen frame.
    /// <paramref name="primaryDpiScale"/> is the primary monitor's scale, used to
    /// aim the pre-show placement hint (WPF converts pre-show Left/Top with the
    /// primary DPI when it creates the HWND).
    /// </summary>
    public SelectionOverlayWindow(DisplayInfo monitor, BitmapSource? frozenFrame, double primaryDpiScale = 1.0)
    {
        _monitor = monitor;
        _frozenFrame = frozenFrame;
        ShowActivated = true;
        InitializeComponent();

        // Size the WPF logical surface to the monitor's DIP extent; the actual
        // physical placement is done in code once the HWND exists.
        Width = monitor.Bounds.Width / monitor.DpiScale;
        Height = monitor.Bounds.Height / monitor.DpiScale;

        // Mixed-DPI: aim the HWND at its target monitor BEFORE it exists, so
        // Windows assigns that monitor's DPI at creation. Without this the
        // window is born on the primary, sized under the primary's scale, and
        // moved afterwards — WPF's rescale-in-flight left the overlay covering
        // only part of 100%-scale monitors next to a 175% primary.
        double hintScale = primaryDpiScale <= 0 ? 1.0 : primaryDpiScale;
        Left = (monitor.Bounds.X + (monitor.Bounds.Width / 2.0)) / hintScale - (Width / 2.0);
        Top = (monitor.Bounds.Y + (monitor.Bounds.Height / 2.0)) / hintScale - (Height / 2.0);

        BuildHandles();

        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseDoubleClick += OnMouseDoubleClick;
        LostMouseCapture += OnLostMouseCapture;
        KeyDown += OnKeyDown;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        // If capture is stolen (Win key, a focus-stealing popup) the button-up is
        // never seen; reset drag state so the overlay does not keep rubber-banding
        // with the button already released.
        if (_isDragging || _isMoving || _activeHandle >= 0)
        {
            _isDragging = false;
            _isMoving = false;
            _activeHandle = -1;

            if (_hasSelection)
            {
                SetHandlesVisible(true);
                Toolbar.Visibility = Visibility.Visible;
                PositionToolbar();
            }

            UpdateDimMask();
        }
    }

    private double Scale => _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Pin the HWND to the monitor's exact physical rect while it is still
        // invisible, so any DPI transition happens before the first paint.
        NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Overlays are created at the primary monitor's DPI and then moved to
        // their target monitor; on mixed-DPI setups WPF rescales them mid-move
        // and the DIP size computed in the constructor no longer maps to the
        // monitor's physical bounds. Re-assert both from the ACTUAL new DPI so
        // every overlay converges to exact full-monitor coverage.
        double scaleX = newDpi.DpiScaleX <= 0 ? 1.0 : newDpi.DpiScaleX;
        double scaleY = newDpi.DpiScaleY <= 0 ? 1.0 : newDpi.DpiScaleY;
        Width = _monitor.Bounds.Width / scaleX;
        Height = _monitor.Bounds.Height / scaleY;
        NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Place the window at the monitor's exact physical bounds (mixed-DPI safe).
        // The overlay must be activatable to receive Enter/Esc/arrow keys, so it is
        // deliberately NOT marked WS_EX_NOACTIVATE.
        NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);

        if (_frozenFrame is not null)
        {
            // The service already hands us this monitor's slice (pixel 0,0 = monitor
            // top-left), so paint it directly, stretched to the DIP surface.
            FrozenImage.Source = _frozenFrame;
            FrozenImage.Visibility = Visibility.Visible;
        }

        UpdateDimMask();
        PositionHint();
        Focus();
        Keyboard.Focus(this);

        // Prime the crosshair/loupe at the current cursor location.
        UpdateCursorVisuals(GetCursorDip());

        // Verify coverage once layout settles; WPF and SetWindowPos can fight
        // over the size during a cross-DPI move, so the loser gets corrected.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (NativeMethods.GetPhysicalWindowRect(Hwnd) != _monitor.Bounds)
            {
                NativeMethods.PositionPhysical(Hwnd, _monitor.Bounds);
            }
        });
    }

    /// <summary>The window's actual on-screen rect in physical pixels (diagnostics).</summary>
    public PixelRect PhysicalWindowRect => NativeMethods.GetPhysicalWindowRect(Hwnd);

    // ---- Input --------------------------------------------------------------

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(Overlay);

        // If a selection exists, decide between resize (on a handle), move (inside)
        // or starting a fresh draw (outside).
        if (_hasSelection)
        {
            int handle = HitTestHandle(p);
            if (handle >= 0)
            {
                _activeHandle = handle;
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (_selection.Contains(p))
            {
                _isMoving = true;
                _moveGrabOffset = new Point(p.X - _selection.X, p.Y - _selection.Y);
                CaptureMouse();
                e.Handled = true;
                return;
            }
        }

        // Start a new rubber-band selection.
        _isDragging = true;
        _hasSelection = false;
        _dragAnchor = p;
        _selection = new Rect(p, new Size(0, 0));
        Hint.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Collapsed;
        SelectionRect.Visibility = Visibility.Visible;
        DimChip.Visibility = Visibility.Visible;
        SetHandlesVisible(false);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        Point p = e.GetPosition(Overlay);

        if (_isDragging)
        {
            // Clamp the rubber-band to the monitor surface as it is drawn, so a
            // drag past the edge cannot produce an over-size or off-monitor rect
            // (which would later make Math.Clamp throw when moving the selection).
            _selection = ClampToSurface(new Rect(_dragAnchor, p));
            ApplySelectionVisuals();
        }
        else if (_activeHandle >= 0)
        {
            _selection = ResizeFromHandle(_activeHandle, p);
            ApplySelectionVisuals();
        }
        else if (_isMoving)
        {
            // Guard the clamp bounds: if the selection somehow spans the whole
            // surface the max would fall below the min and Math.Clamp would throw.
            double maxX = Math.Max(0, ActualWidth - _selection.Width);
            double maxY = Math.Max(0, ActualHeight - _selection.Height);
            double x = Math.Clamp(p.X - _moveGrabOffset.X, 0, maxX);
            double y = Math.Clamp(p.Y - _moveGrabOffset.Y, 0, maxY);
            _selection = new Rect(x, y, _selection.Width, _selection.Height);
            ApplySelectionVisuals();
        }

        UpdateCursorVisuals(p);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging || _activeHandle >= 0)
        {
            bool wasDraw = _isDragging;
            _isDragging = false;
            _activeHandle = -1;
            ReleaseMouseCapture();

            if (_selection.Width >= MinSelectionDip && _selection.Height >= MinSelectionDip)
            {
                _hasSelection = true;
                SetHandlesVisible(true);
                Toolbar.Visibility = Visibility.Visible;
                PositionToolbar();
            }
            else if (wasDraw)
            {
                // A click without a meaningful drag: reset to the hint state.
                _hasSelection = false;
                _selection = Rect.Empty;
                SelectionRect.Visibility = Visibility.Collapsed;
                DimChip.Visibility = Visibility.Collapsed;
                Hint.Visibility = Visibility.Visible;
            }

            UpdateDimMask();
        }
        else if (_isMoving)
        {
            _isMoving = false;
            ReleaseMouseCapture();
            PositionToolbar();
        }
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_hasSelection && _selection.Contains(e.GetPosition(Overlay)))
        {
            Confirm();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;

            case Key.Enter:
                if (_hasSelection)
                {
                    Confirm();
                    e.Handled = true;
                }

                break;

            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                if (_hasSelection)
                {
                    NudgeSelection(e.Key, resize: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => Confirm();

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancel();

    // ---- Selection geometry -------------------------------------------------

    private void NudgeSelection(Key key, bool resize)
    {
        const double step = 1.0;
        Rect r = _selection;

        if (resize)
        {
            switch (key)
            {
                case Key.Left: r.Width = Math.Max(MinSelectionDip, r.Width - step); break;
                case Key.Right: r.Width += step; break;
                case Key.Up: r.Height = Math.Max(MinSelectionDip, r.Height - step); break;
                case Key.Down: r.Height += step; break;
            }
        }
        else
        {
            switch (key)
            {
                case Key.Left: r.X -= step; break;
                case Key.Right: r.X += step; break;
                case Key.Up: r.Y -= step; break;
                case Key.Down: r.Y += step; break;
            }
        }

        _selection = ClampToSurface(r);
        ApplySelectionVisuals();
        PositionToolbar();
        UpdateDimMask();
    }

    private Rect ClampToSurface(Rect r)
    {
        r = new Rect(
            Math.Max(0, r.X),
            Math.Max(0, r.Y),
            Math.Max(0, r.Width),
            Math.Max(0, r.Height));

        if (r.Right > ActualWidth)
        {
            r.Width = Math.Max(0, ActualWidth - r.X);
        }

        if (r.Bottom > ActualHeight)
        {
            r.Height = Math.Max(0, ActualHeight - r.Y);
        }

        return r;
    }

    private void ApplySelectionVisuals()
    {
        Rect r = _selection;
        Canvas.SetLeft(SelectionRect, r.X);
        Canvas.SetTop(SelectionRect, r.Y);
        SelectionRect.Width = r.Width;
        SelectionRect.Height = r.Height;
        SelectionRect.Visibility = Visibility.Visible;

        PositionHandles();
        UpdateDimensionChip();
        UpdateDimMask();
    }

    private void UpdateDimensionChip()
    {
        // Report the size in physical pixels — that is what the capture will be.
        int widthPx = (int)Math.Round(_selection.Width * Scale);
        int heightPx = (int)Math.Round(_selection.Height * Scale);
        DimText.Text = $"{widthPx} × {heightPx}";
        DimChip.Visibility = Visibility.Visible;

        double chipX = Math.Clamp(_selection.X, 0, Math.Max(0, ActualWidth - 90));
        double chipY = _selection.Y - 26;
        if (chipY < 4)
        {
            chipY = _selection.Bottom + 6;
        }

        Canvas.SetLeft(DimChip, chipX);
        Canvas.SetTop(DimChip, chipY);
    }

    private void UpdateDimMask()
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (_hasSelection || _isDragging || _activeHandle >= 0)
        {
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                var hole = new RectangleGeometry(_selection);
                DimPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
                return;
            }
        }

        DimPath.Data = full;
    }

    // ---- Handles ------------------------------------------------------------

    private void BuildHandles()
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = (Brush)FindResource("Octadock.Brush.CaptureHandle"),
                Stroke = (Brush)FindResource("Octadock.Brush.Accent"),
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed,
                RadiusX = 2,
                RadiusY = 2,
            };
            _handles[i] = handle;
            HandleLayer.Children.Add(handle);
        }
    }

    // Handle order: 0 TL, 1 TC, 2 TR, 3 MR, 4 BR, 5 BC, 6 BL, 7 ML.
    private void PositionHandles()
    {
        Rect r = _selection;
        double midX = r.X + (r.Width / 2);
        double midY = r.Y + (r.Height / 2);
        var points = new[]
        {
            new Point(r.X, r.Y),
            new Point(midX, r.Y),
            new Point(r.Right, r.Y),
            new Point(r.Right, midY),
            new Point(r.Right, r.Bottom),
            new Point(midX, r.Bottom),
            new Point(r.X, r.Bottom),
            new Point(r.X, midY),
        };

        for (int i = 0; i < _handles.Length; i++)
        {
            Canvas.SetLeft(_handles[i], points[i].X - (HandleSize / 2));
            Canvas.SetTop(_handles[i], points[i].Y - (HandleSize / 2));
        }
    }

    private void SetHandlesVisible(bool visible)
    {
        Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
        HandleLayer.IsHitTestVisible = visible;
        foreach (Rectangle handle in _handles)
        {
            handle.Visibility = v;
        }
    }

    private int HitTestHandle(Point p)
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            if (_handles[i].Visibility != Visibility.Visible)
            {
                continue;
            }

            double left = Canvas.GetLeft(_handles[i]);
            double top = Canvas.GetTop(_handles[i]);
            var box = new Rect(left - 2, top - 2, HandleSize + 4, HandleSize + 4);
            if (box.Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    // Resize by moving only the edges the dragged handle owns. Corner handles
    // (0 TL, 2 TR, 4 BR, 6 BL) move two edges; edge handles (1 TC, 3 MR, 5 BC,
    // 7 ML) move exactly one, so dragging the top-center handle changes only the
    // top edge instead of both axes.
    private Rect ResizeFromHandle(int handle, Point p)
    {
        double left = _selection.X;
        double top = _selection.Y;
        double right = _selection.Right;
        double bottom = _selection.Bottom;

        bool movesLeft = handle is 0 or 6 or 7;
        bool movesRight = handle is 2 or 3 or 4;
        bool movesTop = handle is 0 or 1 or 2;
        bool movesBottom = handle is 4 or 5 or 6;

        if (movesLeft) left = p.X;
        if (movesRight) right = p.X;
        if (movesTop) top = p.Y;
        if (movesBottom) bottom = p.Y;

        // Normalize so dragging a handle across the opposite edge still yields a
        // positive rectangle.
        var r = new Rect(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Abs(right - left),
            Math.Abs(bottom - top));
        return ClampToSurface(r);
    }

    // ---- Cursor visuals (crosshair + loupe) ---------------------------------

    private void UpdateCursorVisuals(Point p)
    {
        bool showCross = !_hasSelection && !_isMoving;
        CrossH.Visibility = showCross ? Visibility.Visible : Visibility.Collapsed;
        CrossV.Visibility = showCross ? Visibility.Visible : Visibility.Collapsed;
        if (showCross)
        {
            CrossH.X1 = 0;
            CrossH.X2 = ActualWidth;
            CrossH.Y1 = CrossH.Y2 = p.Y;
            CrossV.Y1 = 0;
            CrossV.Y2 = ActualHeight;
            CrossV.X1 = CrossV.X2 = p.X;
        }

        UpdateLoupe(p);
    }

    private void UpdateLoupe(Point p)
    {
        // Physical-pixel coordinates on the virtual desktop for readouts.
        int vpx = _monitor.Bounds.X + (int)Math.Round(p.X * Scale);
        int vpy = _monitor.Bounds.Y + (int)Math.Round(p.Y * Scale);
        LoupeCoords.Text = $"{vpx}, {vpy}";

        if (_frozenFrame is not null)
        {
            try
            {
                Color color = SampleFrozenPixel(vpx, vpy, out CroppedBitmap? zoomSource);
                LoupeHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                LoupeSwatch.Background = new SolidColorBrush(color);
                if (zoomSource is not null)
                {
                    // The 112×112 host Border upscales this small crop (Stretch=Fill,
                    // NearestNeighbor) into a pixelated loupe view.
                    LoupeImage.Source = zoomSource;
                }
            }
            catch
            {
                // Sampling near the frame edge can throw; keep the loupe usable.
            }

            Loupe.Visibility = Visibility.Visible;
        }
        else
        {
            // No frozen frame: still show a coordinate loupe (no pixel preview).
            LoupeHex.Text = "#------";
            Loupe.Visibility = Visibility.Visible;
        }

        PositionLoupe(p);
    }

    private void PositionLoupe(Point p)
    {
        double lx = p.X + 20;
        double ly = p.Y + 20;
        if (lx + Loupe.Width > ActualWidth)
        {
            lx = p.X - Loupe.Width - 20;
        }

        if (ly + Loupe.Height > ActualHeight)
        {
            ly = p.Y - Loupe.Height - 20;
        }

        Canvas.SetLeft(Loupe, Math.Max(0, lx));
        Canvas.SetTop(Loupe, Math.Max(0, ly));
    }

    /// <summary>
    /// Reads the color at a virtual-desktop physical pixel from the frozen frame and
    /// produces a zoomed crop centered on it for the loupe.
    /// </summary>
    private Color SampleFrozenPixel(int virtualX, int virtualY, out CroppedBitmap? zoom)
    {
        zoom = null;
        // The frozen frame is already this monitor's slice: pixel (0,0) maps to the
        // monitor's top-left virtual coordinate.
        int fx = virtualX - _monitor.Bounds.X;
        int fy = virtualY - _monitor.Bounds.Y;

        BitmapSource src = _frozenFrame!;
        fx = Math.Clamp(fx, 0, src.PixelWidth - 1);
        fy = Math.Clamp(fy, 0, src.PixelHeight - 1);

        var oneByOne = new CroppedBitmap(src, new Int32Rect(fx, fy, 1, 1));
        var pixel = new byte[4];
        oneByOne.CopyPixels(pixel, 4, 0);
        var color = Color.FromRgb(pixel[2], pixel[1], pixel[0]);

        int half = LoupeSourcePixels / 2;
        int zx = Math.Clamp(fx - half, 0, Math.Max(0, src.PixelWidth - LoupeSourcePixels));
        int zy = Math.Clamp(fy - half, 0, Math.Max(0, src.PixelHeight - LoupeSourcePixels));
        int zw = Math.Min(LoupeSourcePixels, src.PixelWidth - zx);
        int zh = Math.Min(LoupeSourcePixels, src.PixelHeight - zy);
        if (zw > 0 && zh > 0)
        {
            zoom = new CroppedBitmap(src, new Int32Rect(zx, zy, zw, zh));
        }

        return color;
    }

    // ---- Toolbar / hint placement ------------------------------------------

    private void PositionToolbar()
    {
        Toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = Toolbar.DesiredSize.Width;
        double h = Toolbar.DesiredSize.Height;

        double x = Math.Clamp(_selection.Right - w, 0, Math.Max(0, ActualWidth - w));
        double y = _selection.Bottom + 8;
        if (y + h > ActualHeight)
        {
            y = Math.Max(0, _selection.Y - h - 8);
        }

        Canvas.SetLeft(Toolbar, x);
        Canvas.SetTop(Toolbar, y);
    }

    private void PositionHint()
    {
        Hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(Hint, Math.Max(0, (ActualWidth - Hint.DesiredSize.Width) / 2));
        Canvas.SetTop(Hint, Math.Max(0, (ActualHeight - Hint.DesiredSize.Height) / 2));
    }

    // ---- Commit / cancel ----------------------------------------------------

    private void Confirm()
    {
        if (!_hasSelection || _selection.Width < MinSelectionDip || _selection.Height < MinSelectionDip)
        {
            return;
        }

        PixelRect pixels = ToVirtualPixels(_selection);
        Raise(new RegionSelectionResult(true, pixels, null));
    }

    private void Cancel() => Raise(new RegionSelectionResult(false, PixelRect.Empty, null));

    private bool _raised;

    private void Raise(RegionSelectionResult result)
    {
        if (_raised)
        {
            return;
        }

        _raised = true;
        Completed?.Invoke(this, result);
    }

    /// <summary>
    /// Converts a DIP rectangle in this overlay's client space to physical pixels on
    /// the virtual desktop: scale by the monitor DPI, then offset by the monitor origin.
    /// </summary>
    private PixelRect ToVirtualPixels(Rect dip)
    {
        var rect = new DipRect(dip.X, dip.Y, dip.Width, dip.Height);
        return _monitor.ToPixels(rect); // scales by DpiScale then offsets to Bounds origin
    }

    private Point GetCursorDip()
    {
        PixelPoint cursor = NativeMethods.GetCursorPixel();
        double localX = (cursor.X - _monitor.Bounds.X) / Scale;
        double localY = (cursor.Y - _monitor.Bounds.Y) / Scale;
        return new Point(
            Math.Clamp(localX, 0, ActualWidth),
            Math.Clamp(localY, 0, ActualHeight));
    }
}

/// <summary>Internal overlay result carried from a single monitor overlay to the service.</summary>
public readonly record struct RegionSelectionResult(bool Confirmed, PixelRect Region, string? WindowHandleHex);

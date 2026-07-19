using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Octadock.App.Theming;
using Octadock.Core.Annotations;
using Octadock.Core.Geometry;
using Octadock.Core.Primitives;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Octadock.App.Editing;

/// <summary>
/// The interactive editing surface: a <see cref="FrameworkElement"/> that draws
/// the base raster, the vector overlay (via <see cref="AnnotationRenderer"/>) and
/// the selection chrome, and translates mouse gestures into create / move / resize
/// operations reported to the <see cref="EditorViewModel"/>. It works entirely in
/// image-pixel coordinates internally, applying a uniform scale so the canvas
/// fills the available area while preserving aspect ratio.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class EditorCanvas : FrameworkElement
{
    private const double HandleSize = 9.0;

    private EditorViewModel? _viewModel;
    private BitmapSource? _baseImage;

    // Active gesture state (all in image-pixel space).
    private bool _dragging;
    private DragMode _mode;
    private WpfPoint _startImage;
    private WpfPoint _lastImage;
    private AnnotationObject? _gestureOriginal;
    private ResizeHandle _activeHandle;
    private readonly List<PointD> _freehandPoints = [];

    public EditorCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Arrow;
    }

    private enum DragMode
    {
        None,
        Create,
        Move,
        Resize,
        CropSelect,
    }

    /// <summary>The uniform image-to-device scale currently applied.</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>
    /// Raised when a text object needs the inline text editor: immediately after
    /// the Text tool places a new (empty) object, and when an existing text
    /// object is double-clicked with the Select tool. Without this the Text tool
    /// creates an invisible empty object the user can never type into.
    /// </summary>
    public event EventHandler<AnnotationObject>? TextEditRequested;

    /// <summary>Binds the canvas to its view model and repaints on model changes.</summary>
    public void Attach(EditorViewModel viewModel, BitmapSource baseImage)
    {
        _viewModel = viewModel;
        _baseImage = baseImage;
        _viewModel.VisualInvalidated += (_, _) => InvalidateVisual();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Swaps in a new base raster (e.g. after a crop) and repaints.</summary>
    public void UpdateBaseImage(BitmapSource baseImage)
    {
        _baseImage = baseImage;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        if (_viewModel is null)
        {
            return WpfSize.Empty;
        }

        PixelSize canvas = _viewModel.Document.CanvasSize;
        return new WpfSize(canvas.Width, canvas.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_viewModel is null || _baseImage is null)
        {
            return;
        }

        PixelSize canvas = _viewModel.Document.CanvasSize;
        if (canvas.IsEmpty)
        {
            return;
        }

        // Fit-to-bounds uniform scale.
        double sx = ActualWidth / canvas.Width;
        double sy = ActualHeight / canvas.Height;
        Scale = Math.Max(0.01, Math.Min(sx, sy));

        double drawnW = canvas.Width * Scale;
        double drawnH = canvas.Height * Scale;
        double offsetX = (ActualWidth - drawnW) / 2;
        double offsetY = (ActualHeight - drawnH) / 2;

        dc.PushTransform(new TranslateTransform(offsetX, offsetY));
        dc.PushTransform(new ScaleTransform(Scale, Scale));

        // Base raster (clamped to the canvas rect; crop shrinks the canvas).
        dc.DrawImage(_baseImage, new Rect(0, 0, canvas.Width, canvas.Height));

        // Vector overlay.
        AnnotationRenderer.Render(dc, _viewModel.Document, _baseImage);

        // In-progress freehand preview.
        if (_dragging && _mode == DragMode.Create && _viewModel.ActiveTool == EditorTool.Freehand && _freehandPoints.Count > 1)
        {
            AnnotationObject preview = BuildGestureObject(_lastImage);
            AnnotationRenderer.RenderObject(dc, preview, _baseImage);
        }

        // Crop dimming + selection chrome.
        if (_dragging && _mode == DragMode.CropSelect)
        {
            DrawCropPreview(dc, canvas);
        }

        dc.Pop(); // scale
        dc.Pop(); // translate

        // Selection handles are drawn in device space so they stay a constant size.
        DrawSelection(dc, offsetX, offsetY);
    }

    private void DrawCropPreview(DrawingContext dc, PixelSize canvas)
    {
        Rect crop = Normalize(_startImage, _lastImage);
        Brush dim = OctadockDesignTokens.Brushes.EditorCropDim;

        // Dim everything, then punch a clear hole over the crop region by drawing
        // the four surrounding bands.
        dc.DrawRectangle(dim, null, new Rect(0, 0, canvas.Width, crop.Top));
        dc.DrawRectangle(dim, null, new Rect(0, crop.Bottom, canvas.Width, canvas.Height - crop.Bottom));
        dc.DrawRectangle(dim, null, new Rect(0, crop.Top, crop.Left, crop.Height));
        dc.DrawRectangle(dim, null, new Rect(crop.Right, crop.Top, canvas.Width - crop.Right, crop.Height));

        var pen = new Pen(OctadockDesignTokens.Brushes.CaptureHandle, 1 / Math.Max(0.01, Scale));
        pen.Freeze();
        dc.DrawRectangle(null, pen, crop);
    }

    private void DrawSelection(DrawingContext dc, double offsetX, double offsetY)
    {
        if (_viewModel?.SelectedObject is not { } selected)
        {
            return;
        }

        Rect frame = selected.Frame.ToRect();
        Rect device = ImageToDevice(frame, offsetX, offsetY);

        Brush accent = OctadockDesignTokens.Brushes.Accent;
        var pen = new Pen(accent, 1.5) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        dc.DrawRectangle(null, pen, device);

        Brush handleFill = OctadockDesignTokens.Brushes.CaptureHandle;
        var handlePen = new Pen(accent, 1.5);
        handlePen.Freeze();

        foreach (WpfPoint corner in HandleCenters(device))
        {
            var handleRect = new Rect(
                corner.X - (HandleSize / 2),
                corner.Y - (HandleSize / 2),
                HandleSize,
                HandleSize);
            dc.DrawRectangle(handleFill, handlePen, handleRect);
        }
    }

    private static IEnumerable<WpfPoint> HandleCenters(Rect r)
    {
        yield return new WpfPoint(r.Left, r.Top);
        yield return new WpfPoint(r.Right, r.Top);
        yield return new WpfPoint(r.Left, r.Bottom);
        yield return new WpfPoint(r.Right, r.Bottom);
    }

    // ---- Input ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_viewModel is null)
        {
            return;
        }

        Focus();
        WpfPoint device = e.GetPosition(this);
        WpfPoint image = DeviceToImage(device);
        _startImage = image;
        _lastImage = image;

        EditorTool tool = _viewModel.ActiveTool;

        if (tool == EditorTool.Select)
        {
            // Double-click on a text object re-opens the inline editor.
            if (e.ClickCount == 2)
            {
                AnnotationObject? textHit = _viewModel.HitTest(image.ToPointD(), Scale);
                if (textHit is { Type: AnnotationObjectType.Text })
                {
                    _viewModel.Select(textHit.Id);
                    TextEditRequested?.Invoke(this, textHit);
                    e.Handled = true;
                    return;
                }
            }

            // Resize handle hit-test on the current selection first.
            if (_viewModel.SelectedObject is { } current)
            {
                ResizeHandle handle = HitTestHandle(current, device);
                if (handle != ResizeHandle.None)
                {
                    BeginResize(current, handle);
                    e.Handled = true;
                    CaptureMouse();
                    return;
                }
            }

            AnnotationObject? hit = _viewModel.HitTest(image.ToPointD(), Scale);
            _viewModel.Select(hit?.Id);
            if (hit is not null && !hit.Locked)
            {
                _mode = DragMode.Move;
                _dragging = true;
                _gestureOriginal = hit;
                CaptureMouse();
            }

            e.Handled = true;
            return;
        }

        if (tool == EditorTool.Crop)
        {
            _mode = DragMode.CropSelect;
            _dragging = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (tool.IsDrawing())
        {
            _mode = DragMode.Create;
            _dragging = true;
            _freehandPoints.Clear();
            _freehandPoints.Add(image.ToPointD());

            // Counter and text place immediately at a default size on click.
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_viewModel is null)
        {
            return;
        }

        // Right-click selects the object under the cursor (without starting a
        // drag) so the context menu that opens next acts on what was clicked.
        Focus();
        WpfPoint image = DeviceToImage(e.GetPosition(this));
        AnnotationObject? hit = _viewModel.HitTest(image.ToPointD(), Scale);
        _viewModel.Select(hit?.Id);
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || _viewModel is null)
        {
            return;
        }

        WpfPoint image = DeviceToImage(e.GetPosition(this));

        switch (_mode)
        {
            case DragMode.Move:
                MoveGesture(image);
                break;
            case DragMode.Resize:
                ResizeGesture(image);
                break;
            case DragMode.Create when _viewModel.ActiveTool == EditorTool.Freehand:
                _freehandPoints.Add(image.ToPointD());
                _lastImage = image;
                InvalidateVisual();
                break;
            case DragMode.Create:
            case DragMode.CropSelect:
                _lastImage = image;
                InvalidateVisual();
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging || _viewModel is null)
        {
            ReleaseMouseCapture();
            return;
        }

        WpfPoint image = DeviceToImage(e.GetPosition(this));
        _dragging = false;
        ReleaseMouseCapture();

        switch (_mode)
        {
            case DragMode.Move when _gestureOriginal is not null:
                _viewModel.CommitMove(_gestureOriginal, _viewModel.SelectedObject);
                break;
            case DragMode.Resize when _gestureOriginal is not null:
                _viewModel.CommitMove(_gestureOriginal, _viewModel.SelectedObject);
                break;
            case DragMode.Create:
                CommitCreate(image);
                break;
            case DragMode.CropSelect:
                CommitCrop(image);
                break;
        }

        _gestureOriginal = null;
        _mode = DragMode.None;
        InvalidateVisual();
    }

    private void MoveGesture(WpfPoint image)
    {
        if (_gestureOriginal is null || _viewModel is null)
        {
            return;
        }

        double dx = image.X - _startImage.X;
        double dy = image.Y - _startImage.Y;
        AnnotationFrame frame = _gestureOriginal.Frame.Offset(dx, dy);

        AnnotationObject moved = _gestureOriginal with
        {
            Frame = frame,
            Payload = OffsetPoints(_gestureOriginal.Payload, dx, dy),
        };
        _viewModel.PreviewUpdate(moved);
    }

    private void BeginResize(AnnotationObject target, ResizeHandle handle)
    {
        _mode = DragMode.Resize;
        _dragging = true;
        _gestureOriginal = target;
        _activeHandle = handle;
    }

    private void ResizeGesture(WpfPoint image)
    {
        if (_gestureOriginal is null || _viewModel is null)
        {
            return;
        }

        AnnotationFrame f = _gestureOriginal.Frame;
        double left = f.X;
        double top = f.Y;
        double right = f.Right;
        double bottom = f.Bottom;

        switch (_activeHandle)
        {
            case ResizeHandle.TopLeft:
                left = image.X;
                top = image.Y;
                break;
            case ResizeHandle.TopRight:
                right = image.X;
                top = image.Y;
                break;
            case ResizeHandle.BottomLeft:
                left = image.X;
                bottom = image.Y;
                break;
            case ResizeHandle.BottomRight:
                right = image.X;
                bottom = image.Y;
                break;
        }

        var frame = new AnnotationFrame(
            Math.Min(left, right),
            Math.Min(top, bottom),
            Math.Abs(right - left),
            Math.Abs(bottom - top));

        AnnotationObject resized = _gestureOriginal with { Frame = frame };

        // Line/arrow endpoints follow the frame corners so resize stays intuitive.
        if (_gestureOriginal.Type is AnnotationObjectType.Arrow or AnnotationObjectType.Line)
        {
            resized = resized with
            {
                Payload = _gestureOriginal.Payload with
                {
                    Points = [new PointD(frame.X, frame.Y), new PointD(frame.Right, frame.Bottom)],
                },
            };
        }

        _viewModel.PreviewUpdate(resized);
    }

    private void CommitCreate(WpfPoint image)
    {
        if (_viewModel is null)
        {
            return;
        }

        AnnotationObject obj = BuildGestureObject(image);

        // Reject accidental zero-size drags for shape tools (text/counter are ok).
        if (obj.Type is not (AnnotationObjectType.Text or AnnotationObjectType.Counter)
            && obj.Frame.Width < 3 && obj.Frame.Height < 3
            && _viewModel.ActiveTool != EditorTool.Freehand)
        {
            return;
        }

        _viewModel.AddObject(obj);

        // A new text object starts empty (invisible); open the inline editor
        // right away so the user can type. Committing empty text removes it.
        if (obj.Type == AnnotationObjectType.Text)
        {
            _viewModel.Select(obj.Id);
            TextEditRequested?.Invoke(this, obj);
        }
    }

    private void CommitCrop(WpfPoint image)
    {
        if (_viewModel is null)
        {
            return;
        }

        Rect crop = Normalize(_startImage, image);
        if (crop.Width < 4 || crop.Height < 4)
        {
            return;
        }

        _viewModel.ApplyCrop(new PixelRect(
            (int)Math.Round(crop.X),
            (int)Math.Round(crop.Y),
            (int)Math.Round(crop.Width),
            (int)Math.Round(crop.Height)));
    }

    /// <summary>Builds the object described by the current create gesture.</summary>
    private AnnotationObject BuildGestureObject(WpfPoint image)
    {
        EditorTool tool = _viewModel!.ActiveTool;
        AnnotationObjectType type = tool.ToObjectType() ?? AnnotationObjectType.Rectangle;

        Rect rect = Normalize(_startImage, image);
        AnnotationStyle style = _viewModel.BuildStyleForNewObject(type);
        int z = _viewModel.Document.NextZIndex();

        AnnotationPayload payload = AnnotationPayload.Empty;
        AnnotationFrame frame = new(rect.X, rect.Y, rect.Width, rect.Height);

        switch (type)
        {
            case AnnotationObjectType.Arrow:
            case AnnotationObjectType.Line:
                payload = new AnnotationPayload
                {
                    Points = [_startImage.ToPointD(), image.ToPointD()],
                };
                break;
            case AnnotationObjectType.Freehand:
                payload = new AnnotationPayload { Points = [.. _freehandPoints] };
                frame = FrameOfPoints(_freehandPoints);
                break;
            case AnnotationObjectType.Text:
                payload = new AnnotationPayload { Text = string.Empty };
                if (frame.Width < 10)
                {
                    frame = new AnnotationFrame(_startImage.X, _startImage.Y, 220, style.FontSize * 1.6);
                }

                break;
            case AnnotationObjectType.Counter:
                double d = Math.Max(28, style.FontSize * 1.6);
                frame = new AnnotationFrame(_startImage.X, _startImage.Y, d, d);
                payload = new AnnotationPayload { CounterValue = _viewModel.NextCounterValue() };
                break;
        }

        return new AnnotationObject
        {
            Id = Guid.NewGuid(),
            Type = type,
            Frame = frame,
            Style = style,
            Payload = payload,
            ZIndex = z,
        };
    }

    // ---- Selection handle hit testing ----

    private ResizeHandle HitTestHandle(AnnotationObject obj, WpfPoint device)
    {
        double offsetX = (ActualWidth - (_viewModel!.Document.CanvasSize.Width * Scale)) / 2;
        double offsetY = (ActualHeight - (_viewModel.Document.CanvasSize.Height * Scale)) / 2;
        Rect deviceRect = ImageToDevice(obj.Frame.ToRect(), offsetX, offsetY);

        if (Near(device, new WpfPoint(deviceRect.Left, deviceRect.Top)))
        {
            return ResizeHandle.TopLeft;
        }

        if (Near(device, new WpfPoint(deviceRect.Right, deviceRect.Top)))
        {
            return ResizeHandle.TopRight;
        }

        if (Near(device, new WpfPoint(deviceRect.Left, deviceRect.Bottom)))
        {
            return ResizeHandle.BottomLeft;
        }

        if (Near(device, new WpfPoint(deviceRect.Right, deviceRect.Bottom)))
        {
            return ResizeHandle.BottomRight;
        }

        return ResizeHandle.None;
    }

    private static bool Near(WpfPoint a, WpfPoint b)
        => Math.Abs(a.X - b.X) <= HandleSize && Math.Abs(a.Y - b.Y) <= HandleSize;

    // ---- Coordinate helpers ----

    private WpfPoint DeviceToImage(WpfPoint device)
    {
        PixelSize canvas = _viewModel!.Document.CanvasSize;
        double offsetX = (ActualWidth - (canvas.Width * Scale)) / 2;
        double offsetY = (ActualHeight - (canvas.Height * Scale)) / 2;
        double x = (device.X - offsetX) / Scale;
        double y = (device.Y - offsetY) / Scale;
        return new WpfPoint(
            Math.Clamp(x, 0, canvas.Width),
            Math.Clamp(y, 0, canvas.Height));
    }

    private Rect ImageToDevice(Rect imageRect, double offsetX, double offsetY)
        => new(
            offsetX + (imageRect.X * Scale),
            offsetY + (imageRect.Y * Scale),
            Math.Max(0, imageRect.Width) * Scale,
            Math.Max(0, imageRect.Height) * Scale);

    private static Rect Normalize(WpfPoint a, WpfPoint b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static AnnotationFrame FrameOfPoints(IReadOnlyList<PointD> points)
    {
        if (points.Count == 0)
        {
            return AnnotationFrame.Empty;
        }

        double minX = points[0].X, minY = points[0].Y, maxX = points[0].X, maxY = points[0].Y;
        foreach (PointD p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new AnnotationFrame(minX, minY, maxX - minX, maxY - minY);
    }

    private static AnnotationPayload OffsetPoints(AnnotationPayload payload, double dx, double dy)
    {
        if (payload.Points.Count == 0)
        {
            return payload;
        }

        var moved = new List<PointD>(payload.Points.Count);
        foreach (PointD p in payload.Points)
        {
            moved.Add(new PointD(p.X + dx, p.Y + dy));
        }

        return payload with { Points = moved };
    }

    private enum ResizeHandle
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }
}

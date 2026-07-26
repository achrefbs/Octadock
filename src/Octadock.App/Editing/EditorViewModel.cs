using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Annotations;
using Octadock.Core.Geometry;
using Octadock.Core.Primitives;

namespace Octadock.App.Editing;

/// <summary>
/// The annotation editor's view model. Owns the <see cref="AnnotationDocument"/>,
/// the undo/redo stack, the current tool and style, and the selection. The
/// <see cref="EditorCanvas"/> renders the document and reports gestures back
/// through the mutation methods here (every mutation goes through the history so
/// it is undoable). Save/export are wired by the owning window, which supplies
/// the raster and file operations via the <see cref="EditorHost"/> callbacks.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly EditorHistory _history;
    private readonly ILogger _logger;
    private int _counter;

    [ObservableProperty]
    private EditorTool _activeTool = EditorTool.Select;

    [ObservableProperty]
    private RgbaColor _strokeColor = RgbaColor.Accent;

    [ObservableProperty]
    private RgbaColor? _fillColor;

    [ObservableProperty]
    private double _lineWidth = 4.0;

    [ObservableProperty]
    private double _fontSize = 24.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private AnnotationObject? _selectedObject;

    [ObservableProperty]
    private string _title = "Annotation Editor";

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Creates the editor view model over the given document and base image.</summary>
    public EditorViewModel(AnnotationDocument document, BitmapSource baseImage, ILogger logger)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        BaseImage = baseImage ?? throw new ArgumentNullException(nameof(baseImage));
        _logger = logger;
        _history = new EditorHistory(document);
        _history.Changed += (_, _) =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };

        // Seed the next counter value from any existing counter objects.
        _counter = document.Objects
            .Where(o => o.Type == AnnotationObjectType.Counter)
            .Select(o => o.Payload.CounterValue ?? 0)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>The document being edited.</summary>
    public AnnotationDocument Document { get; }

    /// <summary>The current base raster (changes after a crop bakes a new image).</summary>
    public BitmapSource BaseImage { get; private set; }

    /// <summary>The available color swatches.</summary>
    public IReadOnlyList<ColorSwatch> Palette => ColorSwatch.DefaultPalette;

    /// <summary>
    /// The reduced swatch strip on the floating toolbar: accent plus the four
    /// callout colors that cover point-this-out work. The full palette stays
    /// available through <see cref="Palette"/> for any surface that needs it.
    /// </summary>
    public IReadOnlyList<ColorSwatch> CompactPalette { get; } =
    [
        ColorSwatch.DefaultPalette[0], // accent
        ColorSwatch.DefaultPalette[1], // red
        ColorSwatch.DefaultPalette[2], // amber
        ColorSwatch.DefaultPalette[4], // green
        ColorSwatch.DefaultPalette[9], // white
    ];

    /// <summary>
    /// The essential tools the floating toolbar exposes, in glance order:
    /// select/move, pen, circle an area, arrow, and a text label. The remaining
    /// <see cref="EditorTool"/> values keep working for existing documents and
    /// the canvas; they are simply not toolbar buttons anymore.
    /// </summary>
    public IReadOnlyList<EditorTool> Tools { get; } =
    [
        EditorTool.Select,
        EditorTool.Freehand,
        EditorTool.Ellipse,
        EditorTool.Arrow,
        EditorTool.Text,
    ];

    /// <summary>True when the document holds any annotation object (drives Clear).</summary>
    public bool HasAnnotations => Document.Objects.Count > 0;

    public bool HasSelection => SelectedObject is not null;

    /// <summary>Raised whenever the canvas should repaint.</summary>
    public event EventHandler? VisualInvalidated;

    /// <summary>Raised when the base image is swapped (after crop / crop-undo) so the canvas rebinds it.</summary>
    public event EventHandler<BitmapSource>? BaseImageChanged;

    /// <summary>Swaps the current base raster and notifies the canvas. Used by the crop command.</summary>
    private void SetBaseImage(BitmapSource image)
    {
        BaseImage = image;
        BaseImageChanged?.Invoke(this, image);
    }

    /// <summary>Host callbacks for the file/clipboard/window operations (set by the window).</summary>
    public EditorHost? Host { get; set; }

    // ---- Canvas-facing mutation API (all undoable) ----

    /// <summary>Adds a freshly-created object and selects it.</summary>
    public void AddObject(AnnotationObject obj)
    {
        _history.Do(new AddObjectCommand(obj));
        SelectedObject = obj;
        MarkDirty();
        Invalidate();

        // For text, the host opens an inline editor immediately.
        if (obj.Type == AnnotationObjectType.Text)
        {
            Host?.BeginTextEditing(obj);
        }
    }

    /// <summary>Applies an in-flight (non-committed) preview of a moved/resized object.</summary>
    public void PreviewUpdate(AnnotationObject preview)
    {
        Document.Replace(preview);
        if (SelectedObject?.Id == preview.Id)
        {
            SelectedObject = preview;
        }

        Invalidate();
    }

    /// <summary>Records a completed move/resize as a single undoable command.</summary>
    public void CommitMove(AnnotationObject before, AnnotationObject? after)
    {
        if (after is null || before.Id != after.Id)
        {
            return;
        }

        if (FramesEqual(before, after))
        {
            return; // no-op drag
        }

        // The document already holds `after` from the preview; record it so undo
        // restores `before`.
        _history.Do(new UpdateObjectCommand(before, after));
        SelectedObject = after;
        MarkDirty();
        Invalidate();
    }

    /// <summary>Replaces the given object's text and style (from the inline text editor).</summary>
    public void UpdateText(AnnotationObject original, string text)
    {
        AnnotationObject updated = original with
        {
            Payload = original.Payload with { Text = text },
        };

        if (string.IsNullOrEmpty(text))
        {
            // An empty text box is discarded.
            _history.Do(new RemoveObjectCommand(original));
            SelectedObject = null;
        }
        else
        {
            _history.Do(new UpdateObjectCommand(original, updated));
            SelectedObject = updated;
        }

        MarkDirty();
        Invalidate();
    }

    /// <summary>Crops the canvas to the given image-space rectangle.</summary>
    public void ApplyCrop(PixelRect region)
    {
        PixelRect clamped = region.Intersect(new PixelRect(0, 0, Document.CanvasSize.Width, Document.CanvasSize.Height));
        if (clamped.IsEmpty)
        {
            return;
        }

        PixelSize before = Document.CanvasSize;
        IReadOnlyList<AnnotationObject> objectsBefore = [.. Document.Objects];

        // Offset every object so it stays put relative to the new origin.
        var shifted = new List<AnnotationObject>(Document.Objects.Count);
        foreach (AnnotationObject o in Document.Objects)
        {
            AnnotationFrame f = o.Frame.Offset(-clamped.X, -clamped.Y);
            AnnotationPayload payload = o.Payload;
            if (payload.Points.Count > 0)
            {
                var pts = new List<PointD>(payload.Points.Count);
                foreach (PointD p in payload.Points)
                {
                    pts.Add(new PointD(p.X - clamped.X, p.Y - clamped.Y));
                }

                payload = payload with { Points = pts };
            }

            shifted.Add(o with { Frame = f, Payload = payload });
        }

        // Bake a cropped base raster so blur/pixelate keep sampling correct pixels.
        var cropped = new CroppedBitmap(
            BaseImage,
            new System.Windows.Int32Rect(clamped.X, clamped.Y, clamped.Width, clamped.Height));
        cropped.Freeze();

        BitmapSource originalImage = BaseImage;
        var after = new PixelSize(clamped.Width, clamped.Height);

        // The crop is a single undoable step that also swaps the base raster; undo
        // restores the size, the objects and the original image.
        _history.Do(new CropCommand(before, after, objectsBefore, shifted, originalImage, cropped, SetBaseImage));

        SelectedObject = null;
        MarkDirty();
        Invalidate();

        // Return to the select tool after a crop.
        ActiveTool = EditorTool.Select;
    }

    /// <summary>Deletes the currently selected object.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        if (SelectedObject is not { } selected)
        {
            return;
        }

        _history.Do(new RemoveObjectCommand(selected));
        SelectedObject = null;
        MarkDirty();
        Invalidate();
    }

    /// <summary>
    /// Removes every annotation in one undoable step. The base image stays; this
    /// is the quick "start the callouts over" action, not a document reset.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasAnnotations))]
    private void ClearAnnotations()
    {
        _history.Do(new ClearObjectsCommand([.. Document.Objects]));
        SelectedObject = null;
        MarkDirty();
        Invalidate();
    }

    /// <summary>Hit-tests the document top-down for an object under the point.</summary>
    public AnnotationObject? HitTest(PointD point, double viewScale = 1.0)
    {
        double hitPadding = 4 / Math.Max(0.01, viewScale);

        // Topmost first (objects are sorted ascending by z-index).
        for (int i = Document.Objects.Count - 1; i >= 0; i--)
        {
            AnnotationObject o = Document.Objects[i];
            if (o.Locked)
            {
                continue;
            }

            System.Windows.Rect r = o.Frame.ToRect();
            r.Inflate(hitPadding, hitPadding); // constant-size padding in view space
            if (r.Contains(new System.Windows.Point(point.X, point.Y)))
            {
                return o;
            }
        }

        return null;
    }

    /// <summary>Selects the object with the given id (or clears selection).</summary>
    public void Select(Guid? id)
    {
        SelectedObject = id is { } value
            ? Document.Objects.FirstOrDefault(o => o.Id == value)
            : null;
        Invalidate();
    }

    /// <summary>Builds the style a newly-created object of the given type should use.</summary>
    public AnnotationStyle BuildStyleForNewObject(AnnotationObjectType type)
    {
        var style = new AnnotationStyle
        {
            Stroke = StrokeColor,
            Fill = FillColor,
            LineWidth = LineWidth,
            FontSize = FontSize,
        };

        return type switch
        {
            // Highlighter uses the stroke color as a translucent wash.
            AnnotationObjectType.Highlight => style with { Fill = StrokeColor.WithOpacity(0.35), Stroke = null },
            // Counters fill with the stroke color and use white numerals.
            AnnotationObjectType.Counter => style with { Fill = StrokeColor, Stroke = RgbaColor.White },
            // Text draws in the stroke color; no shape stroke.
            AnnotationObjectType.Text => style with { Stroke = StrokeColor, Fill = null },
            _ => style,
        };
    }

    /// <summary>Advances and returns the next step-marker number.</summary>
    public int NextCounterValue() => ++_counter;

    // ---- Toolbar commands ----

    [RelayCommand]
    private void SelectTool(EditorTool tool)
    {
        ActiveTool = tool;
        if (tool != EditorTool.Select)
        {
            SelectedObject = null;
            Invalidate();
        }
    }

    [RelayCommand]
    private void PickSwatch(ColorSwatch swatch)
    {
        StrokeColor = swatch.Color;
        ApplyStyleToSelection();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _history.Undo();
        SelectedObject = null;
        MarkDirty();
        Invalidate();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _history.Redo();
        SelectedObject = null;
        MarkDirty();
        Invalidate();
    }

    private bool CanUndo() => _history.CanUndo;

    private bool CanRedo() => _history.CanRedo;

    [RelayCommand]
    private async Task CopyAsync() => await (Host?.CopyAsync() ?? Task.CompletedTask).ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Host is null)
        {
            return;
        }

        // Only clear the dirty flag when the host confirms an actual write; a
        // cancelled dialog or a failed save must leave the work marked unsaved.
        if (await Host.SaveAsync().ConfigureAwait(true))
        {
            IsDirty = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (Host is null)
        {
            return;
        }

        if (await Host.SaveAsAsync().ConfigureAwait(true))
        {
            IsDirty = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync() => await (Host?.ExportAsync() ?? Task.CompletedTask).ConfigureAwait(true);

    /// <summary>Applies the current stroke/fill/width/font to the selected object, undoably.</summary>
    public void ApplyStyleToSelection()
    {
        if (SelectedObject is not { } selected)
        {
            return;
        }

        AnnotationStyle style = selected.Style with
        {
            LineWidth = LineWidth,
            FontSize = FontSize,
        };

        // Map the pickers onto the fields relevant to the object type.
        style = selected.Type switch
        {
            AnnotationObjectType.Highlight => style with { Fill = StrokeColor.WithOpacity(0.35) },
            AnnotationObjectType.Text => style with { Stroke = StrokeColor, Fill = FillColor },
            AnnotationObjectType.Counter => style with { Fill = StrokeColor },
            _ => style with { Stroke = StrokeColor, Fill = FillColor },
        };

        AnnotationObject updated = selected with { Style = style };
        _history.Do(new UpdateObjectCommand(selected, updated));
        SelectedObject = updated;
        MarkDirty();
        Invalidate();
    }

    partial void OnLineWidthChanged(double value) => ApplyStyleToSelection();

    partial void OnFontSizeChanged(double value) => ApplyStyleToSelection();

    partial void OnFillColorChanged(RgbaColor? value) => ApplyStyleToSelection();

    private void MarkDirty()
    {
        IsDirty = true;
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        ClearAnnotationsCommand.NotifyCanExecuteChanged();
    }

    private void Invalidate() => VisualInvalidated?.Invoke(this, EventArgs.Empty);

    private static bool FramesEqual(AnnotationObject a, AnnotationObject b)
        => a.Frame == b.Frame
        && a.Payload.Points.Count == b.Payload.Points.Count
        && a.Payload.Points.SequenceEqual(b.Payload.Points);
}

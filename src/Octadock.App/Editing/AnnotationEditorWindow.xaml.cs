using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Annotations;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using WpfPoint = System.Windows.Point;

namespace Octadock.App.Editing;

/// <summary>
/// The annotation editor window: a normal resizable window hosting the toolbar and
/// the <see cref="EditorCanvas"/>. It wires the view model's document to the canvas,
/// implements the file/clipboard/window operations via <see cref="EditorHost"/>,
/// handles keyboard shortcuts, and persists the editable project (and links it back
/// to the source <see cref="CaptureRecord"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AnnotationEditorWindow : Window
{
    private readonly IImageLoadService _images;
    private readonly IImageEncoder _encoder;
    private readonly IClipboardService _clipboard;
    private readonly IProjectSerializer _projects;
    private readonly IStoragePaths _paths;
    private readonly ICaptureRepository _captures;
    private readonly IActionRepository _actions;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly ILogger<AnnotationEditorWindow> _logger;

    private readonly EditorCanvas _canvas = new();
    private EditorViewModel _viewModel = null!;
    private Host _host = null!;

    private string? _projectPath;
    private Guid? _sourceCaptureId;
    private TextBox? _inlineEditor;
    private AnnotationObject? _inlineTarget;
    private bool _closeConfirmed;

    /// <summary>Creates the editor window with its Core services (resolved via DI).</summary>
    public AnnotationEditorWindow(
        IImageLoadService images,
        IImageEncoder encoder,
        IClipboardService clipboard,
        IProjectSerializer projects,
        IStoragePaths paths,
        ICaptureRepository captures,
        IActionRepository actions,
        INotificationService notifications,
        ISettingsService settings,
        ILogger<AnnotationEditorWindow> logger)
    {
        _images = images;
        _encoder = encoder;
        _clipboard = clipboard;
        _projects = projects;
        _paths = paths;
        _captures = captures;
        _actions = actions;
        _notifications = notifications;
        _settings = settings;
        _logger = logger;

        InitializeComponent();
        CanvasHost.Children.Add(_canvas);

        // The Text tool and text double-clicks request the inline editor here.
        _canvas.TextEditRequested += (_, textObject) => BeginInlineText(textObject);
        _canvas.ContextMenu = BuildCanvasContextMenu();
    }

    /// <summary>
    /// The canvas right-click menu. Built in code (the canvas is created in
    /// code) with click handlers that read the view model at invocation time.
    /// </summary>
    private ContextMenu BuildCanvasContextMenu()
    {
        var editText = new MenuItem { Header = "Edit text" };
        editText.Click += (_, _) =>
        {
            if (_viewModel?.SelectedObject is { Type: AnnotationObjectType.Text } text)
            {
                BeginInlineText(text);
            }
        };

        var delete = new MenuItem { Header = "Delete", InputGestureText = "Del" };
        delete.Click += (_, _) => _viewModel?.DeleteSelectedCommand.Execute(null);

        var undo = new MenuItem { Header = "Undo", InputGestureText = "Ctrl+Z" };
        undo.Click += (_, _) => _viewModel?.UndoCommand.Execute(null);

        var redo = new MenuItem { Header = "Redo", InputGestureText = "Ctrl+Y" };
        redo.Click += (_, _) => _viewModel?.RedoCommand.Execute(null);

        var copy = new MenuItem { Header = "Copy image", InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => _viewModel?.CopyCommand.Execute(null);

        var export = new MenuItem { Header = "Export image…" };
        export.Click += (_, _) => _viewModel?.ExportCommand.Execute(null);

        var menu = new ContextMenu();
        menu.Items.Add(editText);
        menu.Items.Add(delete);
        menu.Items.Add(new Separator());
        menu.Items.Add(undo);
        menu.Items.Add(redo);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(export);

        menu.Opened += (_, _) =>
        {
            bool hasSelection = _viewModel?.SelectedObject is not null;
            editText.IsEnabled = _viewModel?.SelectedObject?.Type == AnnotationObjectType.Text;
            delete.IsEnabled = hasSelection;
        };

        return menu;
    }

    /// <summary>Initializes the editor with a document + base image and optional project/source links.</summary>
    public void LoadDocument(
        AnnotationDocument document,
        BitmapSource baseImage,
        string? projectPath,
        Guid? sourceCaptureId,
        string? title)
    {
        _projectPath = projectPath;
        _sourceCaptureId = sourceCaptureId ?? document.SourceCaptureId;

        _viewModel = new EditorViewModel(document, baseImage, _logger);
        _host = new Host(this);
        _viewModel.Host = _host;
        _viewModel.Title = title ?? "Annotation Editor";
        DataContext = _viewModel;

        _viewModel.BaseImageChanged += (_, image) => _canvas.UpdateBaseImage(image);
        _canvas.Attach(_viewModel, baseImage);
    }

    // ---- Keyboard shortcuts ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is null)
        {
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        // Do not intercept while typing in the inline text editor.
        if (_inlineEditor is not null && _inlineEditor.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Escape || (e.Key == Key.Enter && ctrl))
            {
                CommitInlineText();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Z when ctrl && !shift:
                _viewModel.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Y when ctrl:
            case Key.Z when ctrl && shift:
                _viewModel.RedoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S when ctrl:
                _viewModel.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when ctrl:
                _viewModel.CopyCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete:
            case Key.Back:
                _viewModel.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---- Close confirmation ----

    /// <summary>
    /// Guards against silently discarding unsaved annotations. <see cref="Window.Closing"/>
    /// is synchronous, so when the document is dirty we cancel this close, prompt (and
    /// optionally save) on an async continuation, and only re-invoke <see cref="Window.Close"/>
    /// once the user's choice has been honoured — gated by <see cref="_closeConfirmed"/> so
    /// the second pass falls straight through.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeConfirmed || _viewModel is null || !_viewModel.IsDirty)
        {
            return;
        }

        // Defer the real close until the prompt (and any save) has resolved.
        e.Cancel = true;
        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        MessageBoxResult choice = MessageBox.Show(
            this,
            "Save your annotations before closing?",
            "Unsaved annotations",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        switch (choice)
        {
            case MessageBoxResult.Yes:
                await _viewModel.SaveCommand.ExecuteAsync(null).ConfigureAwait(true);

                // If the save was cancelled or failed the document is still dirty;
                // abort the close so the work is not lost.
                if (_viewModel.IsDirty)
                {
                    return;
                }

                break;

            case MessageBoxResult.No:
                break;

            default:
                // Cancel: keep the editor open.
                return;
        }

        _closeConfirmed = true;
        Close();
    }

    // ---- Custom color pickers ----

    private void OnPickStrokeColor(object sender, RoutedEventArgs e)
    {
        if (TryPickColor(_viewModel.StrokeColor, out Core.Primitives.RgbaColor picked))
        {
            _viewModel.StrokeColor = picked;
            _viewModel.ApplyStyleToSelection();
        }
    }

    private void OnPickFillColor(object sender, RoutedEventArgs e)
    {
        Core.Primitives.RgbaColor seed = _viewModel.FillColor ?? _viewModel.StrokeColor;
        if (TryPickColor(seed, out Core.Primitives.RgbaColor picked))
        {
            _viewModel.FillColor = picked;
        }
    }

    private static bool TryPickColor(Core.Primitives.RgbaColor seed, out Core.Primitives.RgbaColor result)
    {
        // WinForms color dialog (the app already references WindowsForms).
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(seed.A, seed.R, seed.G, seed.B),
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            System.Drawing.Color c = dialog.Color;
            result = new Core.Primitives.RgbaColor(c.R, c.G, c.B, c.A);
            return true;
        }

        result = seed;
        return false;
    }

    // ---- Drag-out ----

    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            // Flatten (with annotations) and drop a temp PNG file so any app that
            // accepts file drops works; also place the bitmap for image-aware targets.
            System.Windows.Media.Imaging.BitmapSource flattened =
                EditorExporter.Flatten(_viewModel.Document, _viewModel.BaseImage);
            byte[] png = _images.EncodePng(flattened);
            Directory.CreateDirectory(_paths.TempExportsDirectory);
            string temp = Path.Combine(_paths.TempExportsDirectory, $"octadock-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(temp, png);

            var files = new System.Collections.Specialized.StringCollection { temp };
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetImage(flattened);

            RecordAction(ActionType.DraggedOut);
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Editor drag-out failed.");
        }
    }

    // ---- Inline text editing ----

    private void BeginInlineText(AnnotationObject textObject)
    {
        CommitInlineText(); // commit any in-flight editor first
        _inlineTarget = textObject;

        System.Windows.Rect frame = textObject.Frame.ToRect();
        double scale = _canvas.Scale;

        _inlineEditor = new TextBox
        {
            Text = textObject.Payload.Text ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(textObject.Style.FontFamily),
            FontSize = Math.Max(8, textObject.Style.FontSize * scale),
            MinWidth = Math.Max(60, frame.Width * scale),
            Foreground = new SolidColorBrush((textObject.Style.Stroke ?? Core.Primitives.RgbaColor.Black).ToWpf()),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // Position over the object's top-left in the CanvasHost's coordinate space.
        var host = CanvasHost;
        GeneralTransform transform = _canvas.TransformToAncestor(host);
        WpfPoint canvasOrigin = transform.Transform(new WpfPoint(0, 0));
        double offsetX = (_canvas.ActualWidth - (_viewModel.Document.CanvasSize.Width * scale)) / 2;
        double offsetY = (_canvas.ActualHeight - (_viewModel.Document.CanvasSize.Height * scale)) / 2;

        _inlineEditor.Margin = new Thickness(
            canvasOrigin.X + offsetX + (frame.X * scale),
            canvasOrigin.Y + offsetY + (frame.Y * scale),
            0,
            0);

        host.Children.Add(_inlineEditor);
        _inlineEditor.LostKeyboardFocus += (_, _) => CommitInlineText();
        _inlineEditor.Focus();
        _inlineEditor.SelectAll();
    }

    private void CommitInlineText()
    {
        if (_inlineEditor is null || _inlineTarget is null)
        {
            return;
        }

        string text = _inlineEditor.Text;
        AnnotationObject target = _inlineTarget;

        CanvasHost.Children.Remove(_inlineEditor);
        _inlineEditor = null;
        _inlineTarget = null;

        _viewModel.UpdateText(target, text);
    }

    // ---- Save / export / copy ----

    /// <summary>
    /// Saves the editable project. Returns <see langword="true"/> only when the file was
    /// actually written, so the view model clears its dirty flag exclusively on success:
    /// a cancelled <see cref="SaveFileDialog"/> or a caught exception both report
    /// <see langword="false"/> and leave the work marked unsaved.
    /// </summary>
    private async Task<bool> SaveProjectAsync(bool forcePrompt)
    {
        string? path = _projectPath;
        if (forcePrompt || string.IsNullOrEmpty(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save annotation project",
                Filter = "Octadock project (*.octadock)|*.octadock",
                DefaultExt = ".octadock",
                InitialDirectory = SafeDir(_paths.ProjectsDirectory),
                FileName = $"annotation-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.octadock",
            };

            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            // Encode/flatten on the UI thread — RenderTargetBitmap (used by the
            // flatten) requires the dispatcher thread. Only the serializer's file
            // I/O (over plain byte arrays) is offloaded.
            byte[] basePng = _images.EncodePng(_viewModel.BaseImage);
            byte[] previewPng = EditorExporter.FlattenToPng(_viewModel.Document, _viewModel.BaseImage);

            await Task.Run(() => _projects.SaveAsync(
                path!,
                _viewModel.Document,
                basePng,
                previewPng)).ConfigureAwait(true);

            _projectPath = path;

            // Link the project back to the source capture so History shows it as annotated.
            await LinkProjectToCaptureAsync(path!).ConfigureAwait(true);
            RecordAction(ActionType.Annotated, path);
            _notifications.Notify("Project saved", Path.GetFileName(path), NotificationKind.Success);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save annotation project to {Path}.", path);
            _notifications.Notify("Save failed", "Could not save the annotation project.", NotificationKind.Error);
            return false;
        }
    }

    private async Task LinkProjectToCaptureAsync(string projectPath)
    {
        if (_sourceCaptureId is not { } id)
        {
            return;
        }

        try
        {
            CaptureRecord? record = await _captures.GetAsync(id).ConfigureAwait(true);
            if (record is null)
            {
                return;
            }

            string managedProjectPath = EnsureManagedProjectPath(projectPath, id);
            string relative = _paths.ToRelative(managedProjectPath);
            if (!string.Equals(record.ProjectPath, relative, StringComparison.OrdinalIgnoreCase))
            {
                await _captures.UpdateAsync(record with { ProjectPath = relative }).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to link project to capture {CaptureId}.", id);
        }
    }

    private string EnsureManagedProjectPath(string projectPath, Guid captureId)
    {
        string fullPath = Path.GetFullPath(projectPath);
        if (IsUnderStorageRoot(fullPath))
        {
            return fullPath;
        }

        Directory.CreateDirectory(_paths.ProjectsDirectory);
        string managedPath = Path.Combine(_paths.ProjectsDirectory, $"{captureId:D}.octadock");
        if (!string.Equals(fullPath, Path.GetFullPath(managedPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(fullPath, managedPath, overwrite: true);
        }

        return managedPath;
    }

    private bool IsUnderStorageRoot(string path)
    {
        string root = EnsureTrailingSeparator(Path.GetFullPath(_paths.RootDirectory));
        string full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
        => Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private async Task ExportRasterAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export image",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg",
            DefaultExt = ".png",
            InitialDirectory = SafeDir(DefaultExportDirectory()),
            FileName = $"octadock-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string path = dialog.FileName;
        bool jpeg = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                 || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

        try
        {
            BitmapSource flattened = EditorExporter.Flatten(_viewModel.Document, _viewModel.BaseImage);
            byte[] bytes = jpeg
                ? EncodeFlattened(flattened, ExportImageFormat.Jpeg)
                : _images.EncodePng(flattened);

            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
            RecordAction(ActionType.Exported, path);
            _notifications.Notify("Exported", Path.GetFileName(path), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export flattened image to {Path}.", path);
            _notifications.Notify("Export failed", "Could not export the image.", NotificationKind.Error);
        }
    }

    private byte[] EncodeFlattened(BitmapSource flattened, ExportImageFormat format)
    {
        // Reuse the app encoder when it can encode a BitmapSource; fall back to PNG.
        if (_encoder is Imaging.WpfImageEncoder wpf)
        {
            EncodedImage encoded = wpf.Encode(flattened, new EncodeOptions { Format = format, Quality = 92 });
            return encoded.Bytes.ToArray();
        }

        return _images.EncodePng(flattened);
    }

    private Task CopyFlattenedAsync()
    {
        try
        {
            BitmapSource flattened = EditorExporter.Flatten(_viewModel.Document, _viewModel.BaseImage);
            byte[] png = _images.EncodePng(flattened);
            _clipboard.SetImage(new EncodedImage(png, ExportImageFormat.Png));
            RecordAction(ActionType.Copied, "clipboard");
            _notifications.Notify("Copied", "The annotated image is on the clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy the flattened image.");
        }

        return Task.CompletedTask;
    }

    private void RecordAction(ActionType type, string? destination = null)
    {
        if (_sourceCaptureId is not { } id)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _actions.AddAsync(new ActionRecord
                {
                    Id = Guid.NewGuid(),
                    CaptureId = id,
                    ActionType = type,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Destination = destination,
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to record editor action {Action}.", type);
            }
        });
    }

    /// <summary>
    /// Where "Export image" should land by default. B-6: never TempExports —
    /// the retention pass purges that folder, which would silently delete
    /// exports the user explicitly chose to keep.
    /// </summary>
    private string DefaultExportDirectory()
    {
        string configured = _settings.Current.Capture.SaveDirectory;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Octadock");
    }

    private static string SafeDir(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            // best-effort
        }

        return directory;
    }

    /// <summary>Adapts the window's file/clipboard operations to the view model's <see cref="EditorHost"/>.</summary>
    private sealed class Host(AnnotationEditorWindow owner) : EditorHost
    {
        public override Task CopyAsync() => owner.CopyFlattenedAsync();

        public override Task<bool> SaveAsync() => owner.SaveProjectAsync(forcePrompt: false);

        public override Task<bool> SaveAsAsync() => owner.SaveProjectAsync(forcePrompt: true);

        public override Task ExportAsync() => owner.ExportRasterAsync();

        public override void BeginTextEditing(AnnotationObject textObject) => owner.BeginInlineText(textObject);
    }
}

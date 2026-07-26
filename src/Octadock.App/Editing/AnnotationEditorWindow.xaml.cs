using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.App.Ai;
using Octadock.App.Services;
using Octadock.App.Theming;
using Octadock.Core.Abstractions;
using Octadock.Core.Annotations;
using Octadock.Core.Io;
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
    private readonly IImageMockupService _mockups;
    private readonly IImageEditProvider _imageEditProvider;
    private readonly ISafeFileWriter _safeWriter;
    private readonly IShelfService _shelf;
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
        IImageMockupService mockups,
        IImageEditProvider imageEditProvider,
        ISafeFileWriter safeWriter,
        IShelfService shelf,
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
        _mockups = mockups;
        _imageEditProvider = imageEditProvider;
        _safeWriter = safeWriter;
        _shelf = shelf;
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

        var mockup = new MenuItem { Header = "Create AI mockup from selected area…" };
        mockup.Click += async (_, _) => await CreateMockupAsync().ConfigureAwait(true);

        var menu = new ContextMenu();
        menu.Items.Add(editText);
        menu.Items.Add(delete);
        menu.Items.Add(new Separator());
        menu.Items.Add(undo);
        menu.Items.Add(redo);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(export);
        menu.Items.Add(mockup);

        menu.Opened += (_, _) =>
        {
            bool hasSelection = _viewModel?.SelectedObject is not null;
            editText.IsEnabled = _viewModel?.SelectedObject?.Type == AnnotationObjectType.Text;
            delete.IsEnabled = hasSelection;
            mockup.IsEnabled = _viewModel?.SelectedObject?.Type is AnnotationObjectType.Rectangle or AnnotationObjectType.Ellipse;
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

        // The share path is capture-bound; an ad-hoc image without a source
        // capture has nothing to hand off, so the button leaves the island.
        ShareButton.Visibility = _sourceCaptureId is null ? Visibility.Collapsed : Visibility.Visible;

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

    // ---- Share (reviewed handoff) ----

    private async void OnShare(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShareHandoffAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Editor share handoff failed.");
            _notifications.Notify("Share failed", "Could not prepare the handoff.", NotificationKind.Error);
        }
    }

    /// <summary>
    /// Shares the annotated capture through the same reviewed-handoff flow the
    /// Shelf offers, so the editor never invents a second, divergent share path.
    /// </summary>
    private async Task ShareHandoffAsync()
    {
        if (_sourceCaptureId is not { } id)
        {
            return;
        }

        CaptureRecord? record = await _captures.GetAsync(id).ConfigureAwait(true);
        if (record is null)
        {
            _notifications.Notify("Share unavailable", "The source capture no longer exists.", NotificationKind.Warning);
            return;
        }

        string fileName = Path.GetFileName(record.OriginalPath);
        string? source = record.Source.ProcessName;
        if (!string.IsNullOrWhiteSpace(source) && source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            source = source[..^4];
        }

        App.Services.GetRequiredService<IWindowPresenter>()
            .ShowAiActions(AgentReviewLaunch.FromShelf(id, fileName, source, "choose"));
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
            e.Handled = true;
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
            Background = OctadockDesignTokens.Brushes.Field,
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

    private async void OnCreateMockup(object sender, RoutedEventArgs e)
        => await CreateMockupAsync().ConfigureAwait(true);

    private async Task CreateMockupAsync()
    {
        // The region boundary is a rectangle or an ellipse (circle the area you
        // want changed); either way its frame supplies the bounding box.
        if (_viewModel?.SelectedObject is not { Type: AnnotationObjectType.Rectangle or AnnotationObjectType.Ellipse } selection)
        {
            _notifications.Notify(
                "Select a region",
                "Circle the area you want to change (or draw a rectangle), then choose Mockup.",
                NotificationKind.Info);
            return;
        }

        AnnotationFrame frame = selection.Frame;
        var region = new ImageEditRegion(
            (int)Math.Floor(frame.X),
            (int)Math.Floor(frame.Y),
            (int)Math.Ceiling(frame.Width),
            (int)Math.Ceiling(frame.Height));
        if (region.Width < 4 || region.Height < 4)
        {
            _notifications.Notify("Selection too small", "Choose a larger rectangle.", NotificationKind.Warning);
            return;
        }

        try
        {
            // The selection shape is a boundary, not part of the design sent
            // to the provider or shown in the approved result.
            var cleanDocument = new AnnotationDocument(
                _viewModel.Document.CanvasSize,
                _viewModel.Document.SourceCaptureId);
            cleanDocument.ReplaceAll(_viewModel.Document.Objects.Where(item => item.Id != selection.Id));
            byte[] sourcePng = EditorExporter.FlattenToPng(cleanDocument, _viewModel.BaseImage);
            var dialog = new ImageMockupWindow(_mockups, _imageEditProvider, sourcePng, region)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true || dialog.Result is not { } result)
            {
                return;
            }

            await PersistApprovedMockupAsync(result, dialog.TextDelta).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image mockup workflow failed.");
            _notifications.Notify("Mockup failed", ex.Message, NotificationKind.Error);
        }
    }

    private async Task PersistApprovedMockupAsync(ImageMockupResult result, string textDelta)
    {
        if (_sourceCaptureId is not { } captureId)
        {
            var save = new SaveFileDialog
            {
                Title = "Save approved mockup",
                Filter = "PNG image (*.png)|*.png",
                DefaultExt = ".png",
                InitialDirectory = SafeDir(DefaultExportDirectory()),
                FileName = $"octadock-mockup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png",
            };
            if (save.ShowDialog(this) == true)
            {
                await _safeWriter.WriteAsync(save.FileName, result.CompositePng).ConfigureAwait(true);
                _notifications.Notify("Mockup saved", Path.GetFileName(save.FileName), NotificationKind.Success);
            }
            return;
        }

        CaptureRecord? capture = await _captures.GetAsync(captureId).ConfigureAwait(true);
        if (capture is null)
        {
            throw new InvalidOperationException("The source capture no longer exists.");
        }

        Guid variantId = Guid.NewGuid();
        string relative = _paths.BuildMockupRelativePath(captureId, variantId, DateTimeOffset.Now);
        string absolute = _paths.ToAbsolute(relative);
        await _safeWriter.WriteAsync(absolute, result.CompositePng).ConfigureAwait(true);

        string? previous = capture.ApprovedMockupPath;
        CaptureRecord updated = capture with { ApprovedMockupPath = relative };
        await _captures.UpdateAsync(updated).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(previous) &&
            !string.Equals(previous, relative, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteManagedFile(previous);
        }

        string metadata = JsonSerializer.Serialize(new
        {
            schema = "octadock-image-mockup/v1",
            variantId,
            provider = result.Provider,
            model = result.Model,
            instruction = textDelta,
            selectedRegion = result.SelectedRegion,
            contextRegion = result.ContextRegion,
            result.OriginalSha256,
            result.CompositeSha256,
            result.SentCropSha256,
            result.ChangedPixelsInsideSelection,
            result.ChangedPixelsOutsideSelection,
            approvedMockupPath = relative,
        });
        await _actions.AddAsync(new ActionRecord
        {
            Id = Guid.NewGuid(),
            CaptureId = captureId,
            ActionType = ActionType.MockupApproved,
            CreatedAt = DateTimeOffset.UtcNow,
            Destination = result.Provider,
            MetadataJson = metadata,
        }).ConfigureAwait(true);

        // Reuse the existing compact Capture Shelf; the approved variant is the
        // image shown, while actions remain linked to the source capture.
        await _shelf.ShowAsync(updated with
        {
            OriginalPath = relative,
            ThumbnailPath = null,
        }).ConfigureAwait(true);
        _notifications.Notify(
            "Mockup approved",
            "The verified variant is on the Capture Shelf and linked to its source.",
            NotificationKind.Success);
    }

    private void TryDeleteManagedFile(string relative)
    {
        try
        {
            string absolute = _paths.ToAbsolute(relative);
            if (IsUnderStorageRoot(absolute) && File.Exists(absolute)) File.Delete(absolute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not delete superseded mockup {Path}.", relative);
        }
    }

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

            await App.Services.GetRequiredService<ISafeFileWriter>()
                .WriteAsync(path, bytes).ConfigureAwait(true);
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
            Directory.CreateDirectory(_paths.TempExportsDirectory);
            string temp = Path.Combine(_paths.TempExportsDirectory, $"octadock-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(temp, png);
            _clipboard.SetImageFromFile(temp);
            RecordAction(ActionType.Copied, "clipboard");
            _notifications.Notify("Copied", "The annotated image is on the clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy the flattened image.");
            _notifications.Notify("Copy failed", "Could not copy the annotated image.", NotificationKind.Error);
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

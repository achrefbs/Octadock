using System.IO;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Io;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// One card on the Capture Shelf. Wraps a <see cref="CaptureRecord"/>, loads its
/// thumbnail, and exposes the shelf actions (Copy, Save, Annotate, Pin, Discard) plus
/// the context-menu operations (Save As, Copy File, Flip/Rotate, Scale to 1×, reveal
/// in Explorer). Every action records an <see cref="ActionRecord"/> for the history
/// timeline; heavy work (encode / file IO / DB) runs off the UI thread. Destructive
/// Discard is a soft-delete so it can be restored.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class ShelfItemViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IImageLoadService _imaging;
    private readonly IClipboardService _clipboard;
    private readonly IStoragePaths _paths;
    private readonly ICaptureRepository _captures;
    private readonly IActionRepository _actions;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly ILogger _logger;
    private readonly Func<ShelfItemViewModel, Task> _onDiscarded;
    private readonly Action<ShelfItemViewModel> _onActionCompleted;

    private CaptureRecord _record;

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPinnedIndicator;

    /// <summary>Creates a shelf card for a capture.</summary>
    public ShelfItemViewModel(
        CaptureRecord record,
        IServiceProvider services,
        Func<ShelfItemViewModel, Task> onDiscarded,
        Action<ShelfItemViewModel> onActionCompleted)
    {
        _record = record;
        _services = services;
        _onDiscarded = onDiscarded;
        _onActionCompleted = onActionCompleted;

        _imaging = services.GetRequiredService<IImageLoadService>();
        _clipboard = services.GetRequiredService<IClipboardService>();
        _paths = services.GetRequiredService<IStoragePaths>();
        _captures = services.GetRequiredService<ICaptureRepository>();
        _actions = services.GetRequiredService<IActionRepository>();
        _settings = services.GetRequiredService<ISettingsService>();
        _notifications = services.GetRequiredService<INotificationService>();
        _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ShelfItem");

        LoadThumbnail();
    }

    /// <summary>The underlying capture record.</summary>
    public CaptureRecord Record => _record;

    /// <summary>The absolute path to the original raster, used for drag-out and copy.</summary>
    public string AbsoluteOriginalPath => _paths.ToAbsolute(_record.OriginalPath);

    /// <summary>True when this shelf card represents a video recording.</summary>
    public bool IsRecording => _record.IsRecording;

    /// <summary>True when image-only operations are available.</summary>
    public bool IsImage => !IsRecording;

    /// <summary>Display filename (from the original path).</summary>
    public string FileName => Path.GetFileName(_record.OriginalPath);

    /// <summary>"W × H" in physical pixels.</summary>
    public string Dimensions => _record.PixelWidth > 0 && _record.PixelHeight > 0
        ? $"{_record.PixelWidth} × {_record.PixelHeight}"
        : Path.GetExtension(_record.OriginalPath).TrimStart('.').ToUpperInvariant();

    /// <summary>Short duration label for recording shelf cards.</summary>
    public string DurationLabel
    {
        get
        {
            if (_record.DurationMs is not > 0)
            {
                return "MP4 video";
            }

            var duration = TimeSpan.FromMilliseconds(_record.DurationMs.Value);
            string format = duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss";
            return duration.ToString(format, CultureInfo.InvariantCulture);
        }
    }

    private void LoadThumbnail()
    {
        try
        {
            if (IsRecording && string.IsNullOrWhiteSpace(_record.ThumbnailPath))
            {
                return;
            }

            string thumb = _paths.ToAbsolute(
                string.IsNullOrWhiteSpace(_record.ThumbnailPath) ? _record.OriginalPath : _record.ThumbnailPath!);
            if (File.Exists(thumb))
            {
                Thumbnail = _imaging.LoadFromFile(thumb);
            }
            else if (!IsRecording && File.Exists(AbsoluteOriginalPath))
            {
                Thumbnail = _imaging.LoadFromFile(AbsoluteOriginalPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load shelf thumbnail for {Id}.", _record.Id);
        }
    }

    // ---- Primary actions ----------------------------------------------------

    /// <summary>Copies the image to the clipboard (keeps the card).</summary>
    [RelayCommand]
    private async Task CopyAsync()
    {
        if (IsRecording)
        {
            await CopyFileCoreAsync(completeAction: true).ConfigureAwait(true);
            return;
        }

        try
        {
            await RunOffThreadAsync(() => _clipboard.SetImageFromFile(AbsoluteOriginalPath)).ConfigureAwait(true);
            await RecordActionAsync(ActionType.Copied, "clipboard").ConfigureAwait(true);
            Notify("Copied", "The capture is on your clipboard.", NotificationKind.Success);
            _onActionCompleted(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy capture {Id}.", _record.Id);
            Notify("Copy failed", "Could not copy the capture.", NotificationKind.Error);
        }
    }

    /// <summary>Saves the image to the configured directory, or prompts with Save As.</summary>
    [RelayCommand]
    private Task SaveAsync() => SaveCoreAsync(promptAlways: false);

    /// <summary>Always prompts for a destination (context menu "Save As…").</summary>
    [RelayCommand]
    private Task SaveAsAsync() => SaveCoreAsync(promptAlways: true);

    private async Task SaveCoreAsync(bool promptAlways)
    {
        string source = AbsoluteOriginalPath;
        string extension = Path.GetExtension(source);
        string configuredDir = _settings.Current.Capture.SaveDirectory;

        string? destination;
        bool savedAs = false;
        if (!promptAlways && !string.IsNullOrWhiteSpace(configuredDir))
        {
            Directory.CreateDirectory(configuredDir);
            destination = Path.Combine(configuredDir, FileName);
        }
        else
        {
            destination = PromptSaveAs(FileName, extension);
            savedAs = destination is not null;
            if (destination is null)
            {
                return; // cancelled; keep the card
            }
        }

        string finalDestination = destination;
        try
        {
            await RunOffThreadAsync(() => File.Copy(source, finalDestination, overwrite: true)).ConfigureAwait(true);
            await RecordActionAsync(savedAs ? ActionType.SavedAs : ActionType.Saved, finalDestination).ConfigureAwait(true);
            Notify("Saved", Path.GetFileName(finalDestination), NotificationKind.Success);
            _onActionCompleted(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save capture {Id}.", _record.Id);
            Notify("Save failed", "Could not save the capture.", NotificationKind.Error);
        }
    }

    /// <summary>Opens the annotation editor for this capture, when available.</summary>
    [RelayCommand]
    private async Task AnnotateAsync()
    {
        if (IsRecording)
        {
            Notify("Annotate", "Video recordings are not editable in the annotation editor.", NotificationKind.Info);
            return;
        }

        var annotations = _services.GetService<IAnnotationService>();
        if (annotations is null)
        {
            Notify("Annotate", "The annotation editor is not available yet.", NotificationKind.Warning);
            return;
        }

        try
        {
            await annotations.OpenAsync(_record).ConfigureAwait(true);
            await RecordActionAsync(ActionType.Annotated, destination: null).ConfigureAwait(true);
            _onActionCompleted(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to annotate capture {Id}.", _record.Id);
            Notify("Annotate failed", "Could not open the annotation editor.", NotificationKind.Error);
        }
    }

    /// <summary>Pins the capture as a floating always-on-top window, when available.</summary>
    [RelayCommand]
    private async Task PinAsync()
    {
        if (IsRecording)
        {
            Notify("Pin", "Video recordings cannot be pinned yet.", NotificationKind.Info);
            return;
        }

        var pins = _services.GetService<IPinService>();
        if (pins is null)
        {
            Notify("Pin", "Floating pins are not available yet.", NotificationKind.Warning);
            return;
        }

        try
        {
            await pins.PinCaptureAsync(_record).ConfigureAwait(true);
            await RecordActionAsync(ActionType.Pinned, destination: null).ConfigureAwait(true);
            IsPinnedIndicator = true;
            _onActionCompleted(this);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pin capture {Id}.", _record.Id);
            Notify("Pin failed", "Could not pin the capture.", NotificationKind.Error);
        }
    }

    /// <summary>Soft-deletes the capture and removes the card (restorable).</summary>
    [RelayCommand]
    private async Task DiscardAsync()
    {
        try
        {
            await _captures.SoftDeleteAsync(_record.Id, DateTimeOffset.Now).ConfigureAwait(true);
            await RecordActionAsync(ActionType.Discarded, destination: null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to soft-delete capture {Id}.", _record.Id);
        }

        await _onDiscarded(this).ConfigureAwait(true);
    }

    // ---- Context-menu operations -------------------------------------------

    /// <summary>Copies the image to the clipboard without triggering after-action close (context menu).</summary>
    [RelayCommand]
    private async Task CopyKeepAsync()
    {
        if (IsRecording)
        {
            await CopyFileCoreAsync(completeAction: false).ConfigureAwait(true);
            return;
        }

        try
        {
            await RunOffThreadAsync(() => _clipboard.SetImageFromFile(AbsoluteOriginalPath)).ConfigureAwait(true);
            await RecordActionAsync(ActionType.Copied, "clipboard").ConfigureAwait(true);
            Notify("Copied", "The capture is on your clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy capture {Id}.", _record.Id);
            Notify("Copy failed", "Could not copy the capture.", NotificationKind.Error);
        }
    }

    /// <summary>Copies the file itself (file-drop) to the clipboard, keeping the card.</summary>
    [RelayCommand]
    private Task CopyFileAsync() => CopyFileCoreAsync(completeAction: false);

    private async Task CopyFileCoreAsync(bool completeAction)
    {
        try
        {
            await RunOffThreadAsync(() => _clipboard.SetFileDropList(new[] { AbsoluteOriginalPath })).ConfigureAwait(true);
            await RecordActionAsync(ActionType.Copied, "file").ConfigureAwait(true);
            Notify("Copied file", FileName, NotificationKind.Success);
            if (completeAction)
            {
                _onActionCompleted(this);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy file for capture {Id}.", _record.Id);
            Notify("Copy failed", "Could not copy the file.", NotificationKind.Error);
        }
    }

    /// <summary>Flips the image horizontally and rewrites the original + thumbnail.</summary>
    [RelayCommand]
    private Task FlipHorizontalAsync() => TransformAsync(new ScaleTransform(-1, 1));

    /// <summary>Flips the image vertically.</summary>
    [RelayCommand]
    private Task FlipVerticalAsync() => TransformAsync(new ScaleTransform(1, -1));

    /// <summary>Rotates the image 90° clockwise.</summary>
    [RelayCommand]
    private Task RotateAsync() => TransformAsync(new RotateTransform(90));

    /// <summary>Rescales a hi-DPI capture down to logical 1× size.</summary>
    [RelayCommand]
    private Task ScaleToOneXAsync()
    {
        double scale = _record.DpiScale <= 0 ? 1.0 : _record.DpiScale;
        if (Math.Abs(scale - 1.0) < 0.01)
        {
            Notify("Scale", "This capture is already at 1× scale.", NotificationKind.Info);
            return Task.CompletedTask;
        }

        return TransformAsync(new ScaleTransform(1.0 / scale, 1.0 / scale), resetDpiScale: true);
    }

    private async Task TransformAsync(Transform transform, bool resetDpiScale = false)
    {
        if (IsRecording)
        {
            Notify("Edit", "Video recordings cannot be transformed.", NotificationKind.Info);
            return;
        }

        string path = AbsoluteOriginalPath;
        try
        {
            (int width, int height) = await Task.Run(() =>
            {
                BitmapSource source = _imaging.LoadFromFile(path);
                var transformed = new TransformedBitmap(source, transform);
                transformed.Freeze();
                byte[] png = _imaging.EncodePng(transformed);
                // Writeback to the ORIGINAL capture goes through SafeFileWriter so a
                // crash mid-save cannot corrupt it (WS9, R23).
                _services.GetRequiredService<ISafeFileWriter>()
                    .WriteAsync(path, png).GetAwaiter().GetResult();

                // Refresh the thumbnail file too, when present.
                if (!string.IsNullOrWhiteSpace(_record.ThumbnailPath))
                {
                    string thumbPath = _paths.ToAbsolute(_record.ThumbnailPath!);
                    try
                    {
                        var thumbs = _services.GetService<IThumbnailGenerator>();
                        thumbs?.GenerateToFileAsync(path, thumbPath).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to refresh thumbnail after transform for {Id}.", _record.Id);
                    }
                }

                return (transformed.PixelWidth, transformed.PixelHeight);
            }).ConfigureAwait(true);

            // Update the record's dimensions (and DPI when scaled to 1×).
            _record = _record with
            {
                PixelWidth = width,
                PixelHeight = height,
                DpiScale = resetDpiScale ? 1.0 : _record.DpiScale,
            };
            await _captures.UpdateAsync(_record).ConfigureAwait(true);

            OnPropertyChanged(nameof(Dimensions));
            LoadThumbnail();
            await RecordActionAsync(ActionType.Exported, "transform").ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to transform capture {Id}.", _record.Id);
            Notify("Edit failed", ex.Message, NotificationKind.Error);
        }
    }

    /// <summary>Opens the containing folder with the file selected in Explorer.</summary>
    [RelayCommand]
    private void RevealInExplorer()
    {
        try
        {
            string path = AbsoluteOriginalPath;
            if (File.Exists(path))
            {
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                }))
                {
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reveal {Id} in Explorer.", _record.Id);
        }
    }

    /// <summary>Opens the containing folder in Explorer.</summary>
    [RelayCommand]
    private void OpenFileLocation()
    {
        try
        {
            string? dir = Path.GetDirectoryName(AbsoluteOriginalPath);
            if (dir is not null && Directory.Exists(dir))
            {
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                }))
                {
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open the file location for {Id}.", _record.Id);
        }
    }

    // ---- Drag-out support (called from the view) ----------------------------

    /// <summary>
    /// Records a drag-out action and returns a bitmap for the drag payload. The view
    /// performs the actual <c>DoDragDrop</c> with a <c>DataObject</c> carrying both the
    /// file path (FileDrop) and this bitmap so Explorer, uploads and chat apps all work.
    /// </summary>
    public BitmapSource? GetDragBitmap()
    {
        if (IsRecording)
        {
            return Thumbnail;
        }

        try
        {
            return _imaging.LoadFromFile(AbsoluteOriginalPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to build a drag bitmap for {Id}.", _record.Id);
            return Thumbnail;
        }
    }

    /// <summary>Records that this capture was dragged out to another app.</summary>
    public void NotifyDraggedOut() => _ = RecordActionAsync(ActionType.DraggedOut, "drag-drop");

    // ---- Helpers ------------------------------------------------------------

    private static Task RunOffThreadAsync(Action work) => Task.Run(work);

    private async Task RecordActionAsync(ActionType type, string? destination)
    {
        try
        {
            await _actions.AddAsync(new ActionRecord
            {
                Id = Guid.NewGuid(),
                CaptureId = _record.Id,
                ActionType = type,
                CreatedAt = DateTimeOffset.Now,
                Destination = destination,
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record {Type} for {Id}.", type, _record.Id);
        }
    }

    private string? PromptSaveAs(string suggestedName, string extension)
    {
        string filter = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "JPEG image (*.jpg)|*.jpg|PNG image (*.png)|*.png|All files (*.*)|*.*",
            ".png" => "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|All files (*.*)|*.*",
            ".mp4" => "MP4 video (*.mp4)|*.mp4|All files (*.*)|*.*",
            _ when !string.IsNullOrWhiteSpace(extension) =>
                $"{extension.TrimStart('.').ToUpperInvariant()} file (*{extension})|*{extension}|All files (*.*)|*.*",
            _ => "All files (*.*)|*.*",
        };

        var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            DefaultExt = extension,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void Notify(string title, string message, NotificationKind kind)
        => _notifications.Notify(title, message, kind);
}

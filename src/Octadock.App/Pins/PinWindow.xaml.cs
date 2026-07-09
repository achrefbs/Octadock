using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Octadock.App.CaptureUx;
using Octadock.App.Services;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Io;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.Pins;

/// <summary>
/// A floating image window: a borderless image surface that can be pinned above
/// normal application windows. Supports drag-to-move, corner resize, an opacity slider,
/// copy / save / annotate / close actions, arrow-key nudging, middle-click close
/// and a pinned-on-top mode. Its state is persisted as a <see cref="PinRecord"/>
/// so pinned image windows survive restarts.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class PinWindow : ToolWindowBase
{
    private const int NudgeStep = 1;
    private const int NudgeStepLarge = 10;

    private readonly IImageLoadService _images;
    private readonly IClipboardService _clipboard;
    private readonly IStoragePaths _paths;
    private readonly ICaptureRepository _captures;
    private readonly IPinRepository _pinRepository;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IMonitorService _monitors;
    private readonly IAnnotationService _annotation;

    private PinViewModel _viewModel = null!;
    private Guid _pinId = Guid.NewGuid();
    private Guid? _captureId;
    private string? _pinImagePath;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private CancellationTokenSource _persistenceCancellation = new();
    private Task? _closeTask;
    private bool _closing;
    private bool _persistenceDisposed;

    // Drag-move state.
    private bool _dragging;
    private System.Windows.Point _dragOrigin;

    // Legacy unlock watchdog for old persisted click-through pins. The visible
    // lock button now locks position only, so normal unlock stays clickable.
    private DispatcherTimer? _unlockWatch;

    /// <summary>Raised when the pin closes so the service can drop its reference.</summary>
    public event EventHandler<Guid>? PinClosed;

    /// <summary>Creates a pin window with its Core services (resolved via DI).</summary>
    public PinWindow(
        IImageLoadService images,
        IClipboardService clipboard,
        IStoragePaths paths,
        ICaptureRepository captures,
        IPinRepository pinRepository,
        ISettingsService settings,
        INotificationService notifications,
        IMonitorService monitors,
        IAnnotationService annotation)
    {
        _images = images;
        _clipboard = clipboard;
        _paths = paths;
        _captures = captures;
        _pinRepository = pinRepository;
        _settings = settings;
        _notifications = notifications;
        _monitors = monitors;
        _annotation = annotation;

        InitializeComponent();
        Topmost = false;
        ConfigureInkLayer();
    }

    private void ConfigureInkLayer()
    {
        InkLayer.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.White,
            Width = 4,
            Height = 4,
            FitToCurve = true,
            IgnorePressure = false,
        };
    }

    /// <summary>The pin's stable id (also the persisted <see cref="PinRecord.Id"/>).</summary>
    public Guid PinId => _pinId;

    /// <summary>Configures the pin content and initial state, then shows it.</summary>
    public void Initialize(
        BitmapSource image,
        Guid? captureId,
        string? imagePath,
        Guid? pinId = null,
        PixelRect? bounds = null,
        double opacity = 1.0,
        bool clickThrough = false)
    {
        _captureId = captureId;
        _pinImagePath = imagePath;
        if (pinId is { } id)
        {
            _pinId = id;
        }

        _viewModel = new PinViewModel(image, new Actions(this))
        {
            Opacity = opacity,
            Title = BuildTitle(imagePath, captureId),
        };
        DataContext = _viewModel;

        // Size: use persisted bounds, else the image's natural size (capped to the
        // owning monitor's work area so huge captures still fit).
        if (bounds is { } b && b.Width > 0 && b.Height > 0)
        {
            DisplayInfo monitor = _monitors.GetMonitorFromPoint(b.Location);
            double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
            Left = (b.X - monitor.Bounds.X) / scale + monitor.Bounds.X;
            Top = (b.Y - monitor.Bounds.Y) / scale + monitor.Bounds.Y;
            Width = b.Width / scale;
            Height = b.Height / scale;
        }
        else
        {
            DisplayInfo monitor = _monitors.GetActiveMonitor();
            double maxW = monitor.WorkArea.Width * 0.6;
            double maxH = monitor.WorkArea.Height * 0.6;
            double scale = Math.Min(1.0, Math.Min(maxW / image.PixelWidth, maxH / image.PixelHeight));
            Width = Math.Max(MinWidth, image.PixelWidth * scale);
            Height = Math.Max(MinHeight, image.PixelHeight * scale);
            Left = monitor.WorkArea.X + ((monitor.WorkArea.Width - Width) / 2);
            Top = monitor.WorkArea.Y + ((monitor.WorkArea.Height - Height) / 2);
        }

        Opacity = opacity;
        _viewModel.IsLocked = clickThrough;
        ApplyPinState(clickThrough, notify: false);
        Show();

        if (bounds is { } physicalBounds && !physicalBounds.IsEmpty)
        {
            // Clamp the saved physical rectangle against the current monitor layout so a
            // pin persisted on a monitor that is now gone (or repositioned) does not
            // restore off-screen with no way to recover it.
            PixelRect placement = ClampPersistedBounds(physicalBounds);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PinInterop.PositionPhysical(Hwnd, placement);
                _ = PersistAsync();
            }));
        }
        else
        {
            _ = PersistAsync();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PinInterop.EnsureBaseStyles(Hwnd);
    }

    // ---- Move (drag the image body) ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_viewModel.IsPenActive)
        {
            return;
        }

        // Ignore clicks that originate on the toolbar / grips (they have their own handlers).
        if (e.OriginalSource is DependencyObject src && IsWithinChrome(src))
        {
            return;
        }

        if (_viewModel.IsLocked)
        {
            return;
        }

        _dragging = true;
        _dragOrigin = e.GetPosition(this);
        CaptureMouse();
        Focus();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(this);
        Left += current.X - _dragOrigin.X;
        Top += current.Y - _dragOrigin.Y;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            _ = PersistAsync();
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            ClosePin();
            e.Handled = true;
        }
    }

    // ---- Arrow-key nudge ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_viewModel.IsLocked && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            e.Handled = true;
            return;
        }

        int step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? NudgeStepLarge : NudgeStep;

        switch (e.Key)
        {
            case Key.Left:
                Left -= step;
                e.Handled = true;
                break;
            case Key.Right:
                Left += step;
                e.Handled = true;
                break;
            case Key.Up:
                Top -= step;
                e.Handled = true;
                break;
            case Key.Down:
                Top += step;
                e.Handled = true;
                break;
            case Key.Escape:
                ClosePin();
                e.Handled = true;
                return;
            default:
                return;
        }

        _ = PersistAsync();
    }

    // ---- Resize grips ----

    private void OnResizeBottomRight(object sender, DragDeltaEventArgs e)
    {
        if (_viewModel.IsLocked)
        {
            return;
        }

        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
        _ = PersistAsync();
    }

    private void OnResizeBottomLeft(object sender, DragDeltaEventArgs e)
    {
        if (_viewModel.IsLocked)
        {
            return;
        }

        double newWidth = Math.Max(MinWidth, Width - e.HorizontalChange);
        Left += Width - newWidth;
        Width = newWidth;
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
        _ = PersistAsync();
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    // ---- Pin on top ----

    private void ApplyPinState(bool enabled, bool notify = true)
    {
        Topmost = enabled;
        if (Hwnd != IntPtr.Zero)
        {
            PinInterop.SetClickThrough(Hwnd, false);
        }

        StopUnlockWatch();

        if (!notify)
        {
            return;
        }

        // Pin/unpin is visible in the toolbar/footer state; no toast needed.
    }

    /// <summary>
    /// While the pin is click-through it cannot receive mouse events, so an
    /// out-of-band affordance is needed to unlock it. We poll for a Ctrl+Alt chord
    /// while the cursor is over the pin's rectangle and unlock when detected. This
    /// keeps the unlock self-contained (no global hotkey registration required).
    /// </summary>
    private void StartUnlockWatch()
    {
        _unlockWatch ??= new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _unlockWatch.Tick -= OnUnlockTick;
        _unlockWatch.Tick += OnUnlockTick;
        _unlockWatch.Start();
    }

    private void StopUnlockWatch()
    {
        if (_unlockWatch is not null)
        {
            _unlockWatch.Stop();
            _unlockWatch.Tick -= OnUnlockTick;
        }
    }

    private void OnUnlockTick(object? sender, EventArgs e)
    {
        const int vkControl = 0x11;
        const int vkMenu = 0x12; // Alt
        bool chord = (GetAsyncKeyState(vkControl) & 0x8000) != 0
                  && (GetAsyncKeyState(vkMenu) & 0x8000) != 0;
        if (!chord)
        {
            return;
        }

        // Only unlock if the cursor is over this pin (so the chord targets one pin).
        // GetCursorPos returns physical pixels, so compare against the pin's physical
        // bounds — CurrentBounds() is in DIPs and mismatches on scaled monitors,
        // which could otherwise make a locked click-through pin impossible to unlock.
        if (GetCursorPos(out POINT p))
        {
            var cursor = new PixelRect(p.X, p.Y, 1, 1);
            if (!CurrentPhysicalBounds().IntersectsWith(cursor))
            {
                return;
            }
        }

        _viewModel.IsLocked = false;
        ApplyPinState(false);
        Activate();
        _ = PersistAsync();
    }

    /// <summary>Unlocks the pin if it is currently click-through (used by the service / tray).</summary>
    public void ForceUnlock()
    {
        if (!_viewModel.IsLocked)
        {
            return;
        }

        _viewModel.IsLocked = false;
        ApplyPinState(false);
        _ = PersistAsync();
    }

    // ---- Pin actions (invoked by the view model) ----

    private Task CopyImageAsync()
    {
        try
        {
            byte[] png = _images.EncodePng(BuildComposedImage());
            _clipboard.SetImage(new EncodedImage(png, ExportImageFormat.Png));
        }
        catch (Exception)
        {
            _notifications.Notify("Copy failed", "Could not copy the pinned image.", NotificationKind.Error);
        }

        return Task.CompletedTask;
    }

    private async Task SaveImageAsync()
    {
        BitmapSource image = BuildComposedImage();
        string? sourcePath = await ResolveSourcePathAsync().ConfigureAwait(true);
        ImageEditSaveBehavior behavior = _settings.Current.Capture.ImageEditSaveBehavior;

        try
        {
            if (behavior == ImageEditSaveBehavior.Ask)
            {
                ImageSaveChoice? choice = ImageSaveChoiceDialog.Prompt(this, CanOverwriteOriginal(sourcePath));
                if (choice is null)
                {
                    return;
                }

                behavior = choice.Value.Behavior;
                if (choice.Value.Remember)
                {
                    await _settings.UpdateAsync(s => s with
                    {
                        Capture = s.Capture with { ImageEditSaveBehavior = behavior },
                    }).ConfigureAwait(true);
                }
            }

            if (behavior == ImageEditSaveBehavior.OverwriteOriginal)
            {
                if (CanOverwriteOriginal(sourcePath))
                {
                    await WriteImageAsync(sourcePath!, image).ConfigureAwait(true);
                    await NotifySourceImageSavedAsync(sourcePath!).ConfigureAwait(true);
                    return;
                }

                _notifications.Notify(
                    "Save as copy",
                    "This source format cannot be overwritten yet. Choose a new PNG or JPEG copy.",
                    NotificationKind.Info);
            }

            await SaveImageCopyAsAsync(image, sourcePath).ConfigureAwait(true);
        }
        catch (Exception)
        {
            _notifications.Notify("Save failed", "Could not save the pinned image.", NotificationKind.Error);
        }
    }

    private async Task AdvancedAnnotateImageAsync()
    {
        // Persist the pin image to a temp file and open it for annotation.
        try
        {
            Directory.CreateDirectory(_paths.TempExportsDirectory);
            string temp = Path.Combine(_paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
            byte[] png = _images.EncodePng(BuildComposedImage());
            await File.WriteAllBytesAsync(temp, png).ConfigureAwait(true);
            await _annotation.OpenFileAsync(temp).ConfigureAwait(true);
        }
        catch (Exception)
        {
            _notifications.Notify("Annotate failed", "Could not open the pin in the editor.", NotificationKind.Error);
        }
    }

    private async Task OpenSourceAsync()
    {
        string? path = await ResolveSourcePathAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _notifications.Notify("Open failed", "The source image is no longer on disk.", NotificationKind.Warning);
            return;
        }

        try
        {
            using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            }))
            {
            }
        }
        catch (Exception)
        {
            _notifications.Notify("Open failed", "Could not open the pinned image.", NotificationKind.Error);
        }
    }

    private async Task AddToContextAsync()
    {
        try
        {
            string? sourcePath = await ResolveSourcePathAsync().ConfigureAwait(true);
            string path = !HasInk() && !string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath)
                ? sourcePath
                : await SaveComposedTempAsync().ConfigureAwait(true);

            ContextService context = App.Services.GetRequiredService<ContextService>();
            IReadOnlyList<Octadock.Core.Context.ContextPackage> packages =
                await context.GetPackagesAsync().ConfigureAwait(true);
            Octadock.Core.Context.ContextPackage? package = packages.FirstOrDefault()
                ?? await context.CreatePackageAsync($"Context {DateTimeOffset.Now:yyyy-MM-dd HH:mm}").ConfigureAwait(true);

            if (package is null)
            {
                _notifications.Notify("Context", "Adding to Context needs an active trial or license.", NotificationKind.Warning);
                return;
            }

            if (await context.AddFileAsync(package.Id, path).ConfigureAwait(true))
            {
                App.Services.GetService<IWindowPresenter>()?.ShowContext();
            }
        }
        catch (Exception)
        {
            _notifications.Notify("Context", "Could not add the image to Context.", NotificationKind.Error);
        }
    }

    private BitmapSource BuildComposedImage()
    {
        if (!HasInk() || InkLayer.ActualWidth <= 1 || InkLayer.ActualHeight <= 1)
        {
            return _viewModel.Image;
        }

        int width = _viewModel.Image.PixelWidth;
        int height = _viewModel.Image.PixelHeight;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(_viewModel.Image, new Rect(0, 0, width, height));
            dc.PushTransform(new ScaleTransform(width / InkLayer.ActualWidth, height / InkLayer.ActualHeight));
            foreach (Stroke stroke in InkLayer.Strokes)
            {
                stroke.Draw(dc);
            }

            dc.Pop();
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private bool HasInk() => InkLayer.Strokes.Count > 0;

    private async Task<string> SaveComposedTempAsync()
    {
        Directory.CreateDirectory(_paths.TempExportsDirectory);
        string temp = Path.Combine(_paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(temp, _images.EncodePng(BuildComposedImage())).ConfigureAwait(true);
        return temp;
    }

    private async Task SaveImageCopyAsAsync(BitmapSource image, string? sourcePath)
    {
        string sourceExtension = string.IsNullOrWhiteSpace(sourcePath) ? ".png" : Path.GetExtension(sourcePath);
        string extension = IsJpegExtension(sourceExtension) ? ".jpg" : ".png";
        string baseName = string.IsNullOrWhiteSpace(sourcePath)
            ? $"pin-{DateTimeOffset.Now:yyyyMMdd-HHmmss}"
            : Path.GetFileNameWithoutExtension(sourcePath);

        var dialog = new SaveFileDialog
        {
            Title = "Save pinned image",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg",
            DefaultExt = extension,
            FileName = $"{baseName}-edited{extension}",
            OverwritePrompt = true,
        };

        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            string? directory = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await WriteImageAsync(dialog.FileName, image).ConfigureAwait(true);
    }

    private Task WriteImageAsync(string path, BitmapSource image)
    {
        byte[] bytes = IsJpegExtension(Path.GetExtension(path))
            ? Imaging.FrameImaging.EncodeJpeg(image, 90)
            : _images.EncodePng(image);

        return App.Services.GetRequiredService<ISafeFileWriter>().WriteAsync(path, bytes);
    }

    private static async Task NotifySourceImageSavedAsync(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                App.Services.GetService<ShelfService>() is { } shelf)
            {
                await shelf.RefreshSourceAsync(path).ConfigureAwait(true);
            }
        }
        catch
        {
            // The image is already saved; thumbnail refresh is best-effort UI hygiene.
        }
    }

    private static bool CanOverwriteOriginal(string? path)
        => !string.IsNullOrWhiteSpace(path) &&
           File.Exists(path) &&
           (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            IsJpegExtension(Path.GetExtension(path)));

    private static bool IsJpegExtension(string? extension)
        => extension is not null &&
           (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));

    private async Task RevealSourceAsync()
    {
        string? path = await ResolveSourcePathAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _notifications.Notify("Show failed", "The source image is no longer on disk.", NotificationKind.Warning);
            return;
        }

        try
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
        catch (Exception)
        {
            _notifications.Notify("Show failed", "Could not show the pinned image in Explorer.", NotificationKind.Error);
        }
    }

    private async Task<string?> ResolveSourcePathAsync()
    {
        if (!string.IsNullOrWhiteSpace(_pinImagePath))
        {
            string managedPath = _paths.ToAbsolute(_pinImagePath);
            if (File.Exists(managedPath))
            {
                return managedPath;
            }
        }

        if (_captureId is not { } captureId)
        {
            return null;
        }

        try
        {
            CaptureRecord? capture = await _captures.GetAsync(captureId).ConfigureAwait(true);
            if (capture is null)
            {
                return null;
            }

            return _paths.ToAbsolute(capture.OriginalPath);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildTitle(string? imagePath, Guid? captureId)
    {
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            try
            {
                string fileName = Path.GetFileName(imagePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }
            catch (ArgumentException)
            {
                // Fall back below.
            }
        }

        return captureId is not null ? "Screenshot" : "Image";
    }

    private void ClosePin()
    {
        if (_closeTask is not null)
        {
            return;
        }

        _closeTask = ClosePinAsync();
    }

    private async Task ClosePinAsync()
    {
        if (_closing)
        {
            return;
        }

        try
        {
            _closing = true;
            StopUnlockWatch();

            // Stop queued geometry/opacity writes before deleting. Any write already inside
            // the repository is still serialized by the gate, so DeleteAsync remains last.
            await _persistenceCancellation.CancelAsync().ConfigureAwait(true);

            if (!await ForgetAsync().ConfigureAwait(true))
            {
                ResetPersistenceAfterFailedClose();
                _notifications.Notify(
                    "Pin close failed",
                    "Octadock could not remove this pin from storage. Try closing it again.",
                    NotificationKind.Error);
                return;
            }

            PinClosed?.Invoke(this, _pinId);
            Close();
            DisposePersistence();
        }
        catch (Exception)
        {
            ResetPersistenceAfterFailedClose();
            _notifications.Notify(
                "Pin close failed",
                "Octadock could not close this pin. Try again.",
                NotificationKind.Error);
        }
    }

    /// <summary>
    /// Re-positions this pin fully on-screen if it is currently stranded outside every
    /// monitor (used by the tray "Show All Pins" affordance after a layout change).
    /// </summary>
    public void EnsureOnScreen(IReadOnlyList<DisplayInfo> monitors, DisplayInfo fallback)
    {
        PixelRect current = CurrentPhysicalBounds();
        PixelRect clamped = PinService.ClampToVisible(current, monitors, fallback);
        if (clamped != current)
        {
            PinInterop.PositionPhysical(Hwnd, clamped);
            _ = PersistAsync();
        }
    }

    // ---- Persistence ----

    /// <summary>
    /// Clamps a saved physical rectangle against the live monitor set so a restored pin
    /// is never left entirely off-screen (delegates to <see cref="PinService.ClampToVisible"/>).
    /// </summary>
    private PixelRect ClampPersistedBounds(PixelRect bounds)
    {
        IReadOnlyList<DisplayInfo> monitors;
        try
        {
            monitors = _monitors.GetMonitors();
        }
        catch
        {
            return bounds;
        }

        DisplayInfo fallback = _monitors.GetMonitorFromPoint(bounds.Location);
        return PinService.ClampToVisible(bounds, monitors, fallback);
    }

    private PixelRect CurrentBounds() => new(
        (int)Math.Round(Left),
        (int)Math.Round(Top),
        (int)Math.Round(Width),
        (int)Math.Round(Height));

    private PixelRect CurrentPhysicalBounds()
    {
        if (PinInterop.GetWindowBounds(Hwnd) is { } bounds && !bounds.IsEmpty)
        {
            return bounds;
        }

        DisplayInfo monitor = _monitors.GetMonitorFromPoint(CurrentBounds().Location);
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        return new PixelRect(
            monitor.Bounds.X + (int)Math.Round((Left - monitor.Bounds.X) * scale),
            monitor.Bounds.Y + (int)Math.Round((Top - monitor.Bounds.Y) * scale),
            (int)Math.Round(Width * scale),
            (int)Math.Round(Height * scale));
    }

    private async Task PersistAsync()
    {
        if (_closing)
        {
            return;
        }

        CancellationToken cancellationToken = _persistenceCancellation.Token;
        PixelRect bounds = CurrentPhysicalBounds();
        MonitorId monitorId = SafeMonitorId(bounds);

        var record = new PinRecord
        {
            Id = _pinId,
            CaptureId = _captureId,
            ImagePath = _pinImagePath,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            Opacity = _viewModel.Opacity,
            ClickThrough = _viewModel.IsLocked,
            MonitorId = monitorId,
            LastVisibleAt = DateTimeOffset.UtcNow,
        };

        bool acquired = false;
        try
        {
            await _persistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            if (_closing)
            {
                return;
            }

            await _pinRepository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The pin is closing; queued best-effort saves should not outlive deletion.
        }
        catch (Exception)
        {
            // Persistence is best-effort; never let it break the UI.
        }
        finally
        {
            if (acquired)
            {
                _persistenceGate.Release();
            }
        }
    }

    private async Task<bool> ForgetAsync()
    {
        bool acquired = false;
        try
        {
            await _persistenceGate.WaitAsync().ConfigureAwait(false);
            acquired = true;
            await _pinRepository.DeleteAsync(_pinId).ConfigureAwait(false);
            TryDeleteManagedPinImage();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (acquired)
            {
                _persistenceGate.Release();
            }
        }
    }

    private void DisposePersistence()
    {
        if (_persistenceDisposed)
        {
            return;
        }

        _persistenceDisposed = true;
        _persistenceCancellation.Dispose();
        _persistenceGate.Dispose();
    }

    private void ResetPersistenceAfterFailedClose()
    {
        _closing = false;
        _closeTask = null;
        _persistenceCancellation.Dispose();
        _persistenceCancellation = new CancellationTokenSource();
    }

    private void TryDeleteManagedPinImage()
    {
        if (string.IsNullOrWhiteSpace(_pinImagePath) ||
            Path.IsPathRooted(_pinImagePath) ||
            !_pinImagePath.StartsWith("Pins/", StringComparison.OrdinalIgnoreCase) &&
            !_pinImagePath.StartsWith("Pins\\", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            string path = _paths.ToAbsolute(_pinImagePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private MonitorId SafeMonitorId(PixelRect bounds)
    {
        try
        {
            return _monitors.GetMonitorFromPoint(bounds.Location).Id;
        }
        catch
        {
            return MonitorId.Unknown;
        }
    }

    private bool IsWithinChrome(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null && current != this)
        {
            if (current == Toolbar || current == ResizeBr || current == ResizeBl || current == MoreButton)
            {
                return true;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                      ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void SetPenMode(bool enabled)
    {
        InkLayer.EditingMode = enabled ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        InkLayer.IsHitTestVisible = enabled;
        Cursor = enabled ? Cursors.Cross : Cursors.Arrow;
    }

    private void ClearInk()
    {
        InkLayer.Strokes.Clear();
        _viewModel.IsPenActive = false;
    }

    /// <summary>Adapts the window operations to the view model's <see cref="PinActions"/>.</summary>
    private sealed class Actions(PinWindow owner) : PinActions
    {
        public override Task CopyAsync() => owner.CopyImageAsync();

        public override Task SaveAsync() => owner.SaveImageAsync();

        public override Task AdvancedAnnotateAsync() => owner.AdvancedAnnotateImageAsync();

        public override void ClearInk() => owner.ClearInk();

        public override Task AddToContextAsync() => owner.AddToContextAsync();

        public override Task OpenSourceAsync() => owner.OpenSourceAsync();

        public override Task RevealSourceAsync() => owner.RevealSourceAsync();

        public override void Close() => owner.ClosePin();

        public override void LockChanged(bool locked)
        {
            owner.ApplyPinState(locked);
            _ = owner.PersistAsync();
        }

        public override void OpacityChanged(double opacity)
        {
            owner.Opacity = opacity;
            _ = owner.PersistAsync();
        }

        public override void PenChanged(bool enabled) => owner.SetPenMode(enabled);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}

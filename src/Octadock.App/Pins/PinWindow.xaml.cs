using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Octadock.App.Services;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Pins;

/// <summary>
/// A floating pin window: a borderless, top-most image that hovers above normal
/// application windows. Supports drag-to-move, corner resize, an opacity slider,
/// copy / save / annotate / close actions, arrow-key nudging, middle-click close
/// and a click-through lock (mouse passes to apps beneath). Its state is persisted
/// as a <see cref="PinRecord"/> so pins survive restarts.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class PinWindow : ToolWindowBase
{
    private const int NudgeStep = 1;
    private const int NudgeStepLarge = 10;

    private readonly IImageLoadService _images;
    private readonly IClipboardService _clipboard;
    private readonly IStoragePaths _paths;
    private readonly IPinRepository _pinRepository;
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

    // Unlock watchdog: while click-through is on, poll for the unlock chord.
    private DispatcherTimer? _unlockWatch;

    /// <summary>Raised when the pin closes so the service can drop its reference.</summary>
    public event EventHandler<Guid>? PinClosed;

    /// <summary>Creates a pin window with its Core services (resolved via DI).</summary>
    public PinWindow(
        IImageLoadService images,
        IClipboardService clipboard,
        IStoragePaths paths,
        IPinRepository pinRepository,
        INotificationService notifications,
        IMonitorService monitors,
        IAnnotationService annotation)
    {
        _images = images;
        _clipboard = clipboard;
        _paths = paths;
        _pinRepository = pinRepository;
        _notifications = notifications;
        _monitors = monitors;
        _annotation = annotation;

        InitializeComponent();
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

        _viewModel = new PinViewModel(image, new Actions(this)) { Opacity = opacity };
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
        Show();

        if (clickThrough)
        {
            _viewModel.IsLocked = true;
            ApplyClickThrough(true);
        }

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

        // Ignore clicks that originate on the toolbar / grips (they have their own handlers).
        if (e.OriginalSource is DependencyObject src && IsWithinChrome(src))
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
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
        _ = PersistAsync();
    }

    private void OnResizeBottomLeft(object sender, DragDeltaEventArgs e)
    {
        double newWidth = Math.Max(MinWidth, Width - e.HorizontalChange);
        Left += Width - newWidth;
        Width = newWidth;
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
        _ = PersistAsync();
    }

    // ---- Click-through ----

    private void ApplyClickThrough(bool enabled)
    {
        PinInterop.SetClickThrough(Hwnd, enabled);

        if (enabled)
        {
            StartUnlockWatch();
            _notifications.Notify(
                "Pin locked",
                "Clicks pass through this pin. Hold Ctrl+Alt over it to unlock.",
                NotificationKind.Info);
        }
        else
        {
            StopUnlockWatch();
        }
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
        ApplyClickThrough(false);
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
        ApplyClickThrough(false);
        _ = PersistAsync();
    }

    // ---- Pin actions (invoked by the view model) ----

    private Task CopyImageAsync()
    {
        try
        {
            byte[] png = _images.EncodePng(_viewModel.Image);
            _clipboard.SetImage(new EncodedImage(png, ExportImageFormat.Png));
            _notifications.Notify("Copied", "The pinned image is on the clipboard.", NotificationKind.Success);
        }
        catch (Exception)
        {
            _notifications.Notify("Copy failed", "Could not copy the pinned image.", NotificationKind.Error);
        }

        return Task.CompletedTask;
    }

    private async Task SaveImageAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save pinned image",
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg",
            DefaultExt = ".png",
            FileName = $"pin-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.png",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // Encode to match the chosen extension so a .jpg file contains JPEG
            // bytes rather than PNG bytes written under a .jpg name.
            string ext = Path.GetExtension(dialog.FileName);
            bool jpeg = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            byte[] bytes = jpeg
                ? Imaging.FrameImaging.EncodeJpeg(_viewModel.Image, 90)
                : _images.EncodePng(_viewModel.Image);
            await File.WriteAllBytesAsync(dialog.FileName, bytes).ConfigureAwait(true);
            _notifications.Notify("Saved", Path.GetFileName(dialog.FileName), NotificationKind.Success);
        }
        catch (Exception)
        {
            _notifications.Notify("Save failed", "Could not save the pinned image.", NotificationKind.Error);
        }
    }

    private async Task AnnotateImageAsync()
    {
        // Persist the pin image to a temp file and open it for annotation.
        try
        {
            Directory.CreateDirectory(_paths.TempExportsDirectory);
            string temp = Path.Combine(_paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
            byte[] png = _images.EncodePng(_viewModel.Image);
            await File.WriteAllBytesAsync(temp, png).ConfigureAwait(true);
            await _annotation.OpenFileAsync(temp).ConfigureAwait(true);
        }
        catch (Exception)
        {
            _notifications.Notify("Annotate failed", "Could not open the pin in the editor.", NotificationKind.Error);
        }
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
        if (string.IsNullOrWhiteSpace(_pinImagePath))
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
            if (current == Toolbar || current == ResizeBr || current == ResizeBl)
            {
                return true;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current)
                      ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>Adapts the window operations to the view model's <see cref="PinActions"/>.</summary>
    private sealed class Actions(PinWindow owner) : PinActions
    {
        public override Task CopyAsync() => owner.CopyImageAsync();

        public override Task SaveAsync() => owner.SaveImageAsync();

        public override Task AnnotateAsync() => owner.AnnotateImageAsync();

        public override void Close() => owner.ClosePin();

        public override void LockChanged(bool locked)
        {
            owner.ApplyClickThrough(locked);
            _ = owner.PersistAsync();
        }

        public override void OpacityChanged(double opacity)
        {
            owner.Opacity = opacity;
            _ = owner.PersistAsync();
        }
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

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Octadock.App.Ai;
using Octadock.App.CaptureUx;
using Octadock.App.Services;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Io;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.Pins;

internal readonly record struct PinInitialPlacement(
    PixelRect PhysicalBounds,
    double WidthDip,
    double HeightDip);

/// <summary>
/// A floating image window: a borderless image surface that can be pinned above
/// normal application windows. Supports drag-to-move, corner resize, opacity,
/// copy / save / draw / inline AI / close actions, arrow-key nudging, middle-click close
/// and a pinned-on-top mode. Its state is persisted as a <see cref="PinRecord"/>
/// so pinned image windows survive restarts.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class PinWindow : ToolWindowBase
{
    private const int NudgeStep = 1;
    private const int NudgeStepLarge = 10;
    private const double InitialWorkAreaFraction = 0.6;
    private const double ShellGutterDip = 32;
    private const double MinimumWindowWidthDip = 200;
    private const double MinimumWindowHeightDip = 120;
    private const double ShellCornerRadius = 12;

    private readonly IImageLoadService _images;
    private readonly IClipboardService _clipboard;
    private readonly IStoragePaths _paths;
    private readonly ICaptureRepository _captures;
    private readonly IPinRepository _pinRepository;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IMonitorService _monitors;
    private readonly IImageMockupService _mockups;
    private readonly IImageEditProvider _imageEditProvider;
    private readonly ILicenseGate _licenseGate;

    private PinViewModel _viewModel = null!;
    private Guid _pinId = Guid.NewGuid();
    private Guid? _captureId;
    private string? _pinImagePath;
    private readonly SemaphoreSlim _persistenceGate = new(1, 1);
    private CancellationTokenSource _persistenceCancellation = new();
    private Task? _closeTask;
    private bool _closing;
    private bool _persistenceDisposed;
    private CancellationTokenSource? _aiCancellation;
    private BitmapSource? _aiUndoImage;
    private bool _aiBusy;

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
        IImageMockupService mockups,
        IImageEditProvider imageEditProvider,
        ILicenseGate licenseGate)
    {
        _images = images;
        _clipboard = clipboard;
        _paths = paths;
        _captures = captures;
        _pinRepository = pinRepository;
        _settings = settings;
        _notifications = notifications;
        _monitors = monitors;
        _mockups = mockups;
        _imageEditProvider = imageEditProvider;
        _licenseGate = licenseGate;

        InitializeComponent();
        Topmost = false;
        ConfigureInkLayer();
        Loaded += (_, _) => ApplyShellContentClip();
        Shell.SizeChanged += (_, _) => ApplyShellContentClip();
    }

    private void ApplyShellContentClip()
    {
        if (Shell.ActualWidth <= 0 || Shell.ActualHeight <= 0)
        {
            return;
        }

        ShellContent.Clip = new RectangleGeometry(
            new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight),
            ShellCornerRadius,
            ShellCornerRadius);
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

        PixelRect? initialPhysicalPlacement = null;

        // Size: use persisted bounds, else calculate the complete outer window
        // in physical pixels and center it inside the active monitor work area.
        // WPF's Width/Height are DIP, while DisplayInfo is always physical.
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
            PinInitialPlacement placement = CalculateInitialPlacement(
                image.PixelWidth,
                image.PixelHeight,
                monitor);
            Width = placement.WidthDip;
            Height = placement.HeightDip;
            initialPhysicalPlacement = placement.PhysicalBounds;

            // This is only a pre-HWND placement hint. PositionPhysical below is
            // the authoritative mixed-DPI placement after WPF creates the HWND.
            double dpiScale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
            Left = placement.PhysicalBounds.X / dpiScale;
            Top = placement.PhysicalBounds.Y / dpiScale;
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
        else if (initialPhysicalPlacement is { } initialPlacement)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PinInterop.PositionPhysical(Hwnd, initialPlacement);
                _ = PersistAsync();
            }));
        }
    }

    internal static PinInitialPlacement CalculateInitialPlacement(
        int imagePixelWidth,
        int imagePixelHeight,
        DisplayInfo monitor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imagePixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imagePixelHeight);
        ArgumentNullException.ThrowIfNull(monitor);

        PixelRect work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        double dpiScale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        double gutterPixels = ShellGutterDip * dpiScale;
        double minimumOuterWidth = Math.Min(work.Width, MinimumWindowWidthDip * dpiScale);
        double minimumOuterHeight = Math.Min(work.Height, MinimumWindowHeightDip * dpiScale);
        double maximumOuterWidth = Math.Min(
            work.Width,
            Math.Max(minimumOuterWidth, work.Width * InitialWorkAreaFraction));
        double maximumOuterHeight = Math.Min(
            work.Height,
            Math.Max(minimumOuterHeight, work.Height * InitialWorkAreaFraction));
        double maximumImageWidth = Math.Max(1, maximumOuterWidth - gutterPixels);
        double maximumImageHeight = Math.Max(1, maximumOuterHeight - gutterPixels);
        double imageScale = Math.Min(
            1.0,
            Math.Min(maximumImageWidth / imagePixelWidth, maximumImageHeight / imagePixelHeight));

        int outerWidth = Math.Clamp(
            (int)Math.Round((imagePixelWidth * imageScale) + gutterPixels),
            (int)Math.Ceiling(minimumOuterWidth),
            work.Width);
        int outerHeight = Math.Clamp(
            (int)Math.Round((imagePixelHeight * imageScale) + gutterPixels),
            (int)Math.Ceiling(minimumOuterHeight),
            work.Height);
        int x = work.X + ((work.Width - outerWidth) / 2);
        int y = work.Y + ((work.Height - outerHeight) / 2);

        return new PinInitialPlacement(
            new PixelRect(x, y, outerWidth, outerHeight),
            outerWidth / dpiScale,
            outerHeight / dpiScale);
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

        if (e.Key == Key.Escape && IsAiSurfaceOpen())
        {
            if (_aiBusy)
            {
                CancelAiEdit();
            }
            else
            {
                HideAiPrompt();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
            _aiUndoImage is not null)
        {
            _ = UndoAiEditAsync();
            e.Handled = true;
            return;
        }

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

    // ---- Inline AI editing -------------------------------------------------

    private void OnAiToggle(object sender, RoutedEventArgs e)
    {
        if (_aiBusy)
        {
            return;
        }

        if (AiPromptSurface.Visibility == Visibility.Visible)
        {
            HideAiPrompt();
            return;
        }

        if (!_licenseGate.Allow(GatedFeature.AiActions))
        {
            return;
        }

        _viewModel.IsPenActive = false;
        ShowAiPrompt();
    }

    private void ShowAiPrompt(string? message = null, bool isError = false)
    {
        AiStatusSurface.Visibility = Visibility.Collapsed;
        AiPromptSurface.Visibility = Visibility.Visible;
        AiPromptSurface.Opacity = 0;
        AiPromptTransform.Y = 8;
        SetAiHint(
            message ?? "Describe the result you want · Enter sends this image to your signed-in Codex service",
            isError);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        AiPromptSurface.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        AiPromptTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        Dispatcher.BeginInvoke(() =>
        {
            AiPromptBox.Focus();
            AiPromptBox.SelectAll();
        });
    }

    private void HideAiPrompt()
    {
        AiPromptSurface.Visibility = Visibility.Collapsed;
        AiPromptSurface.BeginAnimation(OpacityProperty, null);
        AiPromptTransform.BeginAnimation(TranslateTransform.YProperty, null);
        Focus();
    }

    private void OnAiClose(object sender, RoutedEventArgs e) => HideAiPrompt();

    private async void OnAiSubmit(object sender, RoutedEventArgs e)
        => await StartAiEditAsync().ConfigureAwait(true);

    private async void OnAiPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        await StartAiEditAsync().ConfigureAwait(true);
    }

    private async Task StartAiEditAsync()
    {
        if (_aiBusy)
        {
            return;
        }

        string instruction = AiPromptBox.Text.Trim();
        if (instruction.Length == 0)
        {
            SetAiHint("Tell Octadock what should change.", isError: true);
            return;
        }

        if (!_imageEditProvider.IsConfigured)
        {
            SetAiHint("Codex CLI is not available. Open Codex once, then restart Octadock.", isError: true);
            return;
        }

        BitmapSource before = BuildComposedImage();
        byte[] sourcePng = _images.EncodePng(before);
        var region = new ImageEditRegion(0, 0, before.PixelWidth, before.PixelHeight);
        var cancellation = new CancellationTokenSource();
        _aiCancellation = cancellation;
        _aiBusy = true;
        AiPromptSurface.Visibility = Visibility.Collapsed;
        AiUndoSurface.Visibility = Visibility.Collapsed;
        AiStatusText.Text = "Reworking the image…";
        AiStatusSurface.Visibility = Visibility.Visible;
        StartAiWorkingAnimation();

        try
        {
            ImageMockupResult result = await _mockups.GenerateAsync(
                sourcePng,
                region,
                instruction,
                contextMargin: 0,
                cancellation.Token).ConfigureAwait(true);
            BitmapSource next = LoadPng(result.CompositePng);
            await PersistAiImageAsync(result.CompositePng, cancellation.Token).ConfigureAwait(true);
            _aiUndoImage = before;
            InkLayer.Strokes.Clear();
            _viewModel.IsPenActive = false;
            await AnimateImageReplacementAsync(next).ConfigureAwait(true);
            AiPromptBox.Clear();
            ShowAiUndo();
        }
        catch (OperationCanceledException)
        {
            ShowAiPrompt("Change cancelled. The image was not touched.");
        }
        catch (Exception ex)
        {
            ShowAiPrompt(FriendlyAiError(ex.Message), isError: true);
        }
        finally
        {
            StopAiWorkingAnimation();
            AiStatusSurface.Visibility = Visibility.Collapsed;
            _aiBusy = false;
            if (ReferenceEquals(_aiCancellation, cancellation))
            {
                _aiCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void StartAiWorkingAnimation()
    {
        AiWorkOverlay.Visibility = Visibility.Visible;
        AiWorkOverlay.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.16, 0.38, TimeSpan.FromMilliseconds(760))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase(),
            });
        AiSweepTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(-220, Math.Max(Width, 640) + 220, TimeSpan.FromMilliseconds(1350))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    private void StopAiWorkingAnimation()
    {
        AiWorkOverlay.BeginAnimation(OpacityProperty, null);
        AiSweepTransform.BeginAnimation(TranslateTransform.XProperty, null);
        AiWorkOverlay.Opacity = 0;
        AiWorkOverlay.Visibility = Visibility.Collapsed;
        AiSweepTransform.X = -220;
    }

    private Task AnimateImageReplacementAsync(BitmapSource next)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AiTransitionLayer.Background = new ImageBrush(next) { Stretch = Stretch.Fill };
        AiTransitionLayer.Opacity = 0;
        AiTransitionLayer.Visibility = Visibility.Visible;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            _viewModel.Image = next;
            AiTransitionLayer.BeginAnimation(OpacityProperty, null);
            AiTransitionLayer.Opacity = 0;
            AiTransitionLayer.Background = null;
            completion.TrySetResult();
        };
        AiTransitionLayer.BeginAnimation(OpacityProperty, animation);
        return completion.Task;
    }

    private async Task PersistAiImageAsync(byte[] png, CancellationToken cancellationToken)
    {
        string relative = $"Pins/{_pinId:D}.png";
        string absolute = _paths.ToAbsolute(relative);
        await App.Services.GetRequiredService<ISafeFileWriter>()
            .WriteAsync(absolute, png, cancellationToken).ConfigureAwait(true);
        _pinImagePath = relative;
        await PersistAsync().ConfigureAwait(true);
    }

    private void ShowAiUndo()
    {
        AiUndoSurface.Visibility = Visibility.Visible;
        AiUndoSurface.Opacity = 0;
        AiUndoSurface.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    private async void OnAiUndo(object sender, RoutedEventArgs e)
        => await UndoAiEditAsync().ConfigureAwait(true);

    private async Task UndoAiEditAsync()
    {
        BitmapSource? previous = _aiUndoImage;
        if (previous is null || _aiBusy)
        {
            return;
        }

        _aiUndoImage = null;
        AiUndoSurface.Visibility = Visibility.Collapsed;
        byte[] png = _images.EncodePng(previous);
        await PersistAiImageAsync(png, CancellationToken.None).ConfigureAwait(true);
        await AnimateImageReplacementAsync(previous).ConfigureAwait(true);
    }

    private void OnAiCancel(object sender, RoutedEventArgs e) => CancelAiEdit();

    private void CancelAiEdit()
    {
        AiStatusText.Text = "Stopping…";
        _aiCancellation?.Cancel();
    }

    private bool IsAiSurfaceOpen()
        => _aiBusy || AiPromptSurface.Visibility == Visibility.Visible;

    private void SetAiHint(string message, bool isError)
    {
        AiHintText.Text = message;
        AiHintText.Foreground = (Brush)FindResource(
            isError ? "Octadock.Brush.Danger" : "Octadock.Brush.TextMuted");
    }

    private static string FriendlyAiError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "The image could not be changed. Try again.";
        string oneLine = string.Join(" ", message.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return oneLine.Length <= 280 ? oneLine : oneLine[..280] + "…";
    }

    private static BitmapSource LoadPng(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async Task<string> SaveComposedTempAsync()
    {
        string temp = Path.Combine(GetSafeTemporaryRoot(), $"pin-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(temp, _images.EncodePng(BuildComposedImage())).ConfigureAwait(true);
        return temp;
    }

    private string GetSafeTemporaryRoot()
    {
        string root = AgentLocalPathGuard.ValidateDestinationDirectory(
            _paths.TempExportsDirectory,
            "Pin temporary storage");
        Directory.CreateDirectory(root);
        return AgentLocalPathGuard.ValidateExistingDirectory(root, "Pin temporary storage");
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
            _aiCancellation?.Cancel();

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
            if (current == Toolbar || current == ResizeBr || current == ResizeBl || current == MoreButton ||
                current == AiPromptSurface || current == AiStatusSurface || current == AiUndoSurface)
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

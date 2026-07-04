using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// <see cref="IRegionSelectionService"/>: presents the selection overlays (one per
/// monitor for correct mixed-DPI coordinates) for area selection and window picking.
/// Each interactive call awaits a single result via a
/// <see cref="TaskCompletionSource{TResult}"/>; whichever overlay completes first
/// tears the whole set down. <see cref="HideAll"/> cancels any active overlay session
/// and closes its windows before the coordinator grabs the screen.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class RegionSelectionService : IRegionSelectionService
{
    private readonly IMonitorService _monitors;
    private readonly ICaptureEngine _captureEngine;
    private readonly IWindowPicker _windowPicker;
    private readonly IImageLoadService _imaging;
    private readonly ISettingsService _settings;
    private readonly ILogger<RegionSelectionService> _logger;

    private OverlaySession? _activeSession;

    /// <summary>Creates the region selection service.</summary>
    public RegionSelectionService(
        IMonitorService monitors,
        ICaptureEngine captureEngine,
        IWindowPicker windowPicker,
        IImageLoadService imaging,
        ISettingsService settings,
        ILogger<RegionSelectionService> logger)
    {
        _monitors = monitors;
        _captureEngine = captureEngine;
        _windowPicker = windowPicker;
        _imaging = imaging;
        _settings = settings;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public async Task<RegionSelection> SelectAreaAsync(CancellationToken cancellationToken = default)
    {
        // Freeze-screen: grab the whole virtual desktop up front so moving content is
        // frozen under the selection and the loupe can read exact pixels.
        BitmapSource? frozen = null;
        if (_settings.Current.Capture.FreezeScreen)
        {
            frozen = await TryCaptureFrozenFrameAsync(cancellationToken).ConfigureAwait(false);
        }

        RegionSelectionResult result = await Dispatcher.InvokeAsync(() =>
            ShowAreaOverlaysAsync(frozen, cancellationToken)).Task.Unwrap().ConfigureAwait(false);

        return result.Confirmed
            ? new RegionSelection(true, result.Region, result.WindowHandleHex)
            : RegionSelection.Cancelled;
    }

    /// <inheritdoc />
    public async Task<RegionSelection> SelectWindowAsync(CancellationToken cancellationToken = default)
    {
        RegionSelectionResult result = await Dispatcher.InvokeAsync(() =>
            ShowWindowPickerAsync(cancellationToken)).Task.Unwrap().ConfigureAwait(false);

        return result.Confirmed
            ? new RegionSelection(true, result.Region, result.WindowHandleHex)
            : RegionSelection.Cancelled;
    }

    /// <inheritdoc />
    public void HideAll()
    {
        void Close()
        {
            CompleteSession(_activeSession, CancelledResult);
        }

        if (Dispatcher.CheckAccess())
        {
            Close();
        }
        else
        {
            Dispatcher.Invoke(Close);
        }
    }

    // ---- Area overlays ------------------------------------------------------

    private Task<RegionSelectionResult> ShowAreaOverlaysAsync(BitmapSource? frozen, CancellationToken cancellationToken)
    {
        OverlaySession session = StartSession();
        IReadOnlyList<DisplayInfo> monitors = _monitors.GetMonitors();
        PixelRect virtualBounds = _monitors.VirtualDesktopBounds;

        if (monitors.Count == 0 || cancellationToken.IsCancellationRequested)
        {
            CompleteSession(session, CancelledResult);
            return session.Completion.Task;
        }

        double primaryScale = monitors.FirstOrDefault(m => m.IsPrimary)?.DpiScale ?? 1.0;
        if (primaryScale <= 0)
        {
            primaryScale = 1.0;
        }

        var overlays = new List<SelectionOverlayWindow>();
        foreach (DisplayInfo monitor in monitors)
        {
            // Slice the frozen virtual-desktop frame for this monitor (origin aware).
            BitmapSource? monitorFrame = frozen is null ? null : CropForMonitor(frozen, virtualBounds, monitor);
            var overlay = new SelectionOverlayWindow(monitor, monitorFrame, primaryScale);
            overlays.Add(overlay);

            overlay.Completed += (_, result) =>
            {
                CompleteSession(session, result);
            };
            overlay.Closed += (_, _) => CompleteSession(session, CancelledResult);
        }

        session.Overlays.AddRange(overlays);

        // Cancellation closes everything and yields a cancelled result.
        if (cancellationToken.CanBeCanceled)
        {
            session.CancellationRegistration = cancellationToken.Register(() => Dispatcher.BeginInvoke(() =>
            {
                CompleteSession(session, CancelledResult);
            }));
        }

        try
        {
            foreach (SelectionOverlayWindow overlay in overlays)
            {
                overlay.Show();
            }

            // Activate the overlay under the active monitor so keyboard focus lands there.
            DisplayInfo active = _monitors.GetActiveMonitor();
            SelectionOverlayWindow? primary = overlays.FirstOrDefault(o => o.Monitor.Id.Value == active.Id.Value)
                ?? overlays.FirstOrDefault();
            primary?.Activate();

            // Multi-monitor diagnostics: log where each overlay actually landed
            // so "overlay missing on one screen" reports can be root-caused
            // from the log alone.
            foreach (SelectionOverlayWindow overlay in overlays)
            {
                _logger.LogInformation(
                    "Area overlay: monitor {Id} bounds={Bounds} dpi={Dpi} primary={Primary} -> physical={Physical} (dips {W:F0}x{H:F0}) visible={Visible}",
                    overlay.Monitor.Id.Value,
                    overlay.Monitor.Bounds,
                    overlay.Monitor.DpiScale,
                    overlay.Monitor.IsPrimary,
                    overlay.PhysicalWindowRect,
                    overlay.ActualWidth,
                    overlay.ActualHeight,
                    overlay.IsVisible);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show the area selection overlay.");
            CompleteSession(session, CancelledResult);
        }

        return session.Completion.Task;
    }

    // ---- Window picker ------------------------------------------------------

    private Task<RegionSelectionResult> ShowWindowPickerAsync(CancellationToken cancellationToken)
    {
        OverlaySession session = StartSession();
        IReadOnlyList<DisplayInfo> monitors = _monitors.GetMonitors();

        if (monitors.Count == 0 || cancellationToken.IsCancellationRequested)
        {
            CompleteSession(session, CancelledResult);
            return session.Completion.Task;
        }

        double primaryScale = monitors.FirstOrDefault(m => m.IsPrimary)?.DpiScale ?? 1.0;
        if (primaryScale <= 0)
        {
            primaryScale = 1.0;
        }

        var overlays = new List<WindowPickerOverlay>();
        foreach (DisplayInfo monitor in monitors)
        {
            var overlay = new WindowPickerOverlay(_windowPicker, monitor, primaryScale, _logger);
            overlays.Add(overlay);

            overlay.Completed += (_, result) =>
            {
                CompleteSession(session, result);
            };
            overlay.Closed += (_, _) => CompleteSession(session, CancelledResult);
        }

        session.Overlays.AddRange(overlays);

        if (cancellationToken.CanBeCanceled)
        {
            session.CancellationRegistration = cancellationToken.Register(() => Dispatcher.BeginInvoke(() =>
            {
                CompleteSession(session, CancelledResult);
            }));
        }

        try
        {
            foreach (WindowPickerOverlay overlay in overlays)
            {
                overlay.Show();
            }

            DisplayInfo active = _monitors.GetActiveMonitor();
            WindowPickerOverlay? primary = overlays.FirstOrDefault(o => o.Monitor.Id.Value == active.Id.Value)
                ?? overlays.FirstOrDefault();
            primary?.Activate();

            foreach (WindowPickerOverlay overlay in overlays)
            {
                _logger.LogInformation(
                    "Window picker overlay: monitor {Id} bounds={Bounds} dpi={Dpi} primary={Primary} -> physical={Physical} (dips {W:F0}x{H:F0}) visible={Visible}",
                    overlay.Monitor.Id.Value,
                    overlay.Monitor.Bounds,
                    overlay.Monitor.DpiScale,
                    overlay.Monitor.IsPrimary,
                    overlay.PhysicalWindowRect,
                    overlay.ActualWidth,
                    overlay.ActualHeight,
                    overlay.IsVisible);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show the window picker overlay.");
            CompleteSession(session, CancelledResult);
        }

        return session.Completion.Task;
    }

    // ---- Helpers ------------------------------------------------------------

    private OverlaySession StartSession()
    {
        CompleteSession(_activeSession, CancelledResult);

        var session = new OverlaySession();
        _activeSession = session;
        return session;
    }

    private void CompleteSession(OverlaySession? session, RegionSelectionResult result)
    {
        if (session is null || !session.TryBeginComplete())
        {
            return;
        }

        if (ReferenceEquals(_activeSession, session))
        {
            _activeSession = null;
        }

        session.CancellationRegistration.Dispose();

        Window[] snapshot = session.Overlays.ToArray();
        session.Overlays.Clear();

        foreach (Window overlay in snapshot)
        {
            try
            {
                overlay.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to close a selection overlay.");
            }
        }

        session.Completion.TrySetResult(result);
    }

    private async Task<BitmapSource?> TryCaptureFrozenFrameAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new FullscreenCaptureRequest { AllMonitors = true, IncludeCursor = false };
            CapturedFrame frame = await _captureEngine.CaptureFullscreenAsync(request, cancellationToken).ConfigureAwait(false);
            BitmapSource source = _imaging.ToBitmapSource(frame);
            source.Freeze();
            return source;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture a freeze-screen frame; continuing without it.");
            return null;
        }
    }

    /// <summary>
    /// Crops a monitor's slice out of the full virtual-desktop frame. The frame's
    /// pixel (0,0) corresponds to the virtual desktop's top-left, which may be at a
    /// negative virtual coordinate, so both offsets are taken relative to that origin.
    /// </summary>
    private static BitmapSource CropForMonitor(BitmapSource frame, PixelRect virtualBounds, DisplayInfo monitor)
    {
        int x = monitor.Bounds.X - virtualBounds.X;
        int y = monitor.Bounds.Y - virtualBounds.Y;
        x = Math.Clamp(x, 0, Math.Max(0, frame.PixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frame.PixelHeight - 1));
        int w = Math.Min(monitor.Bounds.Width, frame.PixelWidth - x);
        int h = Math.Min(monitor.Bounds.Height, frame.PixelHeight - y);
        if (w <= 0 || h <= 0)
        {
            return frame;
        }

        var crop = new CroppedBitmap(frame, new Int32Rect(x, y, w, h));
        crop.Freeze();
        return crop;
    }

    private static RegionSelectionResult CancelledResult => new(false, PixelRect.Empty, null);

    private sealed class OverlaySession
    {
        private bool _completed;

        public TaskCompletionSource<RegionSelectionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<Window> Overlays { get; } = new();

        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public bool TryBeginComplete()
        {
            if (_completed)
            {
                return false;
            }

            _completed = true;
            return true;
        }
    }
}

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Capture;

/// <summary>
/// Still-capture engine. Prefers Windows.Graphics.Capture (occlusion-tolerant,
/// hardware-accelerated) and falls back to a GDI BitBlt path when WGC is
/// unavailable or a grab fails. All capture coordinates are physical pixels on
/// the virtual desktop. Callers exclude Octadock's own windows before capturing;
/// this engine never hides windows itself.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class CaptureEngine : ICaptureEngine, IDisposable
{
    private readonly IMonitorService _monitors;
    private readonly ILogger<CaptureEngine> _logger;
    private readonly Lazy<WgcFrameGrabber> _wgc;

    /// <summary>Creates the capture engine.</summary>
    public CaptureEngine(IMonitorService monitors, ILogger<CaptureEngine> logger, ILoggerFactory loggerFactory)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // Defer D3D device creation until the first capture that needs it.
        _wgc = new Lazy<WgcFrameGrabber>(
            () => new WgcFrameGrabber(loggerFactory.CreateLogger<WgcFrameGrabber>()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public Task<CapturedFrame> CaptureAreaAsync(AreaCaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PixelRect region = request.Region.Normalized();
        if (region.IsEmpty)
        {
            throw new ArgumentException("Capture region is empty.", nameof(request));
        }

        return Task.Run(() => CaptureArea(region, request.IncludeCursor), cancellationToken);
    }

    /// <inheritdoc />
    public Task<CapturedFrame> CaptureWindowAsync(WindowCaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Window.IsValid)
        {
            throw new ArgumentException("Window handle is not valid.", nameof(request));
        }

        return Task.Run(() => CaptureWindow(request), cancellationToken);
    }

    /// <inheritdoc />
    public Task<CapturedFrame> CaptureFullscreenAsync(FullscreenCaptureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => CaptureFullscreen(request), cancellationToken);
    }

    private CapturedFrame CaptureArea(PixelRect region, bool includeCursor)
    {
        // The area path uses GDI directly: it BitBlts the exact region from the
        // virtual-desktop DC (which already spans all monitors), avoiding the need
        // to stitch WGC monitor frames and crop. This is the reliable default.
        DisplayInfo monitor = _monitors.GetMonitorFromPoint(region.Center);
        byte[] pixels = GdiScreenCapture.CaptureRegion(region, includeCursor, out int stride);
        if (pixels.Length == 0)
        {
            throw new InvalidOperationException("GDI area capture returned no pixels.");
        }

        return BuildFrame(
            pixels,
            region.Width,
            region.Height,
            stride,
            monitor.DpiScale,
            monitor.Id,
            WindowUtilities.BuildForegroundSource());
    }

    private CapturedFrame CaptureWindow(WindowCaptureRequest request)
    {
        nint hwnd = (nint)request.Window;
        if (!User32.IsWindow(hwnd))
        {
            throw new InvalidOperationException("The target window no longer exists.");
        }

        PixelRect bounds = WindowUtilities.GetWindowBounds(hwnd, includeShadow: request.IncludeShadow);
        DisplayInfo monitor = bounds.Area > 0
            ? _monitors.GetMonitorFromPoint(bounds.Center)
            : _monitors.GetActiveMonitor();
        CaptureSource source = WindowUtilities.BuildSource(hwnd);

        // Prefer WGC: it captures occluded windows and DirectComposition content.
        WgcGrabResult? grab = TryWgc(() => _wgc.Value.CaptureWindow(hwnd, request.IncludeCursor));
        if (grab is not null)
        {
            return BuildFrame(grab.Bgra, grab.Width, grab.Height, grab.Stride, monitor.DpiScale, monitor.Id, source);
        }

        _logger.LogDebug("WGC window capture unavailable; using GDI PrintWindow fallback.");
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException("Window bounds are empty; cannot capture.");
        }

        byte[] pixels = GdiScreenCapture.CaptureWindow(
            hwnd,
            bounds,
            request.IncludeCursor,
            out int width,
            out int height,
            out int stride);
        if (pixels.Length == 0)
        {
            throw new InvalidOperationException("GDI window capture returned no pixels.");
        }

        return BuildFrame(pixels, width, height, stride, monitor.DpiScale, monitor.Id, source);
    }

    private CapturedFrame CaptureFullscreen(FullscreenCaptureRequest request)
    {
        if (request.AllMonitors)
        {
            PixelRect virtualBounds = _monitors.VirtualDesktopBounds;
            if (virtualBounds.IsEmpty)
            {
                throw new InvalidOperationException("Virtual desktop bounds are empty.");
            }

            DisplayInfo primary = _monitors.GetPrimary();
            byte[] pixels = GdiScreenCapture.CaptureRegion(virtualBounds, request.IncludeCursor, out int stride);
            if (pixels.Length == 0)
            {
                throw new InvalidOperationException("GDI all-monitor capture returned no pixels.");
            }

            return BuildFrame(
                pixels,
                virtualBounds.Width,
                virtualBounds.Height,
                stride,
                primary.DpiScale,
                MonitorId.Unknown,
                CaptureSource.Empty);
        }

        DisplayInfo target = ResolveTargetMonitor(request.Monitor);

        // Prefer WGC for a single monitor (clean, cursor-toggle aware).
        nint hmon = MonitorHandleFor(target);
        if (hmon != nint.Zero)
        {
            WgcGrabResult? grab = TryWgc(() => _wgc.Value.CaptureMonitor(hmon, request.IncludeCursor));
            if (grab is not null)
            {
                return BuildFrame(
                    grab.Bgra,
                    grab.Width,
                    grab.Height,
                    grab.Stride,
                    target.DpiScale,
                    target.Id,
                    CaptureSource.Empty);
            }
        }

        _logger.LogDebug("WGC monitor capture unavailable; using GDI fallback.");
        byte[] gdiPixels = GdiScreenCapture.CaptureRegion(target.Bounds, request.IncludeCursor, out int gdiStride);
        if (gdiPixels.Length == 0)
        {
            throw new InvalidOperationException("GDI monitor capture returned no pixels.");
        }

        return BuildFrame(
            gdiPixels,
            target.Bounds.Width,
            target.Bounds.Height,
            gdiStride,
            target.DpiScale,
            target.Id,
            CaptureSource.Empty);
    }

    private DisplayInfo ResolveTargetMonitor(MonitorId? monitorId)
    {
        if (monitorId is { } id)
        {
            DisplayInfo? found = _monitors.FindById(id);
            if (found is not null)
            {
                return found;
            }
        }

        return _monitors.GetActiveMonitor();
    }

    private static nint MonitorHandleFor(DisplayInfo monitor)
    {
        // Resolve the HMONITOR by hit-testing the monitor's center point.
        var center = new POINT(monitor.Bounds.Center.X, monitor.Bounds.Center.Y);
        return User32.MonitorFromPoint(center, NativeConstants.MONITOR_DEFAULTTONEAREST);
    }

    private WgcGrabResult? TryWgc(Func<WgcGrabResult?> grab)
    {
        try
        {
            if (!_wgc.Value.IsAvailable)
            {
                return null;
            }

            return grab();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WGC capture threw; falling back to GDI.");
            return null;
        }
    }

    private static CapturedFrame BuildFrame(
        byte[] pixels,
        int width,
        int height,
        int stride,
        double dpiScale,
        MonitorId monitorId,
        CaptureSource source)
    {
        return new CapturedFrame(
            pixels,
            width,
            height,
            stride,
            FramePixelFormat.Bgra32,
            dpiScale,
            monitorId,
            source,
            DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_wgc.IsValueCreated)
        {
            _wgc.Value.Dispose();
        }
    }
}

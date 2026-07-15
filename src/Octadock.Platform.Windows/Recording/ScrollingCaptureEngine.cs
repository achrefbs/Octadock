using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Platform.Windows.Capture;

namespace Octadock.Platform.Windows.Recording;

/// <summary>
/// Manual-scroll scrolling-capture engine. The caller scrolls the target between
/// <see cref="CaptureFrameAsync"/> calls; each new viewport frame is grabbed via
/// the GDI region path, its vertical scroll delta relative to the previous frame
/// is detected by naive row-matching, and the newly revealed rows are appended to
/// a growing stitched buffer. Auto-scroll (UI Automation) is not yet supported.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class ScrollingCaptureEngine : IScrollingCaptureEngine, IDisposable
{
    private readonly IMonitorService _monitors;
    private readonly ILogger<ScrollingCaptureEngine> _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sessionOperation = new(1, 1);

    private ScrollingSession? _session;
    private bool _disposed;

    /// <summary>Creates the scrolling-capture engine.</summary>
    public ScrollingCaptureEngine(IMonitorService monitors, ILogger<ScrollingCaptureEngine> logger)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool SupportsAutoScroll => false;

    /// <inheritdoc />
    public Task StartSessionAsync(PixelRect region, ScrollingCaptureOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        PixelRect normalized = region.Normalized();
        if (normalized.IsEmpty)
        {
            throw new ArgumentException("Scrolling-capture region is empty.", nameof(region));
        }

        if (options.AutoScroll)
        {
            throw new NotSupportedException("Automatic scrolling capture is not supported.");
        }

        if (options.Direction != ScrollDirection.Vertical)
        {
            throw new NotSupportedException("Only vertical scrolling capture is supported.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_session is not null)
            {
                throw new InvalidOperationException("A scrolling-capture session is already active.");
            }

            DisplayInfo monitor = _monitors.GetMonitorFromPoint(normalized.Center);
            _session = new ScrollingSession(normalized, options, monitor);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<int> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForSessionOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ScrollingSession session;
            lock (_gate)
            {
                session = _session ?? throw new InvalidOperationException("No scrolling-capture session is active.");
            }

            (byte[] Pixels, int Stride) captured = await Task.Run(() =>
            {
                byte[] pixels = GdiScreenCapture.CaptureRegion(session.Region, includeCursor: false, out int stride);
                return (pixels, stride);
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (!ReferenceEquals(_session, session))
                {
                    throw new InvalidOperationException("The scrolling-capture session ended before the frame was appended.");
                }

                if (captured.Pixels.Length == 0)
                {
                    _logger.LogDebug("Scrolling-capture frame grab returned no pixels.");
                    return session.StitchedHeight;
                }

                var frame = new ScrollFrame(captured.Pixels, session.Region.Width, session.Region.Height, captured.Stride);
                session.AppendFrame(frame);
                return session.StitchedHeight;
            }
        }
        finally
        {
            _sessionOperation.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ScrollingCaptureResult> FinishAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await WaitForSessionOperationAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                ScrollingSession session = _session ?? throw new InvalidOperationException("No scrolling-capture session is active.");

                (byte[] pixels, int width, int height, int stride) = session.BuildStitched();
                _session = null;

                string title = session.Truncated ? "Scrolling capture (truncated)" : "Scrolling capture";
                var source = new CaptureSource("Octadock", title, null);
                var frame = new CapturedFrame(
                    pixels,
                    width,
                    height,
                    stride,
                    FramePixelFormat.Bgra32,
                    session.Monitor.DpiScale,
                    session.Monitor.Id,
                    source,
                    DateTimeOffset.UtcNow);

                return new ScrollingCaptureResult(frame, session.Truncated, height, session.MaxStitchedEdge);
            }
        }
        finally
        {
            _sessionOperation.Release();
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_gate)
        {
            _session = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // A frame grab can still be finishing while the host is shutting down.
        // Wait for that operation before releasing its synchronization primitive.
        _sessionOperation.Wait();
        try
        {
            lock (_gate)
            {
                _session = null;
            }
        }
        finally
        {
            _sessionOperation.Release();
            _sessionOperation.Dispose();
        }
    }

    private Task WaitForSessionOperationAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sessionOperation.WaitAsync(cancellationToken);
        }
    }
}

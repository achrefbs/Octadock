using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Extensions.Logging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Capture;

/// <summary>Raw result of a Windows.Graphics.Capture single-frame grab.</summary>
internal sealed record WgcGrabResult(byte[] Bgra, int Width, int Height, int Stride);

/// <summary>
/// Performs single-frame captures with Windows.Graphics.Capture. Owns a D3D11
/// device, creates a free-threaded frame pool for a capture item, waits for one
/// frame, copies it into a CPU-readable staging texture and returns straight
/// BGRA bytes (top-down). Reused across captures; disposed with the engine.
/// </summary>
/// <remarks>
/// Hardware validation note: the D3D11 staging-texture readback and WinRT
/// interop bridge depend on the active GPU/driver and fall back to GDI when
/// unavailable.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WgcFrameGrabber : IDisposable
{
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private nint _d3dDevicePtr;
    private nint _immediateContextPtr;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winrtDevice;
    private bool _initialized;
    private bool _disposed;

    /// <summary>True when WGC is supported by the OS and a D3D device is available.</summary>
    public bool IsAvailable { get; private set; }

    public WgcFrameGrabber(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        TryInitialize();
    }

    private void TryInitialize()
    {
        if (!OsVersion.SupportsCaptureExclusion)
        {
            _logger.LogDebug("WGC unavailable: OS build {Build} < 19041.", OsVersion.BuildNumber);
            return;
        }

        try
        {
            if (!GraphicsCaptureSession.IsSupported())
            {
                _logger.LogDebug("GraphicsCaptureSession.IsSupported() returned false.");
                return;
            }

            CreateDevice();
            IsAvailable = _initialized;
        }
        catch (Exception ex)
        {
            ReleaseDeviceResources();
            _logger.LogWarning(ex, "Failed to initialize the WGC frame grabber; GDI fallback will be used.");
            IsAvailable = false;
        }
    }

    private void CreateDevice()
    {
        uint flags = D3D11.D3D11_CREATE_DEVICE_BGRA_SUPPORT;
        int hr = D3D11.D3D11CreateDevice(
            nint.Zero,
            D3D_DRIVER_TYPE.Hardware,
            nint.Zero,
            flags,
            nint.Zero,
            0,
            D3D11.D3D11_SDK_VERSION,
            out _d3dDevicePtr,
            out _,
            out _immediateContextPtr);

        if (hr != 0 || _d3dDevicePtr == nint.Zero)
        {
            // Retry with WARP (software) so headless/VM environments still work.
            hr = D3D11.D3D11CreateDevice(
                nint.Zero,
                D3D_DRIVER_TYPE.Warp,
                nint.Zero,
                flags,
                nint.Zero,
                0,
                D3D11.D3D11_SDK_VERSION,
                out _d3dDevicePtr,
                out _,
                out _immediateContextPtr);
        }

        if (hr != 0 || _d3dDevicePtr == nint.Zero)
        {
            _logger.LogDebug("D3D11CreateDevice failed (hr=0x{Hr:X8}).", hr);
            return;
        }

        _device = (ID3D11Device)Marshal.GetObjectForIUnknown(_d3dDevicePtr);
        _context = (ID3D11DeviceContext)Marshal.GetObjectForIUnknown(_immediateContextPtr);
        _winrtDevice = WinRtInterop.CreateDirect3DDevice(_d3dDevicePtr);
        _initialized = true;
    }

    /// <summary>Captures a single frame from the given window handle.</summary>
    public WgcGrabResult? CaptureWindow(nint hwnd, bool includeCursor)
    {
        if (!IsAvailable)
        {
            return null;
        }

        GraphicsCaptureItem item = WinRtInterop.CreateItemForWindow(hwnd);
        return CaptureItem(item, includeCursor);
    }

    /// <summary>Captures a single frame from the given monitor handle.</summary>
    public WgcGrabResult? CaptureMonitor(nint hmon, bool includeCursor)
    {
        if (!IsAvailable)
        {
            return null;
        }

        GraphicsCaptureItem item = WinRtInterop.CreateItemForMonitor(hmon);
        return CaptureItem(item, includeCursor);
    }

    private WgcGrabResult? CaptureItem(GraphicsCaptureItem item, bool includeCursor)
    {
        lock (_gate)
        {
            if (_disposed || _winrtDevice is null)
            {
                return null;
            }

            SizeInt32 size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }

            Direct3D11CaptureFramePool? pool = null;
            GraphicsCaptureSession? session = null;
            using var frameReady = new ManualResetEventSlim(false);
            Direct3D11CaptureFrame? captured = null;
            int capturedState = 0;
            int acceptingFrames = 1;
            object frameLock = new();

            try
            {
                pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    numberOfBuffers: 2,
                    size);

                pool.FrameArrived += (sender, _) =>
                {
                    // Grab the first available frame, then signal.
                    if (Volatile.Read(ref acceptingFrames) == 0 ||
                        Interlocked.CompareExchange(ref capturedState, 1, 0) != 0)
                    {
                        return;
                    }

                    try
                    {
                        Direct3D11CaptureFrame? nextFrame = sender.TryGetNextFrame();
                        if (nextFrame is null)
                        {
                            Interlocked.Exchange(ref capturedState, 0);
                            return;
                        }

                        lock (frameLock)
                        {
                            if (Volatile.Read(ref acceptingFrames) == 0)
                            {
                                nextFrame.Dispose();
                                Interlocked.Exchange(ref capturedState, 0);
                                return;
                            }

                            captured = nextFrame;
                        }

                        frameReady.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        Interlocked.Exchange(ref capturedState, 0);
                    }
                    catch (Exception)
                    {
                        Interlocked.Exchange(ref capturedState, 0);
                    }
                };

                session = pool.CreateCaptureSession(item);
                TrySetCursorCapture(session, includeCursor);
                session.StartCapture();

                if (!frameReady.Wait(TimeSpan.FromMilliseconds(2000)) || captured is null)
                {
                    _logger.LogDebug("WGC frame did not arrive within the timeout.");
                    return null;
                }

                return ReadbackFrame(captured, size);
            }
            finally
            {
                Volatile.Write(ref acceptingFrames, 0);
                session?.Dispose();
                lock (frameLock)
                {
                    captured?.Dispose();
                    captured = null;
                }

                pool?.Dispose();
            }
        }
    }

    private static void TrySetCursorCapture(GraphicsCaptureSession session, bool includeCursor)
    {
        try
        {
            // IsCursorCaptureEnabled is available on Windows 10 2004+.
            session.IsCursorCaptureEnabled = includeCursor;
        }
        catch (Exception)
        {
            // Older builds may not expose the property; ignore.
        }
    }

    private WgcGrabResult? ReadbackFrame(Direct3D11CaptureFrame frame, SizeInt32 size)
    {
        if (_context is null)
        {
            return null;
        }

        nint texturePtr = nint.Zero;
        nint stagingPtr = nint.Zero;
        ID3D11Texture2D? texture = null;
        try
        {
            texturePtr = WinRtInterop.GetDxgiTexture(frame.Surface);
            if (texturePtr == nint.Zero)
            {
                return null;
            }

            texture = (ID3D11Texture2D)Marshal.GetObjectForIUnknown(texturePtr);
            texture.GetDesc(out D3D11_TEXTURE2D_DESC sourceDesc);
            (int width, int height) = ResolveReadbackSize(frame.ContentSize, sourceDesc, size);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = sourceDesc.Width,
                Height = sourceDesc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE.Staging,
                BindFlags = 0,
                CPUAccessFlags = (uint)D3D11_CPU_ACCESS_FLAG.Read,
                MiscFlags = 0,
            };

            int hr = _device!.CreateTexture2D(ref desc, nint.Zero, out stagingPtr);
            if (hr != 0 || stagingPtr == nint.Zero)
            {
                _logger.LogDebug("CreateTexture2D (staging) failed (hr=0x{Hr:X8}).", hr);
                return null;
            }

            // Copy GPU texture -> CPU-readable staging texture.
            _context.CopyResource(stagingPtr, texturePtr);

            hr = _context.Map(stagingPtr, 0, D3D11_MAP.Read, 0, out D3D11_MAPPED_SUBRESOURCE mapped);
            if (hr != 0 || mapped.PData == nint.Zero)
            {
                _logger.LogDebug("Map (staging) failed (hr=0x{Hr:X8}).", hr);
                return null;
            }

            try
            {
                int dstStride = width * 4;
                byte[] buffer = new byte[dstStride * height];
                int srcPitch = (int)mapped.RowPitch;

                // Copy row by row to strip any GPU row padding.
                for (int y = 0; y < height; y++)
                {
                    nint srcRow = mapped.PData + (y * srcPitch);
                    Marshal.Copy(srcRow, buffer, y * dstStride, dstStride);
                }

                return new WgcGrabResult(buffer, width, height, dstStride);
            }
            finally
            {
                _context.Unmap(stagingPtr, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WGC frame readback failed.");
            return null;
        }
        finally
        {
            if (texture is not null)
            {
                Marshal.ReleaseComObject(texture);
            }

            if (stagingPtr != nint.Zero)
            {
                Marshal.Release(stagingPtr);
            }

            if (texturePtr != nint.Zero)
            {
                Marshal.Release(texturePtr);
            }
        }
    }

    internal static (int Width, int Height) ResolveReadbackSize(
        SizeInt32 contentSize,
        D3D11_TEXTURE2D_DESC surfaceDescription,
        SizeInt32 fallbackSize)
    {
        int contentWidth = contentSize.Width > 0 ? contentSize.Width : fallbackSize.Width;
        int contentHeight = contentSize.Height > 0 ? contentSize.Height : fallbackSize.Height;
        int surfaceWidth = surfaceDescription.Width > int.MaxValue ? int.MaxValue : (int)surfaceDescription.Width;
        int surfaceHeight = surfaceDescription.Height > int.MaxValue ? int.MaxValue : (int)surfaceDescription.Height;

        int width = Math.Min(contentWidth, surfaceWidth);
        int height = Math.Min(contentHeight, surfaceHeight);
        return (Math.Max(0, width), Math.Max(0, height));
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

            ReleaseDeviceResources();
        }
    }

    private void ReleaseDeviceResources()
    {
        try
        {
            (_winrtDevice as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose the WinRT D3D device.");
        }

        _winrtDevice = null;

        // Release the RCWs and raw COM references we materialized.
        if (_context is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_context);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to release the D3D11 device context RCW.");
            }

            _context = null;
        }

        if (_device is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_device);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to release the D3D11 device RCW.");
            }

            _device = null;
        }

        if (_immediateContextPtr != nint.Zero)
        {
            Marshal.Release(_immediateContextPtr);
            _immediateContextPtr = nint.Zero;
        }

        if (_d3dDevicePtr != nint.Zero)
        {
            Marshal.Release(_d3dDevicePtr);
            _d3dDevicePtr = nint.Zero;
        }

        _initialized = false;
    }
}

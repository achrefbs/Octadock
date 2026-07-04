using Octadock.Core.Geometry;
using Octadock.Core.Models;

namespace Octadock.Core.Capture;

/// <summary>
/// The immutable result of a still capture: raw pixels plus the metadata needed
/// to persist, encode and index the image. The capture engine produces this;
/// the capture pipeline encodes it to disk and builds a <see cref="CaptureRecord"/>.
/// </summary>
public sealed class CapturedFrame
{
    public CapturedFrame(
        ReadOnlyMemory<byte> pixels,
        int width,
        int height,
        int stride,
        FramePixelFormat format,
        double dpiScale,
        MonitorId monitorId,
        CaptureSource source,
        DateTimeOffset capturedAt)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");
        }

        if (stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride is smaller than a 32bpp row.");
        }

        long required = (long)stride * height;
        if (pixels.Length < required)
        {
            throw new ArgumentException($"Pixel buffer ({pixels.Length}) is smaller than stride*height ({required}).", nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        MonitorId = monitorId;
        Source = source;
        CapturedAt = capturedAt;
    }

    /// <summary>Raw pixel bytes, top-down, <see cref="Stride"/> bytes per row.</summary>
    public ReadOnlyMemory<byte> Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row (may exceed <c>Width*4</c> for alignment).</summary>
    public int Stride { get; }

    public FramePixelFormat Format { get; }

    public double DpiScale { get; }

    public MonitorId MonitorId { get; }

    public CaptureSource Source { get; }

    public DateTimeOffset CapturedAt { get; }

    public PixelSize Size => new(Width, Height);
}

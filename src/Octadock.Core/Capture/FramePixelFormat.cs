namespace Octadock.Core.Capture;

/// <summary>
/// Pixel layout of a <see cref="CapturedFrame"/>. Octadock's capture engine
/// standardizes on 32-bit BGRA (the native output of Windows.Graphics.Capture
/// and DXGI) so downstream encoders have a single format to handle.
/// </summary>
public enum FramePixelFormat
{
    /// <summary>8 bits per channel, byte order B, G, R, A. Straight (non-premultiplied) alpha.</summary>
    Bgra32 = 0,

    /// <summary>8 bits per channel, byte order B, G, R, A with premultiplied alpha.</summary>
    Pbgra32 = 1,
}

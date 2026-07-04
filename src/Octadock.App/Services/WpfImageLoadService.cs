using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Octadock.App.Imaging;
using Octadock.Core.Capture;

namespace Octadock.App.Services;

/// <summary>
/// WPF-backed <see cref="IImageLoadService"/>. Delegates to <see cref="FrameImaging"/>
/// so the shelf/editor/pins use exactly the same pixel handling as the encoder and
/// thumbnail generator.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WpfImageLoadService : IImageLoadService
{
    /// <inheritdoc />
    public BitmapSource LoadFromFile(string absolutePath) => FrameImaging.LoadFromFile(absolutePath);

    /// <inheritdoc />
    public BitmapSource ToBitmapSource(CapturedFrame frame) => FrameImaging.ToBitmapSource(frame);

    /// <inheritdoc />
    public byte[] EncodePng(BitmapSource image) => FrameImaging.EncodePng(image);
}

using Octadock.Core.Capture;
using Octadock.Core.Imaging;

namespace Octadock.Core.Abstractions;

/// <summary>
/// Encodes captured frames to raster byte streams and back. Implemented over WIC
/// / SkiaSharp in the platform/app layer.
/// </summary>
public interface IImageEncoder
{
    /// <summary>Encodes a frame to the requested format.</summary>
    EncodedImage Encode(CapturedFrame frame, EncodeOptions options);

    /// <summary>Encodes a frame and writes it to <paramref name="path"/>, creating directories as needed.</summary>
    Task EncodeToFileAsync(CapturedFrame frame, string path, EncodeOptions options, CancellationToken cancellationToken = default);
}

/// <summary>Generates and caches thumbnails for the shelf and history views.</summary>
public interface IThumbnailGenerator
{
    /// <summary>Longest-edge size, in pixels, of generated thumbnails.</summary>
    int MaxEdge { get; }

    /// <summary>Creates a thumbnail from a captured frame.</summary>
    EncodedImage Generate(CapturedFrame frame);

    /// <summary>Creates a thumbnail from an image file and writes it to <paramref name="thumbnailPath"/>.</summary>
    Task GenerateToFileAsync(string sourceImagePath, string thumbnailPath, CancellationToken cancellationToken = default);
}

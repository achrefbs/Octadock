using System.IO;

namespace Octadock.App.Preview;

internal static class ImageFileSupport
{
    private static readonly HashSet<string> RasterExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".avif",
            ".png",
            ".jpg",
            ".jpeg",
            ".jpe",
            ".jfif",
            ".gif",
            ".bmp",
            ".dib",
            ".webp",
            ".ico",
            ".tif",
            ".tiff",
            ".heic",
            ".heif",
            ".wdp",
            ".jxr",
        };

    public static bool IsSupportedRasterExtension(string extension)
        => !string.IsNullOrWhiteSpace(extension) &&
           RasterExtensions.Contains(extension.StartsWith('.') ? extension : "." + extension);

    public static bool IsSupportedRasterPath(string path)
        => IsSupportedRasterExtension(Path.GetExtension(path));
}

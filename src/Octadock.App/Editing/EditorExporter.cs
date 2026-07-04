using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Octadock.Core.Annotations;

namespace Octadock.App.Editing;

/// <summary>
/// Flattens an <see cref="AnnotationDocument"/> plus its base raster into a single
/// bitmap by composing the base image and the vector overlay through the same
/// <see cref="AnnotationRenderer"/> the live canvas uses. Because both paths share
/// the renderer, the exported pixels match the on-screen canvas exactly (at 1:1
/// image resolution). Callers encode the result to PNG/JPEG.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class EditorExporter
{
    /// <summary>
    /// Composes the document over its base image at native image resolution and
    /// returns a frozen <see cref="BitmapSource"/>.
    /// </summary>
    public static BitmapSource Flatten(AnnotationDocument document, BitmapSource baseImage)
    {
        int width = Math.Max(1, document.CanvasSize.Width);
        int height = Math.Max(1, document.CanvasSize.Height);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // Base raster clamped to the (possibly cropped) canvas.
            dc.DrawImage(baseImage, new Rect(0, 0, width, height));
            AnnotationRenderer.Render(dc, document, baseImage);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>Flattens then encodes to PNG bytes.</summary>
    public static byte[] FlattenToPng(AnnotationDocument document, BitmapSource baseImage)
    {
        BitmapSource composed = Flatten(document, baseImage);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

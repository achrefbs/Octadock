namespace Octadock.Core.Imaging;

/// <summary>Raster formats Octadock can encode to. PNG/JPEG ship in P0; the rest are modeled for later.</summary>
public enum ExportImageFormat
{
    Png = 0,
    Jpeg,
    Webp,
    Bmp,
}

/// <summary>An encoded image ready to write to disk or the clipboard.</summary>
public sealed record EncodedImage(ReadOnlyMemory<byte> Bytes, ExportImageFormat Format)
{
    /// <summary>The conventional file extension (including the dot).</summary>
    public string Extension => Format switch
    {
        ExportImageFormat.Png => ".png",
        ExportImageFormat.Jpeg => ".jpg",
        ExportImageFormat.Webp => ".webp",
        ExportImageFormat.Bmp => ".bmp",
        _ => ".png",
    };
}

/// <summary>Options controlling raster encoding.</summary>
public sealed record EncodeOptions
{
    public ExportImageFormat Format { get; init; } = ExportImageFormat.Png;

    /// <summary>Quality (1-100) for lossy formats.</summary>
    public int Quality { get; init; } = 90;
}

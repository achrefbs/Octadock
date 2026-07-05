using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;

namespace Octadock.App.History;

/// <summary>
/// A single history row: wraps a <see cref="CaptureRecord"/> for display, loads its
/// thumbnail lazily off the UI thread, and surfaces the metadata the list shows
/// (type, timestamp, dimensions, source, and whether it is annotated / deleted).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class CaptureItemViewModel : ObservableObject
{
    private readonly IImageLoadService _images;
    private readonly IStoragePaths _paths;
    private bool _thumbnailRequested;

    [ObservableProperty]
    private BitmapSource? _thumbnail;

    /// <summary>Wraps a capture record for the history list.</summary>
    public CaptureItemViewModel(CaptureRecord record, IImageLoadService images, IStoragePaths paths)
    {
        Record = record;
        _images = images;
        _paths = paths;
    }

    /// <summary>The underlying record.</summary>
    public CaptureRecord Record { get; }

    public Guid Id => Record.Id;

    public bool IsDeleted => Record.IsDeleted;

    public bool IsAnnotated => !string.IsNullOrEmpty(Record.ProjectPath);

    /// <summary>True for OCR-source rows, which offer a copy-extracted-text action.</summary>
    public bool IsOcr => Record.Type == CaptureType.OcrSource;

    /// <summary>A short type label ("Area", "Window", "Recording", …).</summary>
    public string TypeLabel => Record.Type switch
    {
        CaptureType.Area => "Area",
        CaptureType.Window => "Window",
        CaptureType.Fullscreen => "Fullscreen",
        CaptureType.Scrolling => "Scrolling",
        CaptureType.Recording => "Recording",
        CaptureType.OcrSource => "OCR",
        CaptureType.External => "File",
        _ => Record.Type.ToString(),
    };

    /// <summary>The source process/window, or a neutral placeholder.</summary>
    public string SourceLabel
    {
        get
        {
            string? proc = Record.Source.ProcessName;
            string? title = Record.Source.WindowTitle;
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title!;
            }

            return string.IsNullOrWhiteSpace(proc) ? "Unknown source" : proc!;
        }
    }

    public string DimensionsLabel => Record.PixelWidth > 0
        ? $"{Record.PixelWidth} × {Record.PixelHeight}"
        : string.Empty;

    public string TimestampLabel => Record.CreatedAt.LocalDateTime.ToString("g");

    /// <summary>Loads the thumbnail once, off the UI thread. Safe to call repeatedly.</summary>
    public async Task EnsureThumbnailAsync(CancellationToken cancellationToken = default)
    {
        if (_thumbnailRequested)
        {
            return;
        }

        _thumbnailRequested = true;

        string? relative = Record.ThumbnailPath ?? Record.OriginalPath;
        if (string.IsNullOrEmpty(relative))
        {
            return;
        }

        string absolute = _paths.ToAbsolute(relative);
        try
        {
            BitmapSource? loaded = await Task.Run(
                () => File.Exists(absolute) ? _images.LoadFromFile(absolute) : null,
                cancellationToken).ConfigureAwait(true);
            Thumbnail = loaded;
        }
        catch (Exception)
        {
            // A missing/corrupt thumbnail just shows the placeholder.
        }
    }
}

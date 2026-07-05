using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;

namespace Octadock.App.Clipboard;

/// <summary>
/// One clipboard clip row: text snippet or image thumbnail plus provenance,
/// relative timestamp, seen count, and favorite state.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ClipItemViewModel : ObservableObject
{
    private const int SnippetLength = 320;

    private readonly IStoragePaths _paths;

    [ObservableProperty]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Creates the row view model.</summary>
    public ClipItemViewModel(ClipboardClipRecord record, IStoragePaths paths, DateTimeOffset now)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _isFavorite = record.IsFavorite;
        TimeLabel = BuildTimeLabel(record, now);
    }

    /// <summary>The underlying stored clip.</summary>
    public ClipboardClipRecord Record { get; private set; }

    /// <summary>True for text clips.</summary>
    public bool IsText => Record.Kind == ClipboardClipKind.Text;

    /// <summary>True for image clips.</summary>
    public bool IsImage => Record.Kind == ClipboardClipKind.Image;

    /// <summary>A short single-paragraph preview of the text payload.</summary>
    public string TextSnippet
    {
        get
        {
            string text = Record.Text ?? string.Empty;
            text = text.Trim();
            if (text.Length > SnippetLength)
            {
                text = text[..SnippetLength] + "…";
            }

            return text;
        }
    }

    /// <summary>"app.exe · Window title", or a fallback when unknown.</summary>
    public string SourceLabel
    {
        get
        {
            string process = Record.SourceProcess ?? string.Empty;
            string window = Record.SourceWindow ?? string.Empty;
            return (process.Length, window.Length) switch
            {
                (0, 0) => "Unknown source",
                (_, 0) => process,
                (0, _) => window,
                _ => $"{process} · {window}",
            };
        }
    }

    /// <summary>Relative time plus seen count, e.g. "5 min ago · seen 3×".</summary>
    public string TimeLabel { get; }

    /// <summary>Byte size for image clips ("1.4 MB"), empty for text.</summary>
    public string SizeLabel
    {
        get
        {
            if (!IsImage || Record.SizeBytes is not long bytes || bytes <= 0)
            {
                return string.Empty;
            }

            return bytes switch
            {
                < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
                < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
                _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
            };
        }
    }

    /// <summary>Applies a store-side update (favorite toggle, seen bump).</summary>
    public void Apply(ClipboardClipRecord updated)
    {
        Record = updated;
        IsFavorite = updated.IsFavorite;
        OnPropertyChanged(nameof(TextSnippet));
        OnPropertyChanged(nameof(SourceLabel));
    }

    /// <summary>Loads the image thumbnail off the UI thread; safe to call once.</summary>
    public void LoadThumbnail()
    {
        if (!IsImage || Thumbnail is not null)
        {
            return;
        }

        string? relative = Record.ThumbnailPath ?? Record.ImagePath;
        if (string.IsNullOrWhiteSpace(relative))
        {
            return;
        }

        try
        {
            string absolute = _paths.ToAbsolute(relative);
            if (!File.Exists(absolute))
            {
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(absolute, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            Thumbnail = image;
        }
        catch (Exception)
        {
            // A missing/corrupt thumbnail only affects this row's preview.
        }
    }

    private static string BuildTimeLabel(ClipboardClipRecord record, DateTimeOffset now)
    {
        TimeSpan age = now - record.LastSeenAt;
        string when = age switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} min ago"),
            { TotalHours: < 24 } => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours} h ago"),
            { TotalDays: < 7 } => string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays} d ago"),
            _ => record.LastSeenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture),
        };

        return record.SeenCount > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{when} · seen {record.SeenCount}×")
            : when;
    }
}

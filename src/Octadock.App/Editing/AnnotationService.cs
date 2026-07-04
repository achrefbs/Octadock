using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Annotations;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Models;

namespace Octadock.App.Editing;

/// <summary>
/// <see cref="IAnnotationService"/>. Launches the annotation editor from a capture
/// record, an image file, or the clipboard, loading an existing <c>.octadock</c>
/// project when the capture has one. Editor windows are created and shown on the UI
/// dispatcher; the heavy image/DB work happens off the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AnnotationService : IAnnotationService
{
    private readonly IImageLoadService _images;
    private readonly IProjectSerializer _projects;
    private readonly IStoragePaths _paths;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<AnnotationService> _logger;

    /// <summary>Creates the annotation service.</summary>
    public AnnotationService(
        IImageLoadService images,
        IProjectSerializer projects,
        IStoragePaths paths,
        IClipboardService clipboard,
        ILogger<AnnotationService> logger)
    {
        _images = images;
        _projects = projects;
        _paths = paths;
        _clipboard = clipboard;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public async Task OpenAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Prefer an existing editable project; otherwise open the original raster.
        if (!string.IsNullOrEmpty(record.ProjectPath))
        {
            string projectPath = _paths.ToAbsolute(record.ProjectPath);
            if (File.Exists(projectPath))
            {
                await OpenProjectAsync(projectPath, record.Id, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        string imagePath = _paths.ToAbsolute(record.OriginalPath);
        await OpenImagePathAsync(imagePath, record.Id, TitleFor(record), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        if (filePath.EndsWith(".octadock", StringComparison.OrdinalIgnoreCase))
        {
            await OpenProjectAsync(filePath, sourceCaptureId: null, cancellationToken).ConfigureAwait(false);
            return;
        }

        await OpenImagePathAsync(filePath, sourceCaptureId: null, Path.GetFileName(filePath), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OpenFromClipboardAsync(CancellationToken cancellationToken = default)
    {
        EncodedImage? clip = _clipboard.TryGetImage();
        if (clip is null)
        {
            _logger.LogInformation("Annotate-from-clipboard requested but no image is on the clipboard.");
            return;
        }

        BitmapSource bitmap = await Task.Run(() => Decode(clip.Bytes), cancellationToken).ConfigureAwait(false);
        var document = new AnnotationDocument(new PixelSize(bitmap.PixelWidth, bitmap.PixelHeight));
        await ShowEditorAsync(document, bitmap, projectPath: null, sourceCaptureId: null, "Clipboard image")
            .ConfigureAwait(false);
    }

    private async Task OpenImagePathAsync(string imagePath, Guid? sourceCaptureId, string title, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            _logger.LogWarning("Cannot open editor: image not found at {Path}.", imagePath);
            return;
        }

        BitmapSource bitmap = await Task.Run(() => _images.LoadFromFile(imagePath), cancellationToken).ConfigureAwait(false);
        var document = new AnnotationDocument(new PixelSize(bitmap.PixelWidth, bitmap.PixelHeight), sourceCaptureId);
        await ShowEditorAsync(document, bitmap, projectPath: null, sourceCaptureId, title).ConfigureAwait(false);
    }

    private async Task OpenProjectAsync(string projectPath, Guid? sourceCaptureId, CancellationToken cancellationToken)
    {
        try
        {
            ProjectLoadResult loaded = await _projects.LoadAsync(projectPath, cancellationToken).ConfigureAwait(false);
            BitmapSource bitmap = await Task.Run(() => Decode(loaded.BaseImagePng), cancellationToken).ConfigureAwait(false);

            await ShowEditorAsync(
                loaded.Document,
                bitmap,
                projectPath,
                sourceCaptureId ?? loaded.Document.SourceCaptureId,
                Path.GetFileNameWithoutExtension(projectPath)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open annotation project {Path}.", projectPath);
        }
    }

    private async Task ShowEditorAsync(
        AnnotationDocument document,
        BitmapSource baseImage,
        string? projectPath,
        Guid? sourceCaptureId,
        string title)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            var window = ActivatorUtilities.CreateInstance<AnnotationEditorWindow>(App.Services);
            window.LoadDocument(document, baseImage, projectPath, sourceCaptureId, title);
            window.Show();
            window.Activate();
        });
    }

    private static BitmapSource Decode(ReadOnlyMemory<byte> imageBytes)
    {
        using var stream = new MemoryStream(imageBytes.ToArray());
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static string TitleFor(CaptureRecord record)
    {
        string? proc = record.Source.ProcessName;
        return string.IsNullOrWhiteSpace(proc)
            ? $"Capture {record.CreatedAt.LocalDateTime:g}"
            : $"{proc} — {record.CreatedAt.LocalDateTime:g}";
    }
}

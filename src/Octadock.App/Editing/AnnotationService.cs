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
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Editing;

/// <summary>
/// <see cref="IAnnotationService"/>. Launches the annotation editor from a registered
/// capture record or a capture-derived <c>.octadock</c> project, loading an existing
/// project when the capture has one. Arbitrary files and clipboard images are not
/// annotation entry points: the only raster sources are captures recorded in History.
/// Editor windows are created and shown on the UI dispatcher; the heavy image/DB work
/// happens off the UI thread. Missing or corrupt sources fail visibly (notification)
/// and never record an <see cref="ActionType.Annotated"/> action; a successfully
/// opened editor records exactly one.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AnnotationService : IAnnotationService
{
    /// <summary>How far back the registered-capture lookup scans for <see cref="OpenFileAsync"/>.</summary>
    private const int RegisteredCaptureScanLimit = 512;

    private readonly IImageLoadService _images;
    private readonly IProjectSerializer _projects;
    private readonly IStoragePaths _paths;
    private readonly ILicenseGate _licenseGate;
    private readonly ICaptureRepository _captures;
    private readonly IActionRepository _actions;
    private readonly INotificationService _notifications;
    private readonly ILogger<AnnotationService> _logger;

    /// <summary>Creates the annotation service.</summary>
    public AnnotationService(
        IImageLoadService images,
        IProjectSerializer projects,
        IStoragePaths paths,
        ILicenseGate licenseGate,
        ICaptureRepository captures,
        IActionRepository actions,
        INotificationService notifications,
        ILogger<AnnotationService> logger)
    {
        _images = images;
        _projects = projects;
        _paths = paths;
        _licenseGate = licenseGate;
        _captures = captures;
        _actions = actions;
        _notifications = notifications;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <summary>
    /// Test seam for the WPF window launch. When set (unit tests only), the
    /// resolved document/bitmap are handed to this delegate instead of creating
    /// an <see cref="AnnotationEditorWindow"/> on the dispatcher.
    /// </summary>
    internal Func<AnnotationDocument, BitmapSource, string?, Guid?, string, Task>? EditorLauncher { get; set; }

    /// <inheritdoc />
    public async Task<bool> OpenAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Prefer an existing editable project; otherwise open the original raster.
        if (!string.IsNullOrEmpty(record.ProjectPath))
        {
            string projectPath = _paths.ToAbsolute(record.ProjectPath);
            if (File.Exists(projectPath))
            {
                bool opened = await OpenProjectAsync(projectPath, record.Id, cancellationToken).ConfigureAwait(false);
                if (opened)
                {
                    await RecordAnnotatedAsync(record.Id, cancellationToken).ConfigureAwait(false);
                }

                return opened;
            }
        }

        string imagePath = _paths.ToAbsolute(record.OriginalPath);
        bool openedImage = await OpenImagePathAsync(imagePath, record.Id, TitleFor(record), cancellationToken)
            .ConfigureAwait(false);
        if (openedImage)
        {
            await RecordAnnotatedAsync(record.Id, cancellationToken).ConfigureAwait(false);
        }

        return openedImage;
    }

    /// <inheritdoc />
    public async Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        if (filePath.EndsWith(".octadock", StringComparison.OrdinalIgnoreCase))
        {
            return await OpenProjectAsync(filePath, sourceCaptureId: null, cancellationToken).ConfigureAwait(false);
        }

        // Raster sources must be registered Octadock captures. Arbitrary image
        // files were annotatable before the preview/pin removal; that entry
        // point is gone and the refusal is explicit, never a silent no-op.
        CaptureRecord? registered = await FindRegisteredCaptureAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (registered is null)
        {
            _notifications.Notify(
                "Annotate",
                "Only Octadock captures and .octadock projects can be opened for annotation.",
                NotificationKind.Warning);
            return false;
        }

        return await OpenAsync(registered, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CaptureRecord?> FindRegisteredCaptureAsync(string filePath, CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception)
        {
            return null;
        }

        IReadOnlyList<CaptureRecord> recent = await _captures
            .GetRecentAsync(RegisteredCaptureScanLimit, cancellationToken)
            .ConfigureAwait(false);
        foreach (CaptureRecord record in recent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.IsRecording || string.IsNullOrWhiteSpace(record.OriginalPath))
            {
                continue;
            }

            string candidate;
            try
            {
                candidate = Path.GetFullPath(_paths.ToAbsolute(record.OriginalPath));
            }
            catch (Exception)
            {
                continue;
            }

            if (string.Equals(fullPath, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        return null;
    }

    private async Task<bool> OpenImagePathAsync(string imagePath, Guid? sourceCaptureId, string title, CancellationToken cancellationToken)
    {
        // Trial/license gate (WS5): starting a NEW annotation job on a raster image is
        // blocked post-expiry. Opening an existing .octadock project (OpenProjectAsync)
        // stays allowed — viewing/exporting existing work is never gated.
        if (!_licenseGate.Allow(GatedFeature.Annotate))
        {
            return false;
        }

        if (!File.Exists(imagePath))
        {
            _logger.LogWarning("Cannot open the annotation editor: image not found at {Path}.", imagePath);
            _notifications.Notify(
                "Annotate failed",
                "The capture's image file is missing. The History entry stays, but there is nothing to edit.",
                NotificationKind.Warning);
            return false;
        }

        try
        {
            BitmapSource bitmap = await Task.Run(() => _images.LoadFromFile(imagePath), cancellationToken).ConfigureAwait(false);
            var document = new AnnotationDocument(new PixelSize(bitmap.PixelWidth, bitmap.PixelHeight), sourceCaptureId);
            await ShowEditorAsync(document, bitmap, projectPath: null, sourceCaptureId, title).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cannot open the annotation editor: image at {Path} could not be decoded.", imagePath);
            _notifications.Notify(
                "Annotate failed",
                $"The capture's image file could not be opened. {ex.Message}",
                NotificationKind.Error);
            return false;
        }
    }

    private async Task<bool> OpenProjectAsync(string projectPath, Guid? sourceCaptureId, CancellationToken cancellationToken)
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
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to open annotation project {Path}.", projectPath);
            _notifications.Notify(
                "Annotate failed",
                "The .octadock project could not be opened; it may be damaged. The original capture is untouched.",
                NotificationKind.Error);
            return false;
        }
    }

    private async Task RecordAnnotatedAsync(Guid captureId, CancellationToken cancellationToken)
    {
        try
        {
            await _actions.AddAsync(
                new ActionRecord
                {
                    Id = Guid.NewGuid(),
                    CaptureId = captureId,
                    ActionType = ActionType.Annotated,
                    CreatedAt = DateTimeOffset.Now,
                    Destination = null,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // History is best-effort; never fail an editor launch over an action row.
            _logger.LogWarning(ex, "Failed to record an Annotated action for {CaptureId}.", captureId);
        }
    }

    private async Task ShowEditorAsync(
        AnnotationDocument document,
        BitmapSource baseImage,
        string? projectPath,
        Guid? sourceCaptureId,
        string title)
    {
        if (EditorLauncher is { } launcher)
        {
            await launcher(document, baseImage, projectPath, sourceCaptureId, title).ConfigureAwait(false);
            return;
        }

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

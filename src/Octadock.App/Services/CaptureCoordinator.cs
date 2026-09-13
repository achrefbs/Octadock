using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.App.CaptureUx;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Io;
using Octadock.Core.Models;
using Octadock.Core.Naming;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.Services;

/// <summary>
/// The capture pipeline. For every capture mode it (1) hides Octadock's own
/// overlays and ensures they are excluded from capture, (2) invokes
/// <see cref="ICaptureEngine"/> off the UI thread, then (3) runs the persistence
/// pipeline — encode to disk, generate a thumbnail, insert a <see cref="CaptureRecord"/>,
/// record an <see cref="ActionRecord"/> — and (4) applies the requested
/// <see cref="PostCaptureAction"/>. Heavy work stays off the UI thread; UI is
/// marshaled to the dispatcher. The most recent area rectangle is remembered so
/// "capture previous area" can reuse it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CaptureCoordinator : ICaptureCoordinator, IDisposable
{
    private readonly ICaptureEngine _captureEngine;
    private readonly IMonitorService _monitors;
    private readonly ICaptureExclusion _exclusion;
    private readonly IImageEncoder _encoder;
    private readonly IThumbnailGenerator _thumbnails;
    private readonly ICaptureRepository _captureRepository;
    private readonly IActionRepository _actionRepository;
    private readonly IStoragePaths _paths;
    private readonly IFilenameGenerator _filenames;
    private readonly ISettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly ISafeFileWriter _safeFileWriter;
    private readonly IShelfService _shelf;
    private readonly IAnnotationService _annotations;
    private readonly IScrollingCaptureEngine _scrolling;
    private readonly IServiceProvider _services;
    private readonly ILogger<CaptureCoordinator> _logger;

    private readonly object _previousGate = new();
    private readonly CaptureGate _captureGate;
    private PixelRect? _previousArea;
    private int _counter;

    /// <summary>Creates the capture coordinator.</summary>
    public CaptureCoordinator(
        ICaptureEngine captureEngine,
        IMonitorService monitors,
        ICaptureExclusion exclusion,
        IImageEncoder encoder,
        IThumbnailGenerator thumbnails,
        ICaptureRepository captureRepository,
        IActionRepository actionRepository,
        IStoragePaths paths,
        IFilenameGenerator filenames,
        ISettingsService settings,
        IClipboardService clipboard,
        INotificationService notifications,
        ISafeFileWriter safeFileWriter,
        IShelfService shelf,
        IAnnotationService annotations,
        IScrollingCaptureEngine scrolling,
        CaptureGate captureGate,
        IServiceProvider services,
        ILogger<CaptureCoordinator> logger)
    {
        _captureGate = captureGate;
        _captureEngine = captureEngine;
        _monitors = monitors;
        _exclusion = exclusion;
        _encoder = encoder;
        _thumbnails = thumbnails;
        _captureRepository = captureRepository;
        _actionRepository = actionRepository;
        _paths = paths;
        _filenames = filenames;
        _settings = settings;
        _clipboard = clipboard;
        _notifications = notifications;
        _safeFileWriter = safeFileWriter;
        _shelf = shelf;
        _annotations = annotations;
        _scrolling = scrolling;
        _services = services;
        _logger = logger;
    }

    private OctadockSettings Settings => _settings.Current;

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public Task CaptureAreaAsync(PostCaptureAction action, CancellationToken cancellationToken = default)
        => CaptureAreaAsync(action, region: null, cancellationToken);

    /// <summary>
    /// Area capture with an optional explicit region (supplied by automation). When
    /// <paramref name="region"/> is null, prompts via the selection overlay.
    /// </summary>
    public Task CaptureAreaAsync(PostCaptureAction action, PixelRect? region, CancellationToken cancellationToken = default)
        => IgnoreCaptureResultAsync(CaptureAreaWithResultAsync(action, region, cancellationToken));

    public Task<Guid?> CaptureAreaWithResultAsync(
        PostCaptureAction action,
        PixelRect? region,
        CancellationToken cancellationToken = default)
        => RunSerializedCaptureAsync(
            "area capture",
            token => CaptureAreaCoreAsync(action, region, token),
            cancellationToken);

    /// <summary>
    /// Self-timer capture: resolves the region (explicit or via the selection
    /// overlay), then waits <c>Capture.SelfTimerSeconds</c> before grabbing the
    /// frame so the user can arrange windows. A zero timer degrades to a plain
    /// area capture.
    /// </summary>
    public Task CaptureSelfTimerAsync(PostCaptureAction action, PixelRect? region, CancellationToken cancellationToken = default)
        => IgnoreCaptureResultAsync(CaptureSelfTimerWithResultAsync(action, region, cancellationToken));

    /// <summary>Self-timer capture that returns the durable capture identifier.</summary>
    public Task<Guid?> CaptureSelfTimerWithResultAsync(
        PostCaptureAction action,
        PixelRect? region,
        CancellationToken cancellationToken = default)
        => RunSerializedCaptureAsync(
            "self-timer capture",
            token => CaptureAreaCoreAsync(action, region, token, Math.Clamp(Settings.Capture.SelfTimerSeconds, 0, 60)),
            cancellationToken);

    private async Task<Guid?> CaptureAreaCoreAsync(PostCaptureAction action, PixelRect? region, CancellationToken cancellationToken, int selfTimerSeconds = 0)
    {
        PixelRect target;
        if (region is { } explicitRegion && !explicitRegion.IsEmpty)
        {
            target = explicitRegion;
        }
        else
        {
            IRegionSelectionService? selector = _services.GetService(typeof(IRegionSelectionService)) as IRegionSelectionService;
            if (selector is null)
            {
                // Fallback during bring-up: capture the active monitor.
                _logger.LogWarning("No region selection service registered; falling back to active-monitor capture.");
                return await CaptureFullscreenCoreAsync(action, monitorToken: null, allMonitors: false, cancellationToken).ConfigureAwait(false);
            }

            RegionSelection selection = await selector.SelectAreaAsync(cancellationToken).ConfigureAwait(false);
            if (!selection.Confirmed || selection.Region.IsEmpty)
            {
                _logger.LogDebug("Area selection cancelled.");
                return null;
            }

            target = selection.Region;
        }

        RememberPreviousArea(target);
        await HideOverlaysAsync().ConfigureAwait(false);

        if (selfTimerSeconds > 0)
        {
            // The countdown starts after the region is confirmed so the delay
            // buys time to arrange what is being captured (A-2: previously the
            // timer setting was read but never applied and the grab fired
            // immediately).
            await RunSelfTimerCountdownAsync(selfTimerSeconds, cancellationToken).ConfigureAwait(false);
        }

        var request = new AreaCaptureRequest
        {
            Region = target,
            IncludeCursor = Settings.Capture.IncludeCursor,
        };

        CapturedFrame frame = await _captureEngine.CaptureAreaAsync(request, cancellationToken).ConfigureAwait(false);
        return await FinishCaptureAsync(frame, CaptureType.Area, source: null, action, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CloseCountdownPillAsync(CaptureCountdownPill? pill)
    {
        if (pill is null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (pill.IsVisible)
            {
                pill.Close();
            }
        }).Task.ConfigureAwait(false);
    }

    private async Task RunSelfTimerCountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        CaptureCountdownPill? pill = null;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                pill = new CaptureCountdownPill(_monitors);
                pill.Update(seconds);
                pill.ShowOnActiveMonitor();
            }).Task.ConfigureAwait(false);

            for (int remaining = seconds; remaining > 0; remaining--)
            {
                await Dispatcher.InvokeAsync(() => pill?.Update(remaining)).Task.ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await CloseCountdownPillAsync(pill).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task CapturePreviousAreaAsync(PostCaptureAction action, CancellationToken cancellationToken = default)
        => IgnoreCaptureResultAsync(CapturePreviousAreaWithResultAsync(action, cancellationToken));

    /// <summary>Previous-area capture that returns the durable capture identifier.</summary>
    public Task<Guid?> CapturePreviousAreaWithResultAsync(
        PostCaptureAction action,
        CancellationToken cancellationToken = default)
        => RunSerializedCaptureAsync(
            "previous area capture",
            token => CapturePreviousAreaCoreAsync(action, token),
            cancellationToken);

    private async Task<Guid?> CapturePreviousAreaCoreAsync(PostCaptureAction action, CancellationToken cancellationToken)
    {
        PixelRect? previous;
        lock (_previousGate)
        {
            previous = _previousArea;
        }

        if (previous is null || previous.Value.IsEmpty)
        {
            _logger.LogDebug("No previous area recorded; prompting for a new selection.");
            return await CaptureAreaCoreAsync(action, region: null, cancellationToken).ConfigureAwait(false);
        }

        return await CaptureAreaCoreAsync(action, previous, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CaptureFullscreenAsync(PostCaptureAction action, string? monitorToken, bool allMonitors, CancellationToken cancellationToken = default)
        => IgnoreCaptureResultAsync(CaptureFullscreenWithResultAsync(action, monitorToken, allMonitors, cancellationToken));

    /// <summary>Fullscreen capture that returns the durable capture identifier.</summary>
    public Task<Guid?> CaptureFullscreenWithResultAsync(
        PostCaptureAction action,
        string? monitorToken,
        bool allMonitors,
        CancellationToken cancellationToken = default)
        => RunSerializedCaptureAsync(
            "fullscreen capture",
            token => CaptureFullscreenCoreAsync(action, monitorToken, allMonitors, token),
            cancellationToken);

    private async Task<Guid?> CaptureFullscreenCoreAsync(PostCaptureAction action, string? monitorToken, bool allMonitors, CancellationToken cancellationToken)
    {
        await HideOverlaysAsync().ConfigureAwait(false);

        MonitorId? monitorId = null;
        bool captureAll = allMonitors;

        if (!captureAll)
        {
            DisplayInfo? resolved = null;
            if (!string.IsNullOrWhiteSpace(monitorToken))
            {
                resolved = _monitors.Resolve(monitorToken);
                if (resolved is null)
                {
                    string message = $"Monitor '{monitorToken}' was not found.";
                    _notifications.Notify("Capture failed", message, NotificationKind.Warning);
                    throw new InvalidOperationException(message);
                }
            }

            switch (Settings.Capture.MultiMonitorMode)
            {
                case MultiMonitorCaptureMode.AllMonitors:
                    // A-4: an explicit monitor token from automation must force
                    // single-monitor capture; only fall back to the all-monitors
                    // preference when the caller did not name a monitor.
                    if (resolved is not null)
                    {
                        monitorId = resolved.Id;
                    }
                    else
                    {
                        captureAll = true;
                    }

                    break;
                case MultiMonitorCaptureMode.SelectedMonitor:
                    monitorId = (resolved ?? _monitors.GetActiveMonitor()).Id;
                    break;
                default:
                    monitorId = (resolved ?? _monitors.GetActiveMonitor()).Id;
                    break;
            }
        }

        var request = new FullscreenCaptureRequest
        {
            Monitor = captureAll ? null : monitorId,
            AllMonitors = captureAll,
            IncludeCursor = Settings.Capture.IncludeCursor,
        };

        CapturedFrame frame = await _captureEngine.CaptureFullscreenAsync(request, cancellationToken).ConfigureAwait(false);
        return await FinishCaptureAsync(frame, CaptureType.Fullscreen, source: null, action, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CaptureWindowAsync(PostCaptureAction action, CancellationToken cancellationToken = default)
        => CaptureWindowAsync(action, windowHandleHex: null, cancellationToken);

    /// <summary>Window capture with an optional explicit HWND (trusted automation).</summary>
    public Task CaptureWindowAsync(PostCaptureAction action, string? windowHandleHex, CancellationToken cancellationToken = default)
        => IgnoreCaptureResultAsync(CaptureWindowWithResultAsync(action, windowHandleHex, cancellationToken));

    /// <summary>Window capture that returns the durable capture identifier.</summary>
    public Task<Guid?> CaptureWindowWithResultAsync(
        PostCaptureAction action,
        string? windowHandleHex,
        CancellationToken cancellationToken = default)
        => RunSerializedCaptureAsync(
            "window capture",
            token => CaptureWindowCoreAsync(action, windowHandleHex, token),
            cancellationToken);

    private async Task<Guid?> CaptureWindowCoreAsync(PostCaptureAction action, string? windowHandleHex, CancellationToken cancellationToken)
    {
        WindowHandle handle;
        if (!string.IsNullOrWhiteSpace(windowHandleHex) && WindowHandle.TryParseHex(windowHandleHex, out WindowHandle parsed))
        {
            handle = parsed;
        }
        else
        {
            IRegionSelectionService? selector = _services.GetService(typeof(IRegionSelectionService)) as IRegionSelectionService;
            if (selector is null)
            {
                _logger.LogWarning("No window picker registered; falling back to active-monitor capture.");
                return await CaptureFullscreenCoreAsync(action, monitorToken: null, allMonitors: false, cancellationToken).ConfigureAwait(false);
            }

            RegionSelection selection = await selector.SelectWindowAsync(cancellationToken).ConfigureAwait(false);
            if (!selection.Confirmed || !WindowHandle.TryParseHex(selection.WindowHandleHex, out handle))
            {
                _logger.LogDebug("Window selection cancelled.");
                return null;
            }
        }

        await HideOverlaysAsync().ConfigureAwait(false);

        var request = new WindowCaptureRequest
        {
            Window = handle,
            IncludeShadow = Settings.Capture.WindowShadow,
            IncludeCursor = Settings.Capture.IncludeCursor,
        };

        CapturedFrame frame = await _captureEngine.CaptureWindowAsync(request, cancellationToken).ConfigureAwait(false);
        CaptureSource source = BuildWindowSource(handle);
        return await FinishCaptureAsync(frame, CaptureType.Window, source, action, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task CaptureScrollingAsync(PostCaptureAction action, CancellationToken cancellationToken = default)
        => CaptureScrollingAsync(action, region: null, cancellationToken);

    public Task CaptureScrollingAsync(PostCaptureAction action, PixelRect? region, CancellationToken cancellationToken = default)
        => CaptureScrollingAsync(action, region, new ScrollingCaptureOptions(), cancellationToken);

    /// <summary>
    /// Manual scrolling capture. The user selects a viewport region, then scrolls
    /// the target while Octadock samples until the user finishes or the resource
    /// limit is reached, stitching new rows as they appear.
    /// </summary>
    public Task CaptureScrollingAsync(
        PostCaptureAction action,
        PixelRect? region,
        ScrollingCaptureOptions options,
        CancellationToken cancellationToken = default)
        => IgnoreCaptureResultAsync(CaptureScrollingWithResultAsync(action, region, options, cancellationToken));

    /// <summary>Manual vertical scrolling capture that returns the durable capture identifier.</summary>
    public Task<Guid?> CaptureScrollingWithResultAsync(
        PostCaptureAction action,
        PixelRect? region,
        ScrollingCaptureOptions options,
        CancellationToken cancellationToken = default)
        => RunSerializedCaptureAsync(
            "scrolling capture",
            token => CaptureScrollingCoreAsync(action, region, options, token),
            cancellationToken);

    private async Task<Guid?> CaptureScrollingCoreAsync(
        PostCaptureAction action,
        PixelRect? region,
        ScrollingCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Direction != ScrollDirection.Vertical)
        {
            _notifications.Notify("Scrolling capture", "Horizontal scrolling capture is not supported yet.", NotificationKind.Warning);
            throw new NotSupportedException("Horizontal scrolling capture is not supported yet.");
        }

        if (options.AutoScroll)
        {
            _notifications.Notify("Scrolling capture", "Automatic scrolling capture is not supported yet.", NotificationKind.Warning);
            throw new NotSupportedException("Automatic scrolling capture is not supported yet.");
        }

        PixelRect target;
        if (region is { } explicitRegion && !explicitRegion.IsEmpty)
        {
            target = explicitRegion;
        }
        else
        {
            IRegionSelectionService? selector = _services.GetService(typeof(IRegionSelectionService)) as IRegionSelectionService;
            if (selector is null)
            {
                _notifications.Notify("Scrolling capture", "Region selection is not available.", NotificationKind.Warning);
                return null;
            }

            _notifications.Notify("Scrolling capture", "Select the viewport you want to stitch.", NotificationKind.Info);
            RegionSelection selection = await selector.SelectAreaAsync(cancellationToken).ConfigureAwait(false);
            if (!selection.Confirmed || selection.Region.IsEmpty)
            {
                _logger.LogDebug("Scrolling capture selection cancelled.");
                return null;
            }

            target = selection.Region;
        }

        await HideOverlaysAsync().ConfigureAwait(false);

        // The session is driven by a visible pill next to the region: live
        // stitched height, an explicit Finish, and a Cancel. Previously the
        // loop ran silently on a fixed 20-second timer, which made scrolling
        // capture indistinguishable from a normal screenshot.
        var control = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Octadock.App.CaptureUx.ScrollingSessionPill? pill = null;

        try
        {
            await _scrolling.StartSessionAsync(target, options, cancellationToken).ConfigureAwait(false);
            int stitchedHeight = await _scrolling.CaptureFrameAsync(cancellationToken).ConfigureAwait(false);

            DisplayInfo pillMonitor = _monitors.GetMonitorFromPoint(target.Center);
            await Dispatcher.InvokeAsync(() =>
            {
                pill = new Octadock.App.CaptureUx.ScrollingSessionPill();
                pill.FinishRequested += (_, _) => control.TrySetResult(true);
                pill.CancelRequested += (_, _) => control.TrySetResult(false);
                pill.Update(stitchedHeight, hasGrown: false);
                pill.ShowNear(target, pillMonitor);
            });

            bool grew = false;
            TimeSpan interval = TimeSpan.FromMilliseconds(100);

            while (!control.Task.IsCompleted)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                int nextHeight = await _scrolling.CaptureFrameAsync(cancellationToken).ConfigureAwait(false);
                if (nextHeight > stitchedHeight)
                {
                    grew = true;
                }

                stitchedHeight = nextHeight;
                int shownHeight = stitchedHeight;
                bool shownGrew = grew;
                _ = Dispatcher.InvokeAsync(() => pill?.Update(shownHeight, shownGrew));

                if (options.MaxStitchedEdge > 0 && stitchedHeight >= options.MaxStitchedEdge) break;

            }

            if (control.Task.IsCompleted && !control.Task.Result)
            {
                _scrolling.Cancel();
                _notifications.Notify("Scrolling capture", "Cancelled.", NotificationKind.Info);
                return null;
            }

            // Include the last viewport if Finish arrived between sampling ticks.
            await _scrolling.CaptureFrameAsync(cancellationToken).ConfigureAwait(false);
            ScrollingCaptureResult result = await _scrolling.FinishAsync(cancellationToken).ConfigureAwait(false);
            CapturedFrame frame = result.Frame;
            Guid captureId = await FinishCaptureAsync(
                frame,
                CaptureType.Scrolling,
                frame.Source,
                action,
                cancellationToken).ConfigureAwait(false);

            if (!grew)
            {
                _notifications.Notify(
                    "Scrolling capture",
                    "Captured the visible area only — scroll while the pill is shown to stitch a longer shot.",
                    NotificationKind.Info);
            }
            else if (result.Truncated)
            {
                _notifications.Notify(
                    "Scrolling capture truncated",
                    $"Stopped at {result.MaxStitchedEdge} pixels tall.",
                    NotificationKind.Warning);
            }

            return captureId;
        }
        catch
        {
            _scrolling.Cancel();
            throw;
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                pill?.Close();
            });
        }
    }


    // ---- Persistence pipeline ---------------------------------------------

    private async Task<Guid> FinishCaptureAsync(
        CapturedFrame frame,
        CaptureType type,
        CaptureSource? source,
        PostCaptureAction action,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = frame.CapturedAt == default ? DateTimeOffset.Now : frame.CapturedAt;
        Guid id = Guid.NewGuid();

        bool jpeg = Settings.Capture.ImageFormat == CaptureImageFormat.Jpeg && type != CaptureType.Window;
        ExportImageFormat format = jpeg ? ExportImageFormat.Jpeg : ExportImageFormat.Png;
        string extension = jpeg ? ".jpg" : ".png";
        var encodeOptions = new EncodeOptions { Format = format, Quality = Settings.Capture.JpegQuality };

        string relative = _paths.BuildCaptureRelativePath(id, now, extension);
        string absolute = _paths.ToAbsolute(relative);

        // Encode + thumbnail off the UI thread.
        await _encoder.EncodeToFileAsync(frame, absolute, encodeOptions, cancellationToken).ConfigureAwait(false);

        string thumbRelative = _paths.BuildThumbnailRelativePath(id);
        await TryGenerateThumbnailFromFrameAsync(frame, _paths.ToAbsolute(thumbRelative), cancellationToken).ConfigureAwait(false);

        var record = new CaptureRecord
        {
            Id = id,
            Type = type,
            CreatedAt = now,
            Source = source ?? CaptureSource.Empty,
            MonitorId = frame.MonitorId,
            PixelWidth = frame.Width,
            PixelHeight = frame.Height,
            DpiScale = frame.DpiScale,
            OriginalPath = relative,
            ThumbnailPath = thumbRelative,
        };

        await _captureRepository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await ApplyPostCaptureActionAsync(record, action, cancellationToken).ConfigureAwait(false);
        return id;
    }

    private async Task ApplyPostCaptureActionAsync(CaptureRecord record, PostCaptureAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case PostCaptureAction.Copy:
                try
                {
                    _clipboard.SetImageFromFile(_paths.ToAbsolute(record.OriginalPath));
                    await RecordActionAsync(record.Id, ActionType.Copied, "clipboard", cancellationToken).ConfigureAwait(false);
                    _notifications.Notify("Copied", "The capture is on your clipboard.", NotificationKind.Success);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to copy capture {Id} to the clipboard.", record.Id);
                    _notifications.Notify("Copy failed", "The capture was saved to history, but could not be copied.", NotificationKind.Error);
                    await ShowOnShelfOrNotifyAsync(record, cancellationToken).ConfigureAwait(false);
                }
                break;

            case PostCaptureAction.Save:
                await SaveAsync(record, cancellationToken).ConfigureAwait(false);
                break;

            case PostCaptureAction.Annotate:
                // The annotation editor is the capture image surface; it records
                // the honest Annotated action itself when the editor opens.
                await _annotations.OpenAsync(record, cancellationToken).ConfigureAwait(false);
                break;

            case PostCaptureAction.Pin:
                // Pins were removed. Never drop the capture silently: land it on
                // the Shelf and say why the requested action did not happen.
                _notifications.Notify(
                    "Pins removed",
                    "Floating pins were removed in this version of Octadock. The capture is on your Shelf instead.",
                    NotificationKind.Warning);
                await ShowOnShelfOrNotifyAsync(record, cancellationToken).ConfigureAwait(false);
                break;

            case PostCaptureAction.Discard:
                // Discard ends the immediate capture workflow; it is not a
                // History deletion. The durable record remains available for
                // recovery until the user deletes it from History or retention
                // expires it.
                await RecordActionAsync(record.Id, ActionType.Discarded, destination: null, cancellationToken).ConfigureAwait(false);
                break;

            case PostCaptureAction.Upload:
                // Retain old command values without introducing a network path.
                _notifications.Notify("Upload", "Upload was removed. The capture is available locally on the shelf.", NotificationKind.Warning);
                await ShowOnShelfOrNotifyAsync(record, cancellationToken).ConfigureAwait(false);
                break;

            case PostCaptureAction.Shelf:
            default:
                if (await _shelf.ShowAsync(record, cancellationToken).ConfigureAwait(false))
                {
                    await RecordActionAsync(record.Id, ActionType.Shelved, "shelf", cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _notifications.Notify("Saved to history", "The capture was saved, but the shelf could not be shown.", NotificationKind.Warning);
                }
                break;
        }
    }

    private async Task ShowOnShelfOrNotifyAsync(CaptureRecord record, CancellationToken cancellationToken)
    {
        if (!await _shelf.ShowAsync(record, cancellationToken).ConfigureAwait(false))
        {
            _notifications.Notify("Saved to history", "The capture was saved, but the shelf could not be shown.", NotificationKind.Warning);
        }
    }

    private async Task SaveAsync(CaptureRecord record, CancellationToken cancellationToken)
    {
        string sourcePath = _paths.ToAbsolute(record.OriginalPath);
        string extension = Path.GetExtension(sourcePath);
        string baseName = _filenames.Generate(
            Settings.Capture.FilenameTemplate,
            new FilenameContext
            {
                Timestamp = record.CreatedAt,
                Type = record.Type,
                ProcessName = record.Source.ProcessName,
                WindowTitle = record.Source.WindowTitle,
                Counter = NextCounter(),
            });

        string configuredDir = Settings.Capture.SaveDirectory;
        string? destination;
        bool avoidOverwrite;

        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            destination = Path.Combine(configuredDir, baseName + extension);
            avoidOverwrite = true;
        }
        else
        {
            destination = await PromptSaveAsAsync(baseName + extension, extension).ConfigureAwait(false);
            if (destination is null)
            {
                // Cancelled: keep it on the shelf so nothing is lost.
                await ShowOnShelfOrNotifyAsync(record, cancellationToken).ConfigureAwait(false);
                return;
            }

            // SaveFileDialog already asks before replacing an existing file. The
            // writer keeps that confirmed overwrite atomic and revision-backed.
            avoidOverwrite = false;
        }

        string finalDestination = avoidOverwrite
            ? await _safeFileWriter.CopyToUniqueAsync(sourcePath, destination, cancellationToken).ConfigureAwait(false)
            : destination;
        if (!avoidOverwrite)
        {
            await _safeFileWriter.CopyAsync(sourcePath, finalDestination, cancellationToken).ConfigureAwait(false);
        }

        await RecordActionAsync(record.Id, ActionType.Saved, finalDestination, cancellationToken).ConfigureAwait(false);
        _notifications.Notify("Saved", Path.GetFileName(finalDestination), NotificationKind.Success);
    }

    private async Task<string?> PromptSaveAsAsync(string suggestedName, string extension)
    {
        string filter = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            ? "JPEG image (*.jpg)|*.jpg|PNG image (*.png)|*.png"
            : "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg";

        return await Dispatcher.InvokeAsync(() =>
        {
            var dialog = new SaveFileDialog
            {
                FileName = suggestedName,
                DefaultExt = extension,
                Filter = filter,
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };

            bool? ok = dialog.ShowDialog();
            return ok == true ? dialog.FileName : null;
        });
    }

    // ---- Helpers ----------------------------------------------------------

    private async Task<Guid?> RunSerializedCaptureAsync(
        string operation,
        Func<CancellationToken, Task<Guid?>> capture,
        CancellationToken cancellationToken)
    {
        bool waited = false;
        if (!await _captureGate.TryEnterImmediatelyAsync(cancellationToken).ConfigureAwait(false))
        {
            waited = true;
            _notifications.Notify("Capture queued", "Another capture is already active.", NotificationKind.Info);
            await _captureGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (waited)
            {
                _logger.LogDebug("Starting queued {Operation}.", operation);
            }

            return await capture(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _captureGate.Exit();
        }
    }

    private static async Task IgnoreCaptureResultAsync(Task<Guid?> capture)
        => _ = await capture.ConfigureAwait(false);

    private async Task HideOverlaysAsync()
    {
        // UX principle: the capture UI must disappear before the grab.
        if (_services.GetService(typeof(IRegionSelectionService)) is IRegionSelectionService selector)
        {
            void Hide() => selector.HideAll();
            if (Dispatcher.CheckAccess())
            {
                Hide();
            }
            else
            {
                await Dispatcher.InvokeAsync(Hide);
            }

            // Give the compositor a frame to actually remove the overlay before capture.
            await Task.Delay(30).ConfigureAwait(false);
        }
    }

    private void RememberPreviousArea(PixelRect region)
    {
        lock (_previousGate)
        {
            _previousArea = region;
        }
    }

    private async Task RecordActionAsync(Guid captureId, ActionType type, string? destination, CancellationToken cancellationToken)
    {
        try
        {
            await _actionRepository.AddAsync(
                new ActionRecord
                {
                    Id = Guid.NewGuid(),
                    CaptureId = captureId,
                    ActionType = type,
                    CreatedAt = DateTimeOffset.Now,
                    Destination = destination,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // History is best-effort; never fail a capture over an action row.
            _logger.LogWarning(ex, "Failed to record a {ActionType} action for {CaptureId}.", type, captureId);
        }
    }

    private async Task TryGenerateThumbnailFromFrameAsync(CapturedFrame frame, string thumbnailPath, CancellationToken cancellationToken)
    {
        try
        {
            EncodedImage thumb = _thumbnails.Generate(frame);
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            await File.WriteAllBytesAsync(thumbnailPath, thumb.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate a thumbnail from the frame.");
        }
    }

    private async Task TryGenerateThumbnailAsync(string sourceImage, string thumbnailPath, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnails.GenerateToFileAsync(sourceImage, thumbnailPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate a thumbnail from {Source}.", sourceImage);
        }
    }

    private CaptureSource BuildWindowSource(WindowHandle handle)
    {
        try
        {
            var picker = _services.GetService(typeof(IWindowPicker)) as IWindowPicker;
            CandidateWindow? window = picker?.EnumerateWindows()
                .FirstOrDefault(w => w.Handle.Value == handle.Value);

            if (window is null)
            {
                return CaptureSource.Empty;
            }

            return new CaptureSource(window.ProcessName, window.Title, HashHandle(handle));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve window provenance for {Handle}.", handle);
            return CaptureSource.Empty;
        }
    }

    private static string HashHandle(WindowHandle handle)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(handle.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return Convert.ToHexString(bytes, 0, 8);
    }


    private int NextCounter() => System.Threading.Interlocked.Increment(ref _counter);

    /// <inheritdoc />
    public void Dispose()
    {
        // The capture gate is a shared singleton (also used by OcrService);
        // the service provider owns and disposes it, not this coordinator.
    }
}

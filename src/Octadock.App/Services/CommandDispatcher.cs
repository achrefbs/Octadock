using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Recording;

namespace Octadock.App.Services;

/// <summary>
/// <see cref="ICommandDispatcher"/>. Translates a parsed <see cref="OctadockCommand"/>
/// (from the <c>octadock://</c> protocol or the CLI) into calls on the capture
/// coordinator, shelf, pins, annotation, OCR, AI session watcher and window
/// presenter. Region units are converted to physical pixels against the owning monitor when
/// <c>units=dip</c> is supplied. Returns a <see cref="CommandResult"/> the CLI/IPC
/// can surface.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly CaptureCoordinator _coordinator;
    private readonly IShelfService _shelf;
    private readonly IPinService _pins;
    private readonly IAnnotationService _annotations;
    private readonly IOcrService _ocr;
    private readonly IWindowPresenter _presenter;
    private readonly IMonitorService _monitors;
    private readonly RecordingController _recording;
    private readonly DictationController _dictation;
    private readonly ReadAloudService _readAloud;
    private readonly FilePreviewService _preview;
    private readonly AiSessionCommandService _aiSessions;
    private readonly ILogger<CommandDispatcher> _logger;

    /// <summary>Creates the command dispatcher.</summary>
    public CommandDispatcher(
        CaptureCoordinator coordinator,
        IShelfService shelf,
        IPinService pins,
        IAnnotationService annotations,
        IOcrService ocr,
        IWindowPresenter presenter,
        IMonitorService monitors,
        RecordingController recording,
        DictationController dictation,
        ReadAloudService readAloud,
        FilePreviewService preview,
        AiSessionCommandService aiSessions,
        ILogger<CommandDispatcher> logger)
    {
        _coordinator = coordinator;
        _shelf = shelf;
        _pins = pins;
        _annotations = annotations;
        _ocr = ocr;
        _presenter = presenter;
        _monitors = monitors;
        _recording = recording;
        _dictation = dictation;
        _readAloud = readAloud;
        _preview = preview;
        _aiSessions = aiSessions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommandResult> DispatchAsync(OctadockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        _logger.LogInformation("Dispatching command {Command}.", command);

        try
        {
            return await RouteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CommandResult.Fail("The command was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Type} failed.", command.Type);
            return CommandResult.Fail($"Command failed: {ex.Message}");
        }
    }

    private async Task<CommandResult> RouteAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        switch (command.Type)
        {
            case CommandType.CaptureArea:
                await _coordinator.CaptureAreaAsync(command.Action, ResolveRegion(command), cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.CapturePreviousArea:
                await _coordinator.CapturePreviousAreaAsync(command.Action, cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.CaptureFullscreen:
                bool allMonitors = command.GetBool("allmonitors");
                if (!allMonitors &&
                    !string.IsNullOrWhiteSpace(command.Monitor) &&
                    _monitors.Resolve(command.Monitor) is null)
                {
                    return CommandResult.Fail($"Monitor '{command.Monitor}' was not found.");
                }

                await _coordinator.CaptureFullscreenAsync(command.Action, command.Monitor, allMonitors, cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.CaptureWindow:
                await _coordinator.CaptureWindowAsync(command.Action, command.Get("hwnd"), cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.AllInOne:
                PixelRect? hudRegion = ResolveRegion(command);
                (int? hudWidth, int? hudHeight) = ResolveHudPreloadSize(command, hudRegion);
                _presenter.ShowAllInOneHud(command.Mode, hudRegion, hudWidth, hudHeight);
                return CommandResult.Ok;

            case CommandType.SelfTimer:
                // Self-timer: resolve the region first, then the coordinator
                // applies the configured countdown before the grab.
                await _coordinator.CaptureSelfTimerAsync(command.Action, ResolveRegion(command), cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.ScrollingCapture:
                ScrollingCaptureOptions? scrollingOptions = ResolveScrollingOptions(command, out string? scrollingError);
                if (scrollingOptions is null)
                {
                    return CommandResult.Fail(scrollingError ?? "Unsupported scrolling capture options.");
                }

                await _coordinator.CaptureScrollingAsync(command.Action, ResolveRegion(command), scrollingOptions, cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.RecordScreen:
                // Toggle semantics: starts the requested recording target, or
                // stops and saves the one in progress.
                RecordingStartRequest? recordingRequest = ResolveRecordingRequest(command, out string? recordingError);
                if (recordingRequest is null)
                {
                    return CommandResult.Fail(recordingError ?? "Unsupported recording options.");
                }

                await _recording.ToggleAsync(recordingRequest, cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.Pin:
                return await RoutePinAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.CaptureText:
                return await RouteOcrAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.ReadAloud:
                return await RouteReadAloudAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.Dictation:
                await _dictation.ToggleAsync(cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.OpenAnnotate:
                return await RouteAnnotateAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.OpenFromClipboard:
                await _annotations.OpenFromClipboardAsync(cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.AddShelfItem:
                if (string.IsNullOrWhiteSpace(command.FilePath))
                {
                    return CommandResult.Fail("add-shelf-item requires a 'filepath' parameter.");
                }

                await _coordinator.AddExternalFileAsync(command.FilePath, cancellationToken).ConfigureAwait(false);
                return CommandResult.Ok;

            case CommandType.Open:
                if (string.IsNullOrWhiteSpace(command.FilePath))
                {
                    return CommandResult.Fail("open requires a 'filepath' parameter.");
                }

                bool previewed = await _preview.PreviewAsync(command.FilePath, cancellationToken).ConfigureAwait(false);
                return previewed
                    ? CommandResult.Ok
                    : CommandResult.Fail($"Could not preview '{command.FilePath}'.");

            case CommandType.OpenHistory:
                _presenter.ShowHistory();
                return CommandResult.Ok;

            case CommandType.OpenClipboardHistory:
                _presenter.ShowClipboardHistory();
                return CommandResult.Ok;

            case CommandType.OpenTextTools:
                _presenter.ShowTextTools();
                return CommandResult.Ok;

            case CommandType.RestoreRecentlyClosed:
                bool restored = await _shelf.RestoreRecentlyClosedAsync(cancellationToken).ConfigureAwait(false);
                return restored ? CommandResult.Ok : CommandResult.Fail("There was nothing to restore.");

            case CommandType.ClearHistory:
                // Destructive: leave the confirmation + purge to the history UI.
                _presenter.ShowHistory();
                return CommandResult.Ok;

            case CommandType.OpenSettings:
                _presenter.ShowSettings(command.Get("tab"));
                return CommandResult.Ok;

            case CommandType.OpenAiSessions:
                _presenter.ShowAiSessions();
                return CommandResult.Ok;

            case CommandType.Run:
                return await _aiSessions.RunAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.Watch:
                return await _aiSessions.WatchPidAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.AiSessionEvent:
                return await _aiSessions.AddHookEventAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.Unknown:
            default:
                return CommandResult.Fail($"Unknown or unsupported command '{command.Type}'.");
        }
    }

    private async Task<CommandResult> RoutePinAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        if (command.GetBool("clipboard"))
        {
            await _pins.PinFromClipboardAsync(cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok;
        }

        if (!string.IsNullOrWhiteSpace(command.FilePath))
        {
            await _pins.PinImageFileAsync(command.FilePath, cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok;
        }

        return CommandResult.Fail("pin requires 'filepath' or 'clipboard=true'.");
    }

    private RecordingStartRequest? ResolveRecordingRequest(OctadockCommand command, out string? error)
    {
        error = null;

        DisplayInfo? monitor = null;
        if (!string.IsNullOrWhiteSpace(command.Monitor))
        {
            monitor = _monitors.Resolve(command.Monitor);
            if (monitor is null)
            {
                error = $"Monitor '{command.Monitor}' was not found.";
                return null;
            }
        }

        PixelRect? region = ResolveRegion(command);
        bool selectArea = command.GetBool("select-area") || command.GetBool("selected-area");
        if (selectArea && region is not null)
        {
            error = "record-screen cannot combine an explicit region with '--select-area'.";
            return null;
        }

        if (selectArea && monitor is not null)
        {
            error = "record-screen cannot combine '--monitor' with '--select-area'.";
            return null;
        }

        return new RecordingStartRequest
        {
            Region = region,
            Monitor = monitor?.Id,
            PromptForRegion = selectArea,
        };
    }

    private async Task<CommandResult> RouteOcrAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        OcrTextMode mode = command.GetEnum<OcrTextMode>("mode")
            ?? (command.GetBool("linebreaks") ? OcrTextMode.Lines : OcrTextMode.Compact);
        string? language = command.Get("language");

        if (!string.IsNullOrWhiteSpace(command.FilePath))
        {
            await _ocr.ExtractFromFileAsync(command.FilePath, mode, language, cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok;
        }

        PixelRect? region = ResolveRegion(command);
        if (region is not null)
        {
            await _ocr.CaptureRegionTextAsync(region.Value, mode, language, cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok;
        }

        await _ocr.CaptureRegionTextAsync(mode, language, cancellationToken).ConfigureAwait(false);
        return CommandResult.Ok;
    }

    private async Task<CommandResult> RouteReadAloudAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        if (command.Region is not null && ResolveRegion(command) is PixelRect resolvedRegion)
        {
            command = WithRegion(command, resolvedRegion);
        }

        return await _readAloud.StartAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResult> RouteAnnotateAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.FilePath))
        {
            await _annotations.OpenFileAsync(command.FilePath, cancellationToken).ConfigureAwait(false);
            return CommandResult.Ok;
        }

        // open-annotate with no file just brings up the editor's open flow via file.
        return CommandResult.Fail("open-annotate requires a 'filepath' (captureId routing is handled by the history UI).");
    }

    private static ScrollingCaptureOptions? ResolveScrollingOptions(OctadockCommand command, out string? error)
    {
        error = null;

        ScrollDirection direction = ScrollDirection.Vertical;
        if (command.Has("direction"))
        {
            ScrollDirection? parsed = command.GetEnum<ScrollDirection>("direction");
            if (parsed is null)
            {
                error = "Unsupported scrolling capture direction.";
                return null;
            }

            direction = parsed.Value;
        }

        if (direction == ScrollDirection.Horizontal)
        {
            error = "Horizontal scrolling capture is not supported yet.";
            return null;
        }

        bool autoScroll = command.GetBool("autoscroll");
        if (autoScroll)
        {
            error = "Automatic scrolling capture is not supported yet; use manual vertical scrolling.";
            return null;
        }

        int maxStitchedEdge = command.GetInt("maxstitchededge")
            ?? command.GetInt("maxedge")
            ?? 32000;

        return new ScrollingCaptureOptions
        {
            Direction = direction,
            AutoScroll = false,
            MaxStitchedEdge = maxStitchedEdge,
        };
    }

    /// <summary>
    /// Resolves the command's region to physical pixels. When <c>units=dip</c>, the
    /// x/y/width/height are treated as DIPs relative to the owning monitor and
    /// converted with that monitor's scale factor; otherwise they are already
    /// physical pixels.
    /// </summary>
    private PixelRect? ResolveRegion(OctadockCommand command)
    {
        PixelRect? raw = command.Region;
        if (raw is null)
        {
            return null;
        }

        if (command.Units != CoordinateUnits.Dip)
        {
            return raw;
        }

        PixelRect r = raw.Value;

        // Resolve the monitor: explicit token, else the monitor under the DIP-origin
        // point interpreted against the primary (best effort), else active.
        DisplayInfo monitor = _monitors.Resolve(command.Monitor) ?? _monitors.GetActiveMonitor();

        var dip = new DipRect(r.X, r.Y, r.Width, r.Height);
        return monitor.ToPixels(dip);
    }

    private (int? Width, int? Height) ResolveHudPreloadSize(OctadockCommand command, PixelRect? resolvedRegion)
    {
        if (resolvedRegion is not null)
        {
            return (null, null);
        }

        int? width = command.GetInt("width");
        int? height = command.GetInt("height");
        if (width is null && height is null)
        {
            return (null, null);
        }

        if (command.Units != CoordinateUnits.Dip)
        {
            return (width, height);
        }

        DisplayInfo monitor = _monitors.Resolve(command.Monitor) ?? _monitors.GetActiveMonitor();
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        return (
            width is null ? null : Math.Max(1, (int)Math.Round(width.Value * scale)),
            height is null ? null : Math.Max(1, (int)Math.Round(height.Value * scale)));
    }

    private static OctadockCommand WithRegion(OctadockCommand command, PixelRect region)
    {
        var parameters = new Dictionary<string, string>(command.Parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = region.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["y"] = region.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["width"] = region.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["height"] = region.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["units"] = CoordinateUnits.Pixels.ToString(),
        };

        return command with { Parameters = parameters };
    }
}

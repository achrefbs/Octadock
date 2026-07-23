using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.App.Ai;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Licensing;
using Octadock.Core.Recording;

namespace Octadock.App.Services;

/// <summary>
/// <see cref="ICommandDispatcher"/>. Translates a parsed <see cref="OctadockCommand"/>
/// (from the <c>octadock://</c> protocol or the CLI) into calls on the capture
/// coordinator, shelf, OCR and window
/// presenter. Commands whose features were removed in this version keep a parse-level
/// tombstone and fail truthfully here. Region units are converted to physical pixels
/// against the owning monitor when <c>units=dip</c> is supplied. Returns a
/// <see cref="CommandResult"/> the CLI/IPC can surface.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly CaptureCoordinator _coordinator;
    private readonly IShelfService _shelf;
    private readonly IOcrService _ocr;
    private readonly IWindowPresenter _presenter;
    private readonly IMonitorService _monitors;
    private readonly RecordingController _recording;
    private readonly DictationController _dictation;
    private readonly ReadAloudService _readAloud;
    private readonly ActivationService _activation;
    private readonly IActivationReplacementConfirmation _activationReplacementConfirmation;
    private readonly ILicenseGate _licenseGate;
    private readonly INotificationService _notifications;
    private readonly ILogger<CommandDispatcher> _logger;

    /// <summary>Creates the command dispatcher.</summary>
    public CommandDispatcher(
        CaptureCoordinator coordinator,
        IShelfService shelf,
        IOcrService ocr,
        IWindowPresenter presenter,
        IMonitorService monitors,
        RecordingController recording,
        DictationController dictation,
        ReadAloudService readAloud,
        ActivationService activation,
        IActivationReplacementConfirmation activationReplacementConfirmation,
        ILicenseGate licenseGate,
        INotificationService notifications,
        ILogger<CommandDispatcher> logger)
    {
        _coordinator = coordinator;
        _shelf = shelf;
        _ocr = ocr;
        _presenter = presenter;
        _monitors = monitors;
        _recording = recording;
        _dictation = dictation;
        _readAloud = readAloud;
        _activation = activation;
        _activationReplacementConfirmation = activationReplacementConfirmation;
        _licenseGate = licenseGate;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CommandResult> DispatchAsync(OctadockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Never log the parameter bag: AI/read commands can carry private source text.
        _logger.LogInformation("Dispatching command type {Type}.", command.Type);

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
        cancellationToken.ThrowIfCancellationRequested();

        // Removed features keep a parse-level tombstone (WS-removal contract):
        // the token still resolves, but dispatch fails truthfully and non-zero
        // instead of silently doing nothing or reporting success.
        if (CommandTokens.IsRemoved(command.Type))
        {
            _logger.LogInformation("Refused removed command {Type}.", command.Type);
            return CommandResult.Fail(CommandTokens.RemovedMessage(command.Type));
        }

        if (RequiredLicenseFeature(command.Type) is { } feature &&
            !_licenseGate.Allow(feature))
        {
            return CommandResult.Fail(
                $"{FeatureName(feature)} needs an active trial or license. Existing Octadock data remains available.");
        }

        switch (command.Type)
        {
            case CommandType.CaptureArea:
                return CaptureCommandResult(await _coordinator
                    .CaptureAreaWithResultAsync(command.Action, ResolveRegion(command), cancellationToken)
                    .ConfigureAwait(false));

            case CommandType.CapturePreviousArea:
                return CaptureCommandResult(await _coordinator
                    .CapturePreviousAreaWithResultAsync(command.Action, cancellationToken)
                    .ConfigureAwait(false));

            case CommandType.CaptureFullscreen:
                bool allMonitors = command.GetBool("allmonitors");
                if (!allMonitors &&
                    !string.IsNullOrWhiteSpace(command.Monitor) &&
                    _monitors.Resolve(command.Monitor) is null)
                {
                    return CommandResult.Fail($"Monitor '{command.Monitor}' was not found.");
                }

                return CaptureCommandResult(await _coordinator
                    .CaptureFullscreenWithResultAsync(command.Action, command.Monitor, allMonitors, cancellationToken)
                    .ConfigureAwait(false));

            case CommandType.CaptureWindow:
                return CaptureCommandResult(await _coordinator
                    .CaptureWindowWithResultAsync(command.Action, command.Get("hwnd"), cancellationToken)
                    .ConfigureAwait(false));

            case CommandType.SelfTimer:
                // Self-timer: resolve the region first, then the coordinator
                // applies the configured countdown before the grab.
                return CaptureCommandResult(await _coordinator
                    .CaptureSelfTimerWithResultAsync(command.Action, ResolveRegion(command), cancellationToken)
                    .ConfigureAwait(false));

            case CommandType.ScrollingCapture:
                ScrollingCaptureOptions? scrollingOptions = ResolveScrollingOptions(command, out string? scrollingError);
                if (scrollingOptions is null)
                {
                    return CommandResult.Fail(scrollingError ?? "Unsupported scrolling capture options.");
                }

                return CaptureCommandResult(await _coordinator
                    .CaptureScrollingWithResultAsync(command.Action, ResolveRegion(command), scrollingOptions, cancellationToken)
                    .ConfigureAwait(false));

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

            case CommandType.CaptureText:
                return await RouteOcrAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.ReadAloud:
                return await RouteReadAloudAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.AiActions:
                _presenter.ShowAiActions(AgentReviewLaunch.FromAutomation(command));
                return new CommandResult(
                    true,
                    "Opened the reviewed handoff. Source validation is shown there. Nothing is sent until you review and confirm it.");

            case CommandType.Dictation:
                DictationOperationResult dictation = await _dictation
                    .ToggleWithResultAsync(cancellationToken)
                    .ConfigureAwait(false);
                return dictation.Succeeded
                    ? new CommandResult(true, dictation.Message)
                    : CommandResult.Fail(dictation.Message);

            case CommandType.OpenHistory:
                _presenter.ShowHistory();
                return CommandResult.Ok;

            case CommandType.OpenClipboardHistory:
                _presenter.ShowClipboardHistory();
                return CommandResult.Ok;

            case CommandType.OpenContext:
                _presenter.ShowContext();
                return CommandResult.Ok;

            case CommandType.Quit:
                // Local-only (protocol launches are blocked upstream). A short
                // delay lets the pipe reply flush before the app tears down,
                // so 'octadock quit' gives a clean exit: SQLite closes its WAL
                // and Serilog flushes — unlike taskkill /F, which is how the
                // store got corrupted in the first place.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                        () => System.Windows.Application.Current?.Shutdown());
                }, CancellationToken.None);
                return new CommandResult(true, "Octadock is shutting down.");

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

            case CommandType.Activate:
                return await RouteActivateAsync(command, cancellationToken).ConfigureAwait(false);

            case CommandType.Unknown:
            default:
                return CommandResult.Fail($"Unknown or unsupported command '{command.Type}'.");
        }
    }

    internal static CommandResult CaptureCommandResult(Guid? captureId) => captureId is { } id
        ? CommandResult.Captured(id, "Capture completed.")
        : CommandResult.Fail("Capture was cancelled before an artifact was created.");

    /// <summary>
    /// Commands whose entire operation is gated can be refused at this boundary so
    /// CLI/IPC callers receive a truthful failure instead of "OK" after a downstream
    /// service safely no-ops. Toggle commands (record/dictate/read) are deliberately
    /// excluded because stopping an active operation must always remain possible.
    /// </summary>
    internal static GatedFeature? RequiredLicenseFeature(CommandType commandType) => commandType switch
    {
        CommandType.CaptureArea or
        CommandType.CapturePreviousArea or
        CommandType.CaptureFullscreen or
        CommandType.CaptureWindow or
        CommandType.SelfTimer or
        CommandType.ScrollingCapture => GatedFeature.Capture,
        CommandType.CaptureText => GatedFeature.Ocr,
        CommandType.OpenTextTools => GatedFeature.TextTools,
        _ => null,
    };

    private static string FeatureName(GatedFeature feature) => feature switch
    {
        GatedFeature.Capture => "Capturing",
        GatedFeature.Ocr => "Text recognition",
        GatedFeature.Annotate => "Starting an annotation",
        GatedFeature.TextTools => "Text tools",
        _ => "This feature",
    };

    /// <summary>
    /// <c>octadock://activate?key=…</c> (and the <c>activate</c> CLI verb): the deep-link
    /// accelerator for the primary key-entry UI (WS5, R2). Runs activation, announces the
    /// result via a notification (screen-reader friendly), and opens Account &amp; Billing so
    /// the resulting license state is visible. With no key it just opens Account for manual
    /// entry.
    /// </summary>
    private async Task<CommandResult> RouteActivateAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        string? key = command.Get("key");
        if (string.IsNullOrWhiteSpace(key))
        {
            _presenter.ShowSettings("account");
            return CommandResult.Fail("activate needs a 'key' (e.g. octadock://activate?key=OCTA-…). Opened Account & Billing to enter one.");
        }

        string? currentKey = _licenseGate.State.Entitlement?.LicenseKey;
        if (!string.IsNullOrWhiteSpace(currentKey) && LicenseKeyNormalizer.IsWellFormed(key))
        {
            string normalizedCurrent = LicenseKeyNormalizer.Normalize(currentKey);
            string normalizedIncoming = LicenseKeyNormalizer.Normalize(key);
            if (!string.Equals(normalizedCurrent, normalizedIncoming, StringComparison.OrdinalIgnoreCase))
            {
                var review = new ActivationReplacementReview(normalizedCurrent, normalizedIncoming);
                if (!_activationReplacementConfirmation.Confirm(review))
                {
                    return CommandResult.Fail(
                        "Activation cancelled. The existing Octadock license was not changed.");
                }
            }
        }

        ActivationResult result = await _activation.ActivateAsync(key, cancellationToken).ConfigureAwait(false);
        _notifications.Notify(
            result.Succeeded ? "Octadock activated" : "Octadock activation",
            result.Message,
            result.Succeeded ? NotificationKind.Success : NotificationKind.Warning,
            () => _presenter.ShowSettings("account"));

        // Bring up the Account surface so the outcome + license state is visible.
        _presenter.ShowSettings("account");
        return result.Succeeded ? CommandResult.Ok : CommandResult.Fail(result.Message);
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
        if (command.GetBool("explain"))
        {
            return CommandResult.Fail(
                "Read aloud currently speaks the selected text exactly. Automatic trusted summaries remain an internal prototype and are not exposed in the app yet.");
        }

        if (command.Region is not null && ResolveRegion(command) is PixelRect resolvedRegion)
        {
            command = WithRegion(command, resolvedRegion);
        }

        return await _readAloud.StartAsync(command, cancellationToken).ConfigureAwait(false);
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

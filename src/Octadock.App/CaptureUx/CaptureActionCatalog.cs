using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// Capture commands shared by the permanent Dock and the Capture Shelf.
/// Keeping one catalog and dispatcher prevents the two surfaces from drifting
/// into different labels or behavior as the high-frequency and secondary
/// actions are reorganized.
/// </summary>
internal enum CaptureAction
{
    Area = 0,
    Window,
    FullScreen,
    Record,
    AllMonitors,
    PreviousArea,
    SelfTimer,
    Scrolling,
    Ocr,
}

/// <summary>User-facing metadata for a capture command.</summary>
internal sealed record CaptureActionDefinition(
    CaptureAction Action,
    string Label,
    string AutomationName,
    string ToolTip);

/// <summary>
/// The product-level placement contract for capture commands. The Dock exposes
/// only the four commands that need to be one click away. The Shelf owns the
/// secondary capture tools, where they can be labeled and explained without a
/// hidden chevron or a dense floating menu.
/// </summary>
internal static class CaptureActionCatalog
{
    public static IReadOnlyList<CaptureActionDefinition> DockActions { get; } =
    [
        new(CaptureAction.Area, "Area", "Capture area", "Capture an area"),
        new(CaptureAction.Window, "Window", "Capture window", "Capture a window"),
        new(CaptureAction.FullScreen, "Full screen", "Capture full screen", "Capture the active screen"),
        new(CaptureAction.Record, "Record (Beta)", "Start screen recording (Beta)", "Start or stop screen recording (Beta)"),
    ];

    public static IReadOnlyList<CaptureActionDefinition> ShelfActions { get; } =
    [
        new(CaptureAction.AllMonitors, "All monitors", "Capture all monitors", "Capture every connected monitor"),
        new(CaptureAction.PreviousArea, "Previous area", "Capture previous area", "Capture the last selected area"),
        new(CaptureAction.SelfTimer, "Timer", "Capture with timer", "Capture an area after the configured timer"),
        new(
            CaptureAction.Scrolling,
            "Scrolling — manual vertical (Beta)",
            "Start manual vertical scrolling capture (Beta)",
            "Capture a region while you scroll vertically (Beta)"),
        new(
            CaptureAction.Ocr,
            "OCR",
            "Extract text from a region",
            "Extract text from a screen region locally"),
    ];

    public static CaptureActionDefinition Get(CaptureAction action)
        => DockActions.Concat(ShelfActions).Single(definition => definition.Action == action);
}

/// <summary>
/// Executes a catalog command. This is intentionally independent of Dock
/// visuals so the Capture Shelf can host the secondary commands without
/// duplicating orchestration or reaching back into the Dock window.
/// </summary>
internal interface ICaptureActionService
{
    Task ExecuteAsync(CaptureAction action, CancellationToken cancellationToken = default);
}

internal sealed class CaptureActionService : ICaptureActionService
{
    private readonly ICaptureCoordinator _capture;
    private readonly CaptureCoordinator _captureCoordinator;
    private readonly IOcrService _ocr;
    private readonly RecordingController _recording;
    private readonly ISettingsService _settings;

    public CaptureActionService(
        ICaptureCoordinator capture,
        CaptureCoordinator captureCoordinator,
        IOcrService ocr,
        RecordingController recording,
        ISettingsService settings)
    {
        _capture = capture;
        _captureCoordinator = captureCoordinator;
        _ocr = ocr;
        _recording = recording;
        _settings = settings;
    }

    public Task ExecuteAsync(CaptureAction action, CancellationToken cancellationToken = default)
    {
        PostCaptureAction postCaptureAction = _settings.Current.Capture.DefaultAction;
        return action switch
        {
            CaptureAction.Area => _capture.CaptureAreaAsync(postCaptureAction, cancellationToken),
            CaptureAction.Window => _capture.CaptureWindowAsync(postCaptureAction, cancellationToken),
            CaptureAction.FullScreen => _capture.CaptureFullscreenAsync(
                postCaptureAction,
                monitorToken: null,
                allMonitors: false,
                cancellationToken),
            CaptureAction.Record => _recording.ToggleAsync(cancellationToken),
            CaptureAction.AllMonitors => _capture.CaptureFullscreenAsync(
                postCaptureAction,
                monitorToken: null,
                allMonitors: true,
                cancellationToken),
            CaptureAction.PreviousArea => _capture.CapturePreviousAreaAsync(postCaptureAction, cancellationToken),
            CaptureAction.SelfTimer => _captureCoordinator.CaptureSelfTimerAsync(
                postCaptureAction,
                region: null,
                cancellationToken),
            CaptureAction.Scrolling => _capture.CaptureScrollingAsync(postCaptureAction, cancellationToken),
            CaptureAction.Ocr => _ocr.CaptureRegionTextAsync(
                _settings.Current.Ocr.OutputMode,
                language: null,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown capture action."),
        };
    }
}

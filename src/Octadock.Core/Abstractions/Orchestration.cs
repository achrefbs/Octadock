using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Models;

namespace Octadock.Core.Abstractions;

/// <summary>Result of dispatching an automation command.</summary>
public sealed record CommandResult(bool Success, string? Message = null)
{
    /// <summary>The durable capture identifier produced by a successful capture command.</summary>
    public Guid? CaptureId { get; init; }

    public static readonly CommandResult Ok = new(true);

    public static CommandResult Captured(Guid captureId, string? message = null) =>
        new(true, message) { CaptureId = captureId };

    public static CommandResult Fail(string message) => new(false, message);
}

/// <summary>
/// Executes a parsed <see cref="OctadockCommand"/> by routing it to the capture
/// coordinator, shelf, OCR, annotation, history or settings surfaces.
/// The protocol handler and CLI pipe both funnel through this.
/// </summary>
public interface ICommandDispatcher
{
    Task<CommandResult> DispatchAsync(OctadockCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level capture orchestration: hides Octadock UI, invokes the capture
/// engine, runs the persistence/thumbnail pipeline, and applies the requested
/// post-capture action. This is the seam the dispatcher and hotkeys call.
/// </summary>
public interface ICaptureCoordinator
{
    Task CaptureAreaAsync(PostCaptureAction action, CancellationToken cancellationToken = default);

    Task CapturePreviousAreaAsync(PostCaptureAction action, CancellationToken cancellationToken = default);

    Task CaptureFullscreenAsync(PostCaptureAction action, string? monitorToken, bool allMonitors, CancellationToken cancellationToken = default);

    Task CaptureWindowAsync(PostCaptureAction action, CancellationToken cancellationToken = default);

    Task CaptureScrollingAsync(PostCaptureAction action, CancellationToken cancellationToken = default);
}

/// <summary>The Capture Shelf surface.</summary>
public interface IShelfService
{
    /// <summary>Shows a capture on the shelf as the newest item, returning false if the UI could not be shown.</summary>
    Task<bool> ShowAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    /// <summary>Restores the most recently closed shelf item, if any.</summary>
    Task<bool> RestoreRecentlyClosedAsync(CancellationToken cancellationToken = default);

    /// <summary>Toggles a populated Shelf between visible and capsule-minimized.</summary>
    void ToggleVisibility();

    /// <summary>Closes every shelf item.</summary>
    void CloseAll();
}

/// <summary>
/// Launches the annotation editor. Capture image files registered in History
/// and capture-derived <c>.octadock</c> projects are the only accepted sources;
/// arbitrary files and clipboard images are not annotation entry points.
/// </summary>
public interface IAnnotationService
{
    /// <summary>
    /// Opens a registered capture (its existing project, else the original raster)
    /// and records an honest <c>Annotated</c> action when the editor opens.
    /// Returns false when nothing was opened (gated, missing, or corrupt source) —
    /// the refusal is always surfaced to the user.
    /// </summary>
    Task<bool> OpenAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a capture-derived <c>.octadock</c> project or an image file registered
    /// as an Octadock capture. Anything else is refused visibly and returns false.
    /// </summary>
    Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>OCR orchestration used by hotkeys and automation.</summary>
public interface IOcrService
{
    /// <summary>Prompts for a region, runs OCR, and returns the recognized text without copying it.</summary>
    Task<string> ExtractRegionTextAsync(OcrTextMode mode, string? language, CancellationToken cancellationToken = default);

    /// <summary>Runs OCR on a fixed screen region and returns the recognized text without copying it.</summary>
    Task<string> ExtractRegionTextAsync(PixelRect region, OcrTextMode mode, string? language, CancellationToken cancellationToken = default);

    /// <summary>Runs OCR on an image file and returns the recognized text without copying it.</summary>
    Task<string> ExtractFileTextAsync(string filePath, OcrTextMode mode, string? language, CancellationToken cancellationToken = default);

    /// <summary>Prompts for a region, runs OCR, and copies the text to the clipboard.</summary>
    Task CaptureRegionTextAsync(OcrTextMode mode, string? language, CancellationToken cancellationToken = default);

    /// <summary>Runs OCR on a fixed screen region and copies the text to the clipboard.</summary>
    Task CaptureRegionTextAsync(PixelRect region, OcrTextMode mode, string? language, CancellationToken cancellationToken = default);

    /// <summary>Runs OCR on an image file and copies the text to the clipboard.</summary>
    Task<string> ExtractFromFileAsync(string filePath, OcrTextMode mode, string? language, CancellationToken cancellationToken = default);
}

/// <summary>Presents the non-modal windows (history, settings, first-run).</summary>
public interface IWindowPresenter
{
    void ShowHistory();

    /// <summary>Shows the clipboard history window (falls back to Settings when the module is absent).</summary>
    void ShowClipboardHistory();

    /// <summary>Shows the text-transform toolbox window (falls back to Settings when the module is absent).</summary>
    void ShowTextTools();

    /// <summary>Shows the floating Context Stack window.</summary>
    void ShowContext();

    /// <summary>
    /// Shows the explicit, reviewed Agent Workspace. The method name is retained
    /// as a stable automation/binary compatibility contract for the historical
    /// AI Actions entry point.
    /// </summary>
    void ShowAiActions(OctadockCommand? launchCommand = null);

    void ShowSettings(string? tab = null);

    void ShowAllInOneHud(
        CaptureMode? mode = null,
        PixelRect? preloadedRegion = null,
        int? preloadedWidth = null,
        int? preloadedHeight = null);

    Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default);
}

/// <summary>Kind of user-facing notification.</summary>
public enum NotificationKind
{
    Info = 0,
    Success,
    Warning,
    Error,
}

/// <summary>Lightweight tray/toast notifications.</summary>
public interface INotificationService
{
    void Notify(
        string title,
        string message,
        NotificationKind kind = NotificationKind.Info,
        Action? clickAction = null);
}

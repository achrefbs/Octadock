using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Models;

namespace Octadock.Core.Abstractions;

/// <summary>Result of dispatching an automation command.</summary>
public sealed record CommandResult(bool Success, string? Message = null)
{
    public static readonly CommandResult Ok = new(true);

    public static CommandResult Fail(string message) => new(false, message);
}

/// <summary>
/// Executes a parsed <see cref="OctadockCommand"/> by routing it to the capture
/// coordinator, shelf, pins, OCR, annotation, history or settings surfaces.
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

    /// <summary>Adds an external image file to the shelf and history.</summary>
    Task AddExternalFileAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>The Capture Shelf surface.</summary>
public interface IShelfService
{
    /// <summary>Shows a capture on the shelf as the newest item, returning false if the UI could not be shown.</summary>
    Task<bool> ShowAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    /// <summary>Restores the most recently closed shelf item, if any.</summary>
    Task<bool> RestoreRecentlyClosedAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes every shelf item.</summary>
    void CloseAll();
}

/// <summary>The floating-pins surface.</summary>
public interface IPinService
{
    Task PinCaptureAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    Task PinImageFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task PinFromClipboardAsync(CancellationToken cancellationToken = default);

    /// <summary>Restores pins persisted from a previous session.</summary>
    Task RestorePersistedPinsAsync(CancellationToken cancellationToken = default);

    void HideAll();

    void CloseAll();
}

/// <summary>Launches the annotation editor from various sources.</summary>
public interface IAnnotationService
{
    Task OpenAsync(CaptureRecord record, CancellationToken cancellationToken = default);

    Task OpenFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task OpenFromClipboardAsync(CancellationToken cancellationToken = default);
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

    void ShowAiSessions();

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

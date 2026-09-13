using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Io;
using Octadock.Core.Ocr;
using Octadock.Core.Settings;

namespace Octadock.App.Services;

/// <summary>
/// <see cref="IOcrService"/>: captures either a prompted or fixed region, resolves
/// an OCR provider through <see cref="IOcrProviderFactory"/>, copies the
/// recognized text to the clipboard and notifies. Unavailable OCR is reported
/// clearly with a fallback message per the spec. All work happens off the UI
/// thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class OcrService : IOcrService
{
    /// <summary>
    /// A compressed image above this size is not read into the OCR decoder. This is
    /// intentionally independent of the provider so every entry point has the same
    /// bounded-I/O behavior.
    /// </summary>
    internal const long MaxOcrFileBytes = 64L * 1024 * 1024;

    private readonly IOcrProviderFactory _providerFactory;
    private readonly ICaptureEngine _captureEngine;
    private readonly IMonitorService _monitors;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly CaptureGate _captureGate;
    private readonly IServiceProvider _services;
    private readonly OcrHistoryRecorder _history;
    private readonly ILogger<OcrService> _logger;

    /// <summary>Creates the OCR service.</summary>
    public OcrService(
        IOcrProviderFactory providerFactory,
        ICaptureEngine captureEngine,
        IMonitorService monitors,
        IClipboardService clipboard,
        INotificationService notifications,
        ISettingsService settings,
        CaptureGate captureGate,
        IServiceProvider services,
        OcrHistoryRecorder history,
        ILogger<OcrService> logger)
    {
        _providerFactory = providerFactory;
        _captureEngine = captureEngine;
        _monitors = monitors;
        _clipboard = clipboard;
        _notifications = notifications;
        _settings = settings;
        _captureGate = captureGate;
        _services = services;
        _history = history;
        _logger = logger;
    }

    private OcrSettings OcrSettings => _settings.Current.Ocr;

    /// <inheritdoc />
    public async Task<string> ExtractRegionTextAsync(OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        OcrAttempt attempt = await RecognizeRegionAsync(mode, language, cancellationToken).ConfigureAwait(false);
        return attempt.Result.Text;
    }

    /// <inheritdoc />
    public async Task<string> ExtractRegionTextAsync(PixelRect region, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        OcrAttempt attempt = await RecognizeRegionAsync(region, mode, language, cancellationToken).ConfigureAwait(false);
        return attempt.Result.Text;
    }

    /// <inheritdoc />
    public async Task<string> ExtractFileTextAsync(string filePath, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        OcrAttempt attempt = await RecognizeFileAsync(filePath, mode, language, cancellationToken).ConfigureAwait(false);
        return attempt.Result.Text;
    }

    /// <inheritdoc />
    public async Task CaptureRegionTextAsync(OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        OcrAttempt attempt = await RecognizeRegionAsync(mode, language, cancellationToken).ConfigureAwait(false);
        if (!attempt.Completed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DeliverResult(attempt.Result, attempt.Provider);
        await RecordHistoryAsync(attempt.Frame, attempt.Result, mode, language, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CaptureRegionTextAsync(PixelRect region, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        OcrAttempt attempt = await RecognizeRegionAsync(region, mode, language, cancellationToken).ConfigureAwait(false);
        if (!attempt.Completed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DeliverResult(attempt.Result, attempt.Provider);
        await RecordHistoryAsync(attempt.Frame, attempt.Result, mode, language, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ExtractFromFileAsync(string filePath, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        OcrAttempt attempt = await RecognizeFileAsync(filePath, mode, language, cancellationToken).ConfigureAwait(false);
        if (!attempt.Completed)
        {
            return string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DeliverResult(attempt.Result, attempt.Provider);
        return attempt.Result.Text;
    }

    private async Task<OcrAttempt> RecognizeFileAsync(
        string filePath,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Trial/license gate (WS5): OCR is new compute, blocked post-expiry.


        if (!TryResolveInputFile(filePath, out string fullPath))
        {
            return OcrAttempt.NotRun;
        }

        IOcrProvider? provider = ResolveProviderOrNotify();
        if (provider is null)
        {
            return OcrAttempt.NotRun;
        }

        try
        {
            OcrResult result = await provider
                .RecognizeAsync(fullPath, mode, ResolveLanguage(language), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return OcrAttempt.Succeeded(result, frame: null, provider.Kind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            NotifyRecognitionFailed(ex, "image file");
            return OcrAttempt.NotRun;
        }
    }

    private async Task<OcrAttempt> RecognizeRegionAsync(
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Trial/license gate (WS5): OCR is new compute, blocked post-expiry.


        IOcrProvider? provider = ResolveProviderOrNotify();
        if (provider is null)
        {
            return OcrAttempt.NotRun;
        }

        // H2: selection + grab must run under the same gate the capture
        // coordinator uses, so an OCR flow can never overlap an active capture
        // (fighting over the overlay, or grabbing overlay pixels).
        if (!await _captureGate.TryEnterImmediatelyAsync(cancellationToken).ConfigureAwait(false))
        {
            _notifications.Notify("Capture queued", "Another capture is already active.", NotificationKind.Info);
            await _captureGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        }

        CapturedFrame frame;
        try
        {
            PixelRect region;
            if (_services.GetService(typeof(IRegionSelectionService)) is IRegionSelectionService selector)
            {
                RegionSelection selection = await selector.SelectAreaAsync(cancellationToken).ConfigureAwait(false);
                if (!selection.Confirmed || selection.Region.IsEmpty)
                {
                    return OcrAttempt.NotRun;
                }

                selector.HideAll();
                await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                region = selection.Region;
            }
            else
            {
                // Fallback: OCR the active monitor.
                region = _monitors.GetActiveMonitor().Bounds;
            }

            var request = new AreaCaptureRequest { Region = region, IncludeCursor = false };
            frame = await _captureEngine.CaptureAreaAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            NotifyCaptureFailed(ex);
            return OcrAttempt.NotRun;
        }

        finally
        {
            _captureGate.Exit();
        }

        // Recognition runs on the frozen frame, safely outside the gate.
        return await RecognizeFrameAsync(provider, frame, mode, language, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OcrAttempt> RecognizeRegionAsync(
        PixelRect region,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Trial/license gate (WS5): OCR is new compute, blocked post-expiry.


        PixelRect target;
        try
        {
            target = region.Normalized();
            PixelRect desktop = _monitors.VirtualDesktopBounds;
            if (!desktop.IsEmpty)
            {
                target = target.Intersect(desktop);
            }
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            _notifications.Notify("OCR", "The requested OCR region is not valid.", NotificationKind.Warning);
            return OcrAttempt.NotRun;
        }

        if (target.IsEmpty)
        {
            _notifications.Notify("OCR", "The requested OCR region is empty.", NotificationKind.Warning);
            return OcrAttempt.NotRun;
        }

        IOcrProvider? provider = ResolveProviderOrNotify();
        if (provider is null)
        {
            return OcrAttempt.NotRun;
        }

        // H2: selection + grab must run under the same gate the capture
        // coordinator uses, so an OCR flow can never overlap an active capture
        // (fighting over the overlay, or grabbing overlay pixels).
        if (!await _captureGate.TryEnterImmediatelyAsync(cancellationToken).ConfigureAwait(false))
        {
            _notifications.Notify("Capture queued", "Another capture is already active.", NotificationKind.Info);
            await _captureGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        }

        CapturedFrame frame;
        try
        {
            var request = new AreaCaptureRequest { Region = target, IncludeCursor = false };
            frame = await _captureEngine.CaptureAreaAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            NotifyCaptureFailed(ex);
            return OcrAttempt.NotRun;
        }

        finally
        {
            _captureGate.Exit();
        }

        // Recognition runs on the frozen frame, safely outside the gate.
        return await RecognizeFrameAsync(provider, frame, mode, language, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OcrAttempt> RecognizeFrameAsync(
        IOcrProvider provider,
        CapturedFrame frame,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        try
        {
            OcrResult recognized = await provider
                .RecognizeAsync(frame, mode, ResolveLanguage(language), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return OcrAttempt.Succeeded(recognized, frame, provider.Kind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            NotifyRecognitionFailed(ex, "captured region");
            return OcrAttempt.NotRun;
        }
    }

    private async Task RecordHistoryAsync(
        CapturedFrame? frame,
        OcrResult result,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        if (frame is null || result.IsEmpty)
        {
            return;
        }

        await _history.RecordAsync(frame, result.Text, mode, ResolveLanguage(language), cancellationToken)
            .ConfigureAwait(false);
    }

    private IOcrProvider? ResolveProviderOrNotify()
    {
        IOcrProvider? provider = _providerFactory.Resolve(OcrSettings.Provider);
        if (provider is not null)
        {
            OcrAvailability availability = provider.CheckAvailability();
            if (availability.IsAvailable)
            {
                return provider;
            }

            _notifications.Notify(
                "OCR unavailable",
                availability.Reason ?? "The selected text recognition engine is not available on this PC.",
                NotificationKind.Warning);
            return null;
        }

        _notifications.Notify(
            "OCR unavailable",
            "No text recognition engine is available. Windows OCR may require a language pack or package identity.",
            NotificationKind.Warning);
        return null;
    }

    private bool TryResolveInputFile(string filePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _notifications.Notify("OCR", "Choose a local image file to recognize.", NotificationKind.Warning);
            return false;
        }

        // Run the string-only UNC guard before File.Exists/FileInfo: touching a
        // network path can initiate authentication and leak the user's credentials.
        if (PathSafety.IsUncPath(filePath))
        {
            _notifications.Notify(
                "OCR",
                "Network or invalid paths aren't read. Copy the image to this PC first.",
                NotificationKind.Warning);
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(filePath);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                _notifications.Notify("OCR", "The image file could not be found.", NotificationKind.Warning);
                return false;
            }

            if (info.Length == 0)
            {
                _notifications.Notify("OCR", "The image file is empty.", NotificationKind.Warning);
                return false;
            }

            if (info.Length > MaxOcrFileBytes)
            {
                _notifications.Notify(
                    "OCR",
                    "The image is too large to recognize safely (64 MB maximum).",
                    NotificationKind.Warning);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException or
                UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            LogInvalidOcrFile(_logger, ex);
            _notifications.Notify("OCR", "The image file could not be read safely.", NotificationKind.Warning);
            fullPath = string.Empty;
            return false;
        }
    }

    private void DeliverResult(OcrResult result, OcrProvider? provider)
    {
        if (result.IsEmpty)
        {
            _notifications.Notify(
                "OCR",
                provider is { } kind
                    ? $"{ProviderName(kind)} found no text. Recognition stayed on this PC."
                    : "No text was recognized.",
                NotificationKind.Info);
            return;
        }

        try
        {
            _clipboard.SetText(result.Text);
            _notifications.Notify(
                "Text copied",
                provider is { } kind
                    ? $"Recognized locally with {ProviderName(kind)} and copied to your clipboard."
                    : "Recognized text is on your clipboard.",
                NotificationKind.Success);
        }
        catch (Exception ex)
        {
            LogFailedToCopyOcrText(_logger, ex);
            _notifications.Notify("Copy failed", "Text was recognized, but could not be copied.", NotificationKind.Error);
        }
    }

    private void NotifyCaptureFailed(Exception exception)
    {
        LogOcrCaptureFailed(_logger, exception);
        _notifications.Notify(
            "OCR failed",
            "Octadock couldn't capture that region. Nothing was copied.",
            NotificationKind.Error);
    }

    private void NotifyRecognitionFailed(Exception exception, string source)
    {
        LogOcrRecognitionFailed(_logger, exception, source);
        _notifications.Notify(
            "OCR failed",
            "Octadock couldn't read that image. It may be damaged, unsupported, or too large. Nothing was copied.",
            NotificationKind.Error);
    }

    private static string ProviderName(OcrProvider provider) => provider switch
    {
        OcrProvider.WindowsMediaOcr => "Windows OCR",
        OcrProvider.WindowsAiTextRecognition => "Windows AI Text Recognition",
        OcrProvider.Tesseract => "Tesseract OCR",
        _ => "the selected OCR engine",
    };

    private string? ResolveLanguage(string? explicitLanguage)
    {
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return explicitLanguage;
        }

        return string.IsNullOrWhiteSpace(OcrSettings.PreferredLanguage) ? null : OcrSettings.PreferredLanguage;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to copy OCR text to the clipboard.")]
    private static partial void LogFailedToCopyOcrText(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "OCR recognition failed for the {Source}.")]
    private static partial void LogOcrRecognitionFailed(ILogger logger, Exception exception, string source);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Capturing the OCR source region failed.")]
    private static partial void LogOcrCaptureFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Rejected an unreadable OCR file path.")]
    private static partial void LogInvalidOcrFile(ILogger logger, Exception exception);

    private readonly record struct OcrAttempt(
        bool Completed,
        OcrResult Result,
        CapturedFrame? Frame,
        OcrProvider? Provider)
    {
        public static OcrAttempt NotRun => new(false, OcrResult.Empty, null, null);

        public static OcrAttempt Succeeded(OcrResult result, CapturedFrame? frame, OcrProvider provider)
            => new(true, result ?? OcrResult.Empty, frame, provider);
    }
}

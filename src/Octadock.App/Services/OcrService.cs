using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
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
        (OcrResult result, _) = await RecognizeRegionAsync(mode, language, cancellationToken).ConfigureAwait(false);
        return result.Text;
    }

    /// <inheritdoc />
    public async Task<string> ExtractRegionTextAsync(PixelRect region, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        (OcrResult result, _) = await RecognizeRegionAsync(region, mode, language, cancellationToken).ConfigureAwait(false);
        return result.Text;
    }

    /// <inheritdoc />
    public async Task<string> ExtractFileTextAsync(string filePath, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            _notifications.Notify("OCR", "The image file could not be found.", NotificationKind.Warning);
            return string.Empty;
        }

        IOcrProvider? provider = ResolveProviderOrNotify();
        if (provider is null)
        {
            return string.Empty;
        }

        OcrResult result = await provider.RecognizeAsync(filePath, mode, ResolveLanguage(language), cancellationToken).ConfigureAwait(false);
        return result.Text;
    }

    /// <inheritdoc />
    public async Task CaptureRegionTextAsync(OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        (OcrResult result, CapturedFrame? frame) = await RecognizeRegionAsync(mode, language, cancellationToken).ConfigureAwait(false);
        DeliverResult(result);
        await RecordHistoryAsync(frame, result, mode, language, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CaptureRegionTextAsync(PixelRect region, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        (OcrResult result, CapturedFrame? frame) = await RecognizeRegionAsync(region, mode, language, cancellationToken).ConfigureAwait(false);
        DeliverResult(result);
        await RecordHistoryAsync(frame, result, mode, language, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ExtractFromFileAsync(string filePath, OcrTextMode mode, string? language, CancellationToken cancellationToken = default)
    {
        string text = await ExtractFileTextAsync(filePath, mode, language, cancellationToken).ConfigureAwait(false);
        var result = new OcrResult { Text = text };
        DeliverResult(result);
        return text;
    }

    private async Task<(OcrResult Result, CapturedFrame? Frame)> RecognizeRegionAsync(
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        IOcrProvider? provider = ResolveProviderOrNotify();
        if (provider is null)
        {
            return (OcrResult.Empty, null);
        }

        // H2: the selection + grab must run under the same gate the capture
        // coordinator uses, so an OCR region flow can never overlap an active
        // capture (fighting over the overlay, or grabbing overlay pixels).
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
                    return (OcrResult.Empty, null);
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
        finally
        {
            _captureGate.Exit();
        }

        // Recognition runs on the frozen frame, safely outside the gate.
        OcrResult recognized = await provider.RecognizeAsync(frame, mode, ResolveLanguage(language), cancellationToken).ConfigureAwait(false);
        return (recognized, frame);
    }

    private async Task<(OcrResult Result, CapturedFrame? Frame)> RecognizeRegionAsync(
        PixelRect region,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken)
    {
        PixelRect target = region.Normalized();
        if (target.IsEmpty)
        {
            _notifications.Notify("OCR", "The requested OCR region is empty.", NotificationKind.Warning);
            return (OcrResult.Empty, null);
        }

        IOcrProvider? provider = ResolveProviderOrNotify();
        if (provider is null)
        {
            return (OcrResult.Empty, null);
        }

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
        finally
        {
            _captureGate.Exit();
        }

        // Recognition runs on the frozen frame, safely outside the gate.
        OcrResult recognized = await provider.RecognizeAsync(frame, mode, ResolveLanguage(language), cancellationToken).ConfigureAwait(false);
        return (recognized, frame);
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

    private void DeliverResult(OcrResult result)
    {
        if (result.IsEmpty)
        {
            _notifications.Notify("OCR", "No text was recognized in the selection.", NotificationKind.Info);
            return;
        }

        try
        {
            _clipboard.SetText(result.Text);
            _notifications.Notify("Text copied", "Recognized text is on your clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            LogFailedToCopyOcrText(_logger, ex);
            _notifications.Notify("Copy failed", "Text was recognized, but could not be copied.", NotificationKind.Error);
        }
    }

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
}

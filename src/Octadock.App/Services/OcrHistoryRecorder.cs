using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Imaging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Services;

/// <summary>
/// Persists an OCR grab into local history: the source frame becomes an
/// <see cref="CaptureType.OcrSource"/> capture (PNG + thumbnail) and the
/// recognized text is stored on an <see cref="ActionType.OcrExtracted"/> action
/// as JSON metadata, so extractions are recoverable from the History window.
/// Failures are logged and never break the copy-to-clipboard flow.
/// </summary>
public sealed partial class OcrHistoryRecorder
{
    /// <summary>Stored text is capped so a pathological OCR result cannot bloat the DB.</summary>
    internal const int MaxStoredTextLength = 100_000;

    private readonly ICaptureRepository _captures;
    private readonly IActionRepository _actions;
    private readonly IImageEncoder _encoder;
    private readonly IThumbnailGenerator _thumbnails;
    private readonly IStoragePaths _paths;
    private readonly ISettingsService _settings;
    private readonly ILogger<OcrHistoryRecorder> _logger;

    /// <summary>Creates the recorder.</summary>
    public OcrHistoryRecorder(
        ICaptureRepository captures,
        IActionRepository actions,
        IImageEncoder encoder,
        IThumbnailGenerator thumbnails,
        IStoragePaths paths,
        ISettingsService settings,
        ILogger<OcrHistoryRecorder> logger)
    {
        _captures = captures;
        _actions = actions;
        _encoder = encoder;
        _thumbnails = thumbnails;
        _paths = paths;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Records the OCR source frame and extracted text into history. No-op when
    /// history is disabled or the text is empty.
    /// </summary>
    public async Task RecordAsync(
        CapturedFrame frame,
        string text,
        OcrTextMode mode,
        string? language,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.History.Enabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            DateTimeOffset now = frame.CapturedAt == default ? DateTimeOffset.Now : frame.CapturedAt;
            var id = Guid.NewGuid();

            string relative = _paths.BuildCaptureRelativePath(id, now, ".png");
            await _encoder.EncodeToFileAsync(
                frame,
                _paths.ToAbsolute(relative),
                new EncodeOptions { Format = ExportImageFormat.Png },
                cancellationToken).ConfigureAwait(false);

            string? thumbRelative = _paths.BuildThumbnailRelativePath(id);
            try
            {
                EncodedImage thumbnail = _thumbnails.Generate(frame);
                string thumbAbsolute = _paths.ToAbsolute(thumbRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(thumbAbsolute)!);
                await File.WriteAllBytesAsync(thumbAbsolute, thumbnail.Bytes.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogThumbnailFailed(ex);
                thumbRelative = null;
            }

            var record = new CaptureRecord
            {
                Id = id,
                Type = CaptureType.OcrSource,
                CreatedAt = now,
                Source = CaptureSource.Empty,
                MonitorId = frame.MonitorId,
                PixelWidth = frame.Width,
                PixelHeight = frame.Height,
                DpiScale = frame.DpiScale,
                OriginalPath = relative,
                ThumbnailPath = thumbRelative,
            };
            await _captures.AddAsync(record, cancellationToken).ConfigureAwait(false);

            await _actions.AddAsync(
                new ActionRecord
                {
                    Id = Guid.NewGuid(),
                    CaptureId = id,
                    ActionType = ActionType.OcrExtracted,
                    CreatedAt = now,
                    Destination = "clipboard",
                    MetadataJson = BuildMetadataJson(text, mode, language),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRecordFailed(ex);
        }
    }

    /// <summary>Serializes the OCR payload stored on the action row.</summary>
    internal static string BuildMetadataJson(string text, OcrTextMode mode, string? language)
    {
        string stored = text.Length > MaxStoredTextLength ? text[..MaxStoredTextLength] : text;
        return JsonSerializer.Serialize(new OcrActionMetadata
        {
            Text = stored,
            Mode = mode.ToString(),
            Language = string.IsNullOrWhiteSpace(language) ? null : language,
            Truncated = text.Length > MaxStoredTextLength ? true : null,
        });
    }

    /// <summary>Reads the extracted text back out of an action's metadata JSON.</summary>
    internal static string? TryReadExtractedText(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            OcrActionMetadata? metadata = JsonSerializer.Deserialize<OcrActionMetadata>(metadataJson);
            return string.IsNullOrEmpty(metadata?.Text) ? null : metadata.Text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The JSON payload stored on <see cref="ActionType.OcrExtracted"/> actions.</summary>
    internal sealed record OcrActionMetadata
    {
        public string Text { get; init; } = string.Empty;

        public string? Mode { get; init; }

        public string? Language { get; init; }

        public bool? Truncated { get; init; }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Recording the OCR extraction into history failed.")]
    private partial void LogRecordFailed(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Could not generate the OCR source thumbnail.")]
    private partial void LogThumbnailFailed(Exception exception);
}

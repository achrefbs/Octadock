using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;

namespace Octadock.App.Services;

/// <summary>
/// Orchestrates "read this for me": source text extraction, AI explanation,
/// ElevenLabs synthesis, and local playback.
/// </summary>
public sealed partial class ReadAloudService
{
    private const int MaxFileCharacters = 240_000;

    private readonly ITextExplanationProvider _explainer;
    private readonly ITextToSpeechProvider _tts;
    private readonly IAudioPlaybackService _audio;
    private readonly IClipboardService _clipboard;
    private readonly IOcrService _ocr;
    private readonly INotificationService _notifications;
    private readonly ILogger<ReadAloudService> _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _currentCts;
    private Task? _currentTask;
    private Guid _currentRunId;

    /// <summary>Creates the read-aloud orchestrator.</summary>
    public ReadAloudService(
        ITextExplanationProvider explainer,
        ITextToSpeechProvider tts,
        IAudioPlaybackService audio,
        IClipboardService clipboard,
        IOcrService ocr,
        INotificationService notifications,
        ILogger<ReadAloudService> logger)
    {
        _explainer = explainer ?? throw new ArgumentNullException(nameof(explainer));
        _tts = tts ?? throw new ArgumentNullException(nameof(tts));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts a background read-aloud run, or stops the current run when requested.</summary>
    public Task<CommandResult> StartAsync(OctadockCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.GetBool("stop"))
        {
            Stop();
            return Task.FromResult(new CommandResult(true, "Stopped read aloud."));
        }

        if (!_explainer.IsAvailable)
        {
            return Task.FromResult(CommandResult.Fail("Codex CLI or Claude CLI was not found."));
        }

        if (!_tts.IsAvailable)
        {
            return Task.FromResult(CommandResult.Fail(
                _tts.UnavailableReason ?? "ElevenLabs text-to-speech is not configured."));
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            StopNoLock();
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Guid runId = Guid.NewGuid();
            _currentRunId = runId;
            _currentCts = cts;
            _currentTask = Task.Run(() => RunAsync(runId, command, cts.Token), CancellationToken.None);
        }

        _notifications.Notify("Read aloud", "Preparing the explanation.", NotificationKind.Info);
        return Task.FromResult(new CommandResult(true, "Started read aloud."));
    }

    /// <summary>Stops the active read-aloud run, if any.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopNoLock();
        }

        _notifications.Notify("Read aloud", "Stopped.", NotificationKind.Info);
    }

    private void StopNoLock()
    {
        try
        {
            _currentCts?.Cancel();
        }
        catch
        {
            // Best effort.
        }

        _audio.Stop();
        _currentCts?.Dispose();
        _currentCts = null;
        _currentTask = null;
        _currentRunId = Guid.Empty;
    }

    private async Task RunAsync(Guid runId, OctadockCommand command, CancellationToken cancellationToken)
    {
        try
        {
            TextSource source = await ResolveSourceAsync(command, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(source.Text))
            {
                _notifications.Notify("Read aloud", "No readable text was found.", NotificationKind.Info);
                return;
            }

            _notifications.Notify("Read aloud", "Summarizing with local AI.", NotificationKind.Info);
            TextExplanationResult explanation = await _explainer.ExplainAsync(
                new TextExplanationRequest
                {
                    Text = source.Text,
                    SourceName = source.Label,
                    Style = command.Get("style") ?? "explain",
                    Length = command.Get("length") ?? "medium",
                    ProviderPreference = command.Get("provider"),
                },
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(explanation.Text))
            {
                _notifications.Notify("Read aloud", "The AI explainer returned an empty response.", NotificationKind.Warning);
                return;
            }

            _notifications.Notify("Read aloud", "Generating voice with ElevenLabs.", NotificationKind.Info);
            SynthesizedSpeech speech = await _tts.SynthesizeAsync(
                new TextToSpeechRequest
                {
                    Text = explanation.Text,
                    VoiceId = command.Get("voice-id") ?? command.Get("voiceid") ?? command.Get("voice"),
                    ModelId = command.Get("model-id") ?? command.Get("modelid") ?? command.Get("model"),
                },
                cancellationToken).ConfigureAwait(false);

            _notifications.Notify("Read aloud", $"Speaking explanation from {explanation.ProviderId}.", NotificationKind.Success);
            await _audio.PlayAsync(speech.FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogReadAloudRunCancelled(_logger);
        }
        catch (Exception ex)
        {
            LogReadAloudRunFailed(_logger, ex);
            _notifications.Notify("Read aloud failed", ex.Message, NotificationKind.Error);
        }
        finally
        {
            lock (_gate)
            {
                if (runId == _currentRunId)
                {
                    _currentCts?.Dispose();
                    _currentCts = null;
                    _currentTask = null;
                    _currentRunId = Guid.Empty;
                }
            }
        }
    }

    private async Task<TextSource> ResolveSourceAsync(OctadockCommand command, CancellationToken cancellationToken)
    {
        string? literalText = command.Get("text");
        if (!string.IsNullOrWhiteSpace(literalText))
        {
            return new TextSource(literalText, "provided text");
        }

        if (!string.IsNullOrWhiteSpace(command.FilePath))
        {
            return await ReadFileSourceAsync(command.FilePath, command, cancellationToken).ConfigureAwait(false);
        }

        if (command.GetBool("clipboard"))
        {
            string? clipboardText = _clipboard.TryGetText();
            return new TextSource(clipboardText ?? string.Empty, "clipboard");
        }

        OcrTextMode mode = command.GetEnum<OcrTextMode>("mode")
            ?? (command.GetBool("linebreaks") ? OcrTextMode.Lines : OcrTextMode.Lines);
        string? language = command.Get("language");

        if (command.Region is PixelRect region)
        {
            string text = await _ocr.ExtractRegionTextAsync(region, mode, language, cancellationToken).ConfigureAwait(false);
            return new TextSource(text, "screen selection");
        }

        string prompted = await _ocr.ExtractRegionTextAsync(mode, language, cancellationToken).ConfigureAwait(false);
        return new TextSource(prompted, "screen selection");
    }

    private async Task<TextSource> ReadFileSourceAsync(
        string requestedPath,
        OctadockCommand command,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested file could not be found.", fullPath);
        }

        string label = Path.GetFileName(fullPath);
        if (ImageFileSupport.IsSupportedRasterPath(fullPath))
        {
            OcrTextMode mode = command.GetEnum<OcrTextMode>("mode") ?? OcrTextMode.Lines;
            string imageText = await _ocr.ExtractFileTextAsync(
                fullPath,
                mode,
                command.Get("language"),
                cancellationToken).ConfigureAwait(false);
            return new TextSource(imageText, label);
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var buffer = new char[MaxFileCharacters + 1];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await reader.ReadAsync(
                buffer.AsMemory(read, buffer.Length - read),
                cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        bool truncated = read > MaxFileCharacters;
        string fileText = new(buffer, 0, truncated ? MaxFileCharacters : read);
        if (LooksBinary(fileText))
        {
            throw new InvalidOperationException("This file does not look like readable text. Try OCR on a region instead.");
        }

        if (truncated)
        {
            label += " (first 240K characters)";
        }

        return new TextSource(fileText, label);
    }

    private static bool LooksBinary(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        int controlCount = 0;
        int inspected = Math.Min(text.Length, 4096);
        for (int i = 0; i < inspected; i++)
        {
            char c = text[i];
            if (c == '\0')
            {
                return true;
            }

            if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
            {
                controlCount++;
            }
        }

        return controlCount > inspected / 20;
    }

    private sealed record TextSource(string Text, string Label);

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Read-aloud run cancelled.")]
    private static partial void LogReadAloudRunCancelled(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Read-aloud run failed.")]
    private static partial void LogReadAloudRunFailed(ILogger logger, Exception exception);
}

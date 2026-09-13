using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Imaging;
using Octadock.App.Reading;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Reading;
using Octadock.Core.Settings;

namespace Octadock.App.Services;

/// <summary>
/// Orchestrates "read this for me": extract text (selection OCR, clipboard,
/// file, or literal), then speak it exactly as written through the configured
/// voice with sentence prefetch. A playback pill offers pause/resume and stop.
/// </summary>
public sealed partial class ReadAloudService
{
    private const int MaxFileCharacters = 240_000;

    private readonly ITextToSpeechProvider[] _ttsProviders;
    private readonly IAudioPlaybackService _audio;
    private readonly IClipboardService _clipboard;
    private readonly IOcrService _ocr;
    private readonly INotificationService _notifications;
    private readonly IMonitorService _monitors;
    private readonly ISettingsService _settings;
    private readonly ILogger<ReadAloudService> _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _currentCts;
    private Task? _currentTask;
    private Guid _currentRunId;
    private ReadingPill? _pill;
    private DispatcherTimer? _pillTimer;
    private readonly Stopwatch _spokenTime = new();

    /// <summary>Creates the read-aloud orchestrator.</summary>
    public ReadAloudService(
        IEnumerable<ITextToSpeechProvider> ttsProviders,
        IAudioPlaybackService audio,
        IClipboardService clipboard,
        IOcrService ocr,
        INotificationService notifications,
        IMonitorService monitors,
        ISettingsService settings,
        ILogger<ReadAloudService> logger)
    {
        _ttsProviders = (ttsProviders ?? throw new ArgumentNullException(nameof(ttsProviders))).ToArray();
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

        // Trial/license gate (WS5): starting a new read-aloud run is blocked
        // post-expiry because it starts new TTS/compute.


        if (WantsExplanation(command))
        {
            return Task.FromResult(CommandResult.Fail(
                "Read aloud currently speaks the selected text exactly. Automatic trusted summaries are still an internal prototype."));
        }

        ITextToSpeechProvider? tts = ResolveTts(command);
        if (tts is null)
        {
            return Task.FromResult(CommandResult.Fail("No text-to-speech voice is available."));
        }

        CancellationTokenSource cts;
        lock (_gate)
        {
            StopNoLock();
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Guid runId = Guid.NewGuid();
            _currentRunId = runId;
            _currentCts = cts;
            _currentTask = Task.Run(() => RunAsync(runId, command, tts, cts.Token), CancellationToken.None);
        }

        return Task.FromResult(new CommandResult(true, "Started read aloud."));
    }

    /// <summary>Stops the active read-aloud run, if any.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopNoLock();
        }
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
        ClosePill();
    }

    private async Task RunAsync(
        Guid runId,
        OctadockCommand command,
        ITextToSpeechProvider tts,
        CancellationToken cancellationToken)
    {
        try
        {
            TextSource source = await ResolveSourceAsync(command, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(source.Text))
            {
                _notifications.Notify("Read aloud", "No readable text was found.", NotificationKind.Info);
                return;
            }

            await SpeakChunkedAsync(source.Text, source.Label, command, tts, cancellationToken).ConfigureAwait(false);
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

            ClosePill();
        }
    }

    /// <summary>
    /// Speaks the text sentence-chunk by sentence-chunk: the next chunk is
    /// synthesized while the current one plays, so audio starts after only the
    /// first (small) synthesis and never gaps on chunk boundaries.
    /// </summary>
    private async Task SpeakChunkedAsync(
        string text,
        string label,
        OctadockCommand command,
        ITextToSpeechProvider tts,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> chunks = SentenceChunker.Split(text);
        if (chunks.Count == 0)
        {
            return;
        }

        ReadSettings read = _settings.Current.Read;
        double rate = Math.Clamp(
            double.TryParse(command.Get("rate"), out double requestedRate) ? requestedRate : read.Rate,
            0.5,
            3.0);
        string? voice = command.Get("voice-id") ?? command.Get("voiceid") ?? command.Get("voice");
        if (string.IsNullOrWhiteSpace(voice))
        {
            voice = string.IsNullOrWhiteSpace(read.Voice) ? null : read.Voice;
        }

        ShowPill(label);
        _spokenTime.Restart();

        Task<SynthesizedSpeech> next = SynthesizeAsync(tts, chunks[0], voice, rate, command, cancellationToken);
        for (int i = 0; i < chunks.Count; i++)
        {
            SynthesizedSpeech current = await next.ConfigureAwait(false);
            if (i + 1 < chunks.Count)
            {
                next = SynthesizeAsync(tts, chunks[i + 1], voice, rate, command, cancellationToken);
            }

            try
            {
                await _audio.PlayAsync(current.FilePath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(current.FilePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        _spokenTime.Stop();
    }

    private static Task<SynthesizedSpeech> SynthesizeAsync(
        ITextToSpeechProvider tts,
        string chunk,
        string? voice,
        double rate,
        OctadockCommand command,
        CancellationToken cancellationToken)
        => tts.SynthesizeAsync(
            new TextToSpeechRequest
            {
                Text = chunk,
                VoiceId = voice,
                ModelId = command.Get("model-id") ?? command.Get("modelid") ?? command.Get("model"),
                Rate = rate,
            },
            cancellationToken);

    private static bool WantsExplanation(OctadockCommand command)
        => command.GetBool("explain") || !string.IsNullOrWhiteSpace(command.Get("style"));

    /// <summary>
    /// Picks the voice provider: an explicit <c>--tts</c> wins, then the Read
    /// settings choice, and anything unavailable falls back to the built-in
    /// Windows voices (which are always local and keyless).
    /// </summary>
    private ITextToSpeechProvider? ResolveTts(OctadockCommand command)
    {
        string requested = command.Get("tts") ?? _settings.Current.Read.TtsProvider;
        ITextToSpeechProvider? chosen = _ttsProviders.FirstOrDefault(p =>
            string.Equals(p.Id, requested, StringComparison.OrdinalIgnoreCase) && p.IsAvailable);
        chosen ??= _ttsProviders.FirstOrDefault(p =>
            string.Equals(p.Id, ReadSettings.WindowsTtsProvider, StringComparison.OrdinalIgnoreCase) && p.IsAvailable);
        chosen ??= _ttsProviders.FirstOrDefault(p => p.IsAvailable);
        return chosen;
    }

    // ---- Playback pill ----

    private void ShowPill(string label)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            _pill?.Close();
            _pill = new ReadingPill();
            _pill.StopRequested += (_, _) => Stop();
            _pill.PauseResumeRequested += (_, _) => TogglePause();
            _pill.SetStatus($"Reading {label}…");
            _pill.ShowNear(_monitors.GetActiveMonitor());

            _pillTimer?.Stop();
            _pillTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _pillTimer.Tick += (_, _) =>
            {
                TimeSpan elapsed = _spokenTime.Elapsed;
                _pill?.SetStatus($"Reading {label} — {(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}");
            };
            _pillTimer.Start();
        });
    }

    private void TogglePause()
    {
        if (_audio.IsPaused)
        {
            _audio.Resume();
            _spokenTime.Start();
            _pill?.SetPaused(false);
        }
        else if (_audio.IsPlaying)
        {
            _audio.Pause();
            _spokenTime.Stop();
            _pill?.SetPaused(true);
        }
    }

    private void ClosePill()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            _pillTimer?.Stop();
            _pillTimer = null;
            _pill?.Close();
            _pill = null;
        });
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temp files; retention is best-effort.
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

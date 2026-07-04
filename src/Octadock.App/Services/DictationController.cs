using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Stt;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;
using Octadock.Platform.Windows.Audio;
using Octadock.Platform.Windows.Input;
using Octadock.Platform.Windows.Stt;

namespace Octadock.App.Services;

/// <summary>
/// Toggle-mode dictation over <see cref="AudioCaptureService"/> and the selected
/// speech-to-text provider: one action (the dock mic, a hotkey, or the command)
/// starts recording the microphone and shows the dictation pill; the same action
/// stops, transcribes, and inserts the transcript at the cursor. No live partials
/// yet; feedback is via the pill and notifications.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DictationController
{
    /// <summary>Starter dictation dictionary; a user-editable version arrives with Settings → STT.</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> DefaultCodeDictionary =
    [
        new("arrow function", "=>"),
        new("triple equals", "==="),
        new("not equals", "!="),
        new("open brace", "{"),
        new("close brace", "}"),
        new("pipe operator", "|>"),
        new("async await", "async/await"),
    ];

    private readonly ISpeechToTextProviderFactory _sttFactory;
    private readonly AudioCaptureService _audio;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly IMonitorService _monitors;
    private readonly ISettingsService _settings;
    private readonly ILogger<DictationController> _logger;
    private readonly SemaphoreSlim _toggleGate = new(1, 1);

    private DictationPill? _pill;
    private DispatcherTimer? _elapsedTimer;
    private DateTimeOffset _startedAt;
    private bool _listening;
    private SpeechSettings _activeSpeech = OctadockSettings.Defaults.Speech;
    private ISpeechToTextProvider? _activeProvider;

    public DictationController(
        ISpeechToTextProviderFactory sttFactory,
        AudioCaptureService audio,
        IClipboardService clipboard,
        INotificationService notifications,
        IMonitorService monitors,
        ISettingsService settings,
        ILogger<DictationController> logger)
    {
        _sttFactory = sttFactory;
        _audio = audio;
        _clipboard = clipboard;
        _notifications = notifications;
        _monitors = monitors;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>True while an utterance is being recorded (drives any toggle label).</summary>
    public bool IsListening => _listening;

    /// <summary>Starts dictation if idle; stops, transcribes, and inserts if listening.</summary>
    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        // One toggle at a time: a double-press must not race start against stop.
        // If a start or a stop+transcribe is already in flight, the second press is
        // a no-op with a hint rather than a hang (the previous behaviour froze the
        // app when the drain/transcribe held the gate).
        if (!await _toggleGate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            _notifications.Notify(
                "Dictation", "Still working on the last dictation — one moment.", NotificationKind.Info);
            return;
        }

        try
        {
            if (_listening)
            {
                await StopAndInsertAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        // Ensure the ggml model is present before we start capturing, showing the
        // download progress on the pill so a first-run multi-second fetch is legible.
        DisplayInfo monitor = _monitors.GetActiveMonitor();
        await ShowPillAsync(monitor, "Preparing…").ConfigureAwait(false);
        SpeechSettings speech = NormalizeSpeech(_settings.Current.Speech);
        _activeSpeech = speech;

        try
        {
            ISpeechToTextProvider? provider = _sttFactory.Resolve(speech.Provider);
            if (provider is null)
            {
                await ClosePillAsync().ConfigureAwait(false);
                _notifications.Notify(
                    "Speech provider unavailable",
                    $"'{speech.Provider}' is not available in this build.",
                    NotificationKind.Warning);
                return;
            }

            if (!provider.IsAvailable && provider is not WhisperSttProvider)
            {
                await ClosePillAsync().ConfigureAwait(false);
                _notifications.Notify(
                    "Speech provider unavailable",
                    UnavailableProviderMessage(provider),
                    NotificationKind.Warning);
                return;
            }

            if (provider is WhisperSttProvider whisper && !whisper.IsModelAvailable(speech.WhisperModel))
            {
                var progress = new Progress<double>(fraction =>
                    UpdatePill($"Downloading the speech model… {fraction * 100:0}%"));
                await whisper.EnsureModelAsync(speech.WhisperModel, progress, cancellationToken).ConfigureAwait(false);
            }

            _activeProvider = provider;
            _audio.Start();
        }
        catch (MicrophoneAccessDeniedException)
        {
            _listening = false;
            _activeProvider = null;
            SafeStopAudio();
            await ClosePillAsync().ConfigureAwait(false);
            _notifications.Notify(
                "Microphone blocked",
                "Windows privacy settings block Octadock. Opening the setting…",
                NotificationKind.Warning);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "ms-settings:privacy-microphone") { UseShellExecute = true });
            return;
        }
        catch (Exception ex)
        {
            // Model download or Start() failed: undo any partial start so the next
            // toggle begins fresh (state reset, capture stopped, pill closed).
            _listening = false;
            _activeProvider = null;
            SafeStopAudio();
            await ClosePillAsync().ConfigureAwait(false);
            _logger.LogError(ex, "Failed to start dictation.");
            _notifications.Notify("Dictation failed", ex.Message, NotificationKind.Error);
            return;
        }

        _listening = true;
        _startedAt = DateTimeOffset.UtcNow;
        StartElapsedTimer();
    }

    private async Task StopAndInsertAsync(CancellationToken cancellationToken)
    {
        _listening = false;
        StopElapsedTimer();
        UpdatePill("Transcribing…");

        try
        {
            SpeechSettings speech = _activeSpeech;
            ISpeechToTextProvider provider = _activeProvider
                ?? throw new InvalidOperationException("No active speech provider was selected.");
            var options = new SttOptions
            {
                Language = LanguageOrAuto(speech.Language),
                Replacements = BuildReplacements(speech.CustomDictionary),
                Model = ModelForProvider(speech, provider),
            };

            // Draining the audio buffer AND transcribing both run off the UI thread:
            // AudioCaptureService.Stop() has a drain loop and Whisper is CPU-bound, so
            // either on the dispatcher would freeze the whole app (the original hang).
            SttResult result = await Task.Run(
                () =>
                {
                    AudioBuffer utterance = _audio.Stop();
                    return provider.TranscribeAsync(utterance, options, cancellationToken);
                },
                cancellationToken)
                .ConfigureAwait(false);

            if (result.IsEmpty)
            {
                _notifications.Notify("Dictation", "No speech detected.", NotificationKind.Info);
                return;
            }

            // Clipboard + SendInput require the UI thread; marshal the insert back.
            await InvokeOnUiAsync(() => Insert(result.Text, speech)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation failed.");
            _notifications.Notify("Dictation failed", ex.Message, NotificationKind.Error);
        }
        finally
        {
            // Always reset UI + state so the next toggle starts fresh, even on failure.
            _listening = false;
            _activeSpeech = OctadockSettings.Defaults.Speech;
            _activeProvider = null;
            await ClosePillAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pastes the transcript at the cursor: snapshot the clipboard, set the
    /// transcript, simulate Ctrl+V, then restore the original clipboard shortly
    /// after so the paste target reads the transcript first. Degrades to
    /// clipboard-only when the foreground window rejects synthetic input.
    /// </summary>
    private void Insert(string text, SpeechSettings speech)
    {
        if (IsClipboardOnly(speech.InsertionMode))
        {
            _clipboard.SetText(text);
            _notifications.Notify("Dictation", "Copied transcript to clipboard.", NotificationKind.Info);
            return;
        }

        IDataObject? saved = TrySnapshotClipboard();
        _clipboard.SetText(text);

        bool pasted = KeyboardInjector.SendPaste();
        if (!pasted)
        {
            // UIPI (elevated window) or no focused control: degrade gracefully and
            // leave the transcript on the clipboard (so skip the restore).
            _notifications.Notify(
                "Dictation", "Copied to clipboard (the focused window rejected input).",
                NotificationKind.Info);
            return;
        }

        if (saved is not null)
        {
            _ = RestoreClipboardLaterAsync(saved);
        }
    }

    private static SpeechSettings NormalizeSpeech(SpeechSettings speech)
        => speech with
        {
            Provider = string.IsNullOrWhiteSpace(speech.Provider)
                ? SpeechSettings.DefaultProvider
                : speech.Provider.Trim(),
            WhisperModel = string.IsNullOrWhiteSpace(speech.WhisperModel)
                ? SpeechSettings.DefaultWhisperModel
                : speech.WhisperModel.Trim(),
            OpenAiModel = string.IsNullOrWhiteSpace(speech.OpenAiModel)
                ? SpeechSettings.DefaultOpenAiModel
                : speech.OpenAiModel.Trim(),
            Language = speech.Language?.Trim() ?? string.Empty,
            InsertionMode = string.IsNullOrWhiteSpace(speech.InsertionMode)
                ? SpeechSettings.DefaultInsertionMode
                : speech.InsertionMode.Trim(),
            CustomDictionary = speech.CustomDictionary ?? string.Empty,
        };

    private static string? LanguageOrAuto(string language)
        => string.IsNullOrWhiteSpace(language) ? null : language.Trim();

    private static string ModelForProvider(SpeechSettings speech, ISpeechToTextProvider provider)
        => string.Equals(provider.Id, SpeechSettings.OpenAiProvider, StringComparison.OrdinalIgnoreCase)
            ? speech.OpenAiModel
            : speech.WhisperModel;

    private static string UnavailableProviderMessage(ISpeechToTextProvider provider)
        => provider is OpenAiSttProvider openAi && !string.IsNullOrWhiteSpace(openAi.UnavailableReason)
            ? openAi.UnavailableReason
            : $"'{provider.Id}' is not available right now.";

    private static IReadOnlyList<KeyValuePair<string, string>> BuildReplacements(string customDictionary)
    {
        if (string.IsNullOrWhiteSpace(customDictionary))
        {
            return DefaultCodeDictionary;
        }

        var replacements = new List<KeyValuePair<string, string>>(DefaultCodeDictionary);
        foreach (string rawLine in customDictionary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf("=>", StringComparison.Ordinal);
            int separatorLength = 2;
            if (separator < 0)
            {
                separator = line.IndexOf('=', StringComparison.Ordinal);
                separatorLength = 1;
            }

            if (separator <= 0 || separator + separatorLength >= line.Length)
            {
                continue;
            }

            string spoken = line[..separator].Trim();
            string replacement = line[(separator + separatorLength)..].Trim();
            if (spoken.Length > 0 && replacement.Length > 0)
            {
                replacements.Add(new KeyValuePair<string, string>(spoken, replacement));
            }
        }

        return replacements;
    }

    private static bool IsClipboardOnly(string insertionMode)
        => string.Equals(insertionMode, "clipboard", StringComparison.OrdinalIgnoreCase)
           || string.Equals(insertionMode, "clipboardOnly", StringComparison.OrdinalIgnoreCase);

    private static async Task RestoreClipboardLaterAsync(IDataObject saved)
    {
        await Task.Delay(300).ConfigureAwait(true); // Let the paste land first.
        try
        {
            global::System.Windows.Clipboard.SetDataObject(saved, copy: true);
        }
        catch
        {
            // Clipboard contention is non-fatal; the transcript simply stays on it.
        }
    }

    private static IDataObject? TrySnapshotClipboard()
    {
        try
        {
            return global::System.Windows.Clipboard.GetDataObject();
        }
        catch
        {
            return null; // Another app holds the clipboard open; skip restore.
        }
    }

    private void StartElapsedTimer()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            _elapsedTimer?.Stop();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _elapsedTimer.Tick += (_, _) =>
            {
                double seconds = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
                _pill?.SetStatus($"Listening… {seconds:0.0}s — press the mic or hotkey to stop");
            };
            _elapsedTimer.Start();
        });
    }

    private void StopElapsedTimer()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
        });
    }

    private async Task ShowPillAsync(DisplayInfo monitor, string status)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            _pill?.Close();
            _pill = new DictationPill();
            _pill.StopRequested += (_, _) => _ = ToggleAsync();
            _pill.SetStatus(status);
            _pill.ShowNear(monitor);
        });
    }

    private void UpdatePill(string status)
        => Application.Current?.Dispatcher.BeginInvoke(() => _pill?.SetStatus(status));

    /// <summary>Best-effort stop of the capture, swallowing errors (used on failure cleanup).</summary>
    private void SafeStopAudio()
    {
        try
        {
            _audio.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to stop audio capture during cleanup.");
        }
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and awaits its completion.</summary>
    private static async Task InvokeOnUiAsync(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private async Task ClosePillAsync()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
            _pill?.Close();
            _pill = null;
        });
    }
}

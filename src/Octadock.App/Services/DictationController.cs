using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Stt;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using Octadock.Platform.Windows.Audio;
using Octadock.Platform.Windows.Input;
using Octadock.Platform.Windows.Stt;

namespace Octadock.App.Services;

/// <summary>
/// Toggle-mode dictation over an <see cref="IDictationAudioSource"/> and the
/// selected speech-to-text provider: one action (the dock mic, a hotkey, or the
/// command) starts recording the microphone and shows the dictation pill; the
/// same action stops, transcribes, and inserts the transcript at the cursor.
/// Model-backed providers download their model on first use via
/// <see cref="IModelBackedSpeechProvider"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DictationController
{
    private const double AutoStopSilenceSeconds = 2.0;
    private static readonly TimeSpan PartialDecodeInterval = TimeSpan.FromMilliseconds(400);

    private readonly ISpeechToTextProviderFactory _sttFactory;
    private readonly IDictationAudioSource _audio;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly IMonitorService _monitors;
    private readonly ISettingsService _settings;
    private readonly IModelDownloadConsent _consent;
    private readonly IVoiceActivityDetector? _vad;
    private readonly ILogger<DictationController> _logger;
    private readonly SemaphoreSlim _toggleGate = new(1, 1);

    private DictationPill? _pill;
    private DispatcherTimer? _elapsedTimer;
    private DateTimeOffset _startedAt;
    private bool _listening;
    private SpeechSettings _activeSpeech = OctadockSettings.Defaults.Speech;
    private ISpeechToTextProvider? _activeProvider;
    private SimulatedStreamingSession? _activeSession;
    private CancellationTokenSource? _sessionCts;
    private readonly object _backgroundDownloadLock = new();
    private Task? _backgroundModelDownload;

    public DictationController(
        ISpeechToTextProviderFactory sttFactory,
        IDictationAudioSource audio,
        IClipboardService clipboard,
        INotificationService notifications,
        IMonitorService monitors,
        ISettingsService settings,
        IModelDownloadConsent consent,
        ILogger<DictationController> logger,
        IVoiceActivityDetector? vad = null)
    {
        _sttFactory = sttFactory;
        _audio = audio;
        _clipboard = clipboard;
        _notifications = notifications;
        _monitors = monitors;
        _settings = settings;
        _consent = consent;
        _vad = vad;
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

            // Model-backed providers (Parakeet, Whisper) fix their own
            // availability by downloading the model; anything else that reports
            // unavailable is a hard stop (e.g. cloud without an API key).
            if (!provider.IsAvailable && provider is not IModelBackedSpeechProvider)
            {
                await ClosePillAsync().ConfigureAwait(false);
                _notifications.Notify(
                    "Speech provider unavailable",
                    UnavailableProviderMessage(provider),
                    NotificationKind.Warning);
                return;
            }

            // An explicit language outside the provider's coverage routes this
            // utterance to Whisper (99 languages) instead of returning garbage.
            provider = RouteForLanguage(provider, speech);

            if (provider is IModelBackedSpeechProvider modelBacked)
            {
                string model = ModelForProvider(speech, provider);
                if (!modelBacked.IsModelAvailable(model))
                {
                    // Consent gate (WS7, R6): a model-backed provider must never
                    // fetch its (hundreds-of-MB) model — foreground or background —
                    // without explicit, one-time, sized consent.
                    bool consented = await _consent.EnsureConsentAsync(
                        new ModelDownloadConsentRequest(
                            ModelDownloadName(provider), modelBacked.ModelDownloadBytes(model)),
                        cancellationToken).ConfigureAwait(false);
                    if (!consented)
                    {
                        await ClosePillAsync().ConfigureAwait(false);
                        _notifications.Notify(
                            "Dictation needs a model",
                            "Dictation stays off until you allow the one-time model download.",
                            NotificationKind.Info);
                        return;
                    }

                    // Never block dictation on Parakeet's ~640 MB first fetch when a
                    // Whisper model is already on disk: dictate with Whisper now and
                    // finish the Parakeet download in the background.
                    ISpeechToTextProvider? stopgap = ResolveStopgapProvider(provider, speech);
                    if (stopgap is not null)
                    {
                        StartBackgroundModelDownload(modelBacked, model);
                        provider = stopgap;
                    }
                    else
                    {
                        var progress = new Progress<double>(fraction =>
                            UpdatePill($"Downloading the speech model… {fraction * 100:0}%"));
                        await modelBacked.EnsureModelAsync(model, progress, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            _activeProvider = provider;
            StartStreamingSessionIfEligible(provider, speech);
            _audio.Start();
        }
        catch (MicrophoneAccessDeniedException)
        {
            _listening = false;
            _activeProvider = null;
            TearDownStreamingSession();
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
            TearDownStreamingSession();
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
            SimulatedStreamingSession? session = _activeSession;
            var options = new SttOptions
            {
                Language = LanguageOrAuto(speech.Language),
                Replacements = TranscriptDictionary.Parse(speech.CustomDictionary),
                Model = ModelForProvider(speech, provider),
            };

            // Draining the audio buffer AND transcribing both run off the UI thread:
            // AudioCaptureService.Stop() has a drain loop and decoding is CPU-bound, so
            // either on the dispatcher would freeze the whole app (the original hang).
            // With a streaming session, Stop()'s final drain feeds the session and
            // FinalizeAsync only decodes the short open tail — near-instant.
            SttResult result = await Task.Run(
                () =>
                {
                    AudioBuffer utterance = _audio.Stop();
                    return session is not null
                        ? session.FinalizeAsync(cancellationToken)
                        : provider.TranscribeAsync(utterance, options, cancellationToken);
                },
                cancellationToken)
                .ConfigureAwait(false);

            if (result.IsEmpty)
            {
                _notifications.Notify("Dictation", "No speech detected.", NotificationKind.Info);
                return;
            }

            // With hold-to-talk the user may still be holding Ctrl+Shift when the
            // key is released; pasting then would send Ctrl+Shift+V. Wait (bounded,
            // off the UI thread) for physical modifiers to clear first.
            if (!IsClipboardOnly(speech.InsertionMode))
            {
                KeyboardInjector.WaitForModifierRelease(TimeSpan.FromMilliseconds(800));
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
            TearDownStreamingSession();
            _activeSpeech = OctadockSettings.Defaults.Speech;
            _activeProvider = null;
            await ClosePillAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Stops listening and throws the utterance away (pill discard button).</summary>
    public async Task DiscardAsync()
    {
        if (!await _toggleGate.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (!_listening)
            {
                return;
            }

            _listening = false;
            StopElapsedTimer();
            TearDownStreamingSession();
            await Task.Run(SafeStopAudio).ConfigureAwait(false);
            _activeSpeech = OctadockSettings.Defaults.Speech;
            _activeProvider = null;
            await ClosePillAsync().ConfigureAwait(false);
            _notifications.Notify("Dictation", "Dictation discarded.", NotificationKind.Info);
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    /// <summary>
    /// Wires the simulated-streaming session when the provider can decode
    /// segments live, the local VAD runs on this device, and live partials are
    /// enabled. Anything missing falls back to the plain record-then-transcribe
    /// path with no behavior change.
    /// </summary>
    private void StartStreamingSessionIfEligible(ISpeechToTextProvider provider, SpeechSettings speech)
    {
        if (!speech.LivePartials && !speech.AutoStopOnSilence)
        {
            return;
        }

        if (provider is not IStreamingSpeechToTextProvider streaming ||
            _vad is null ||
            !_vad.IsAvailable)
        {
            return;
        }

        _vad.Reset();
        var options = new SttOptions
        {
            Language = LanguageOrAuto(speech.Language),
            Replacements = TranscriptDictionary.Parse(speech.CustomDictionary),
            Model = ModelForProvider(speech, provider),
        };
        var session = new SimulatedStreamingSession(streaming, _vad, options);
        if (speech.LivePartials)
        {
            session.PartialChanged += (_, e) =>
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _pill?.SetTranscript(e.Stable, e.Volatile);
                    _pill?.SetSpeechActive(session.IsSpeechActive);
                });
        }

        _audio.SamplesAvailable += OnSamplesAvailable;
        _activeSession = session;
        _sessionCts = new CancellationTokenSource();
        _ = RunSessionLoopAsync(session, _activeSpeech, _sessionCts.Token);
    }

    private void OnSamplesAvailable(object? sender, AudioSamplesEventArgs e)
        => _activeSession?.Accept(e.Samples);

    /// <summary>
    /// Background cadence for the session: re-decode partials every ~400 ms,
    /// keep the pill dot honest about speech/silence, and auto-stop after
    /// sustained silence when enabled.
    /// </summary>
    private async Task RunSessionLoopAsync(
        SimulatedStreamingSession session, SpeechSettings speech, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PartialDecodeInterval, cancellationToken).ConfigureAwait(false);
                if (speech.LivePartials)
                {
                    await session.TickAsync(cancellationToken).ConfigureAwait(false);
                }

                Application.Current?.Dispatcher.BeginInvoke(() =>
                    _pill?.SetSpeechActive(session.IsSpeechActive));

                if (speech.AutoStopOnSilence &&
                    session.HadSpeech &&
                    session.TrailingSilence.TotalSeconds >= AutoStopSilenceSeconds)
                {
                    _logger.LogInformation("Auto-stopping dictation after silence.");
                    _ = ToggleAsync(CancellationToken.None);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal teardown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live partial decoding stopped; dictation continues without partials.");
        }
    }

    private void TearDownStreamingSession()
    {
        _audio.SamplesAvailable -= OnSamplesAvailable;
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
        _activeSession = null;
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

    /// <summary>
    /// Sends this utterance to Whisper when the selected provider declares it
    /// cannot handle the explicitly configured language (e.g. Japanese on
    /// Parakeet's European set). Auto-detect never reroutes.
    /// </summary>
    private ISpeechToTextProvider RouteForLanguage(ISpeechToTextProvider provider, SpeechSettings speech)
    {
        string? language = LanguageOrAuto(speech.Language);
        if (language is null ||
            provider is not ILanguageScopedSpeechProvider scoped ||
            scoped.SupportsLanguage(language))
        {
            return provider;
        }

        ISpeechToTextProvider? fallback = _sttFactory.Resolve(SpeechSettings.WhisperProvider);
        if (fallback is null || ReferenceEquals(fallback, provider))
        {
            return provider;
        }

        _logger.LogInformation(
            "Language '{Language}' is outside provider '{Provider}'; routing this dictation to '{Fallback}'.",
            language,
            provider.Id,
            fallback.Id);
        return fallback;
    }

    /// <summary>
    /// Picks a ready-to-use local provider to dictate with while the primary
    /// provider's model downloads. Only the Parakeet→Whisper hop exists today,
    /// and only when the configured Whisper model is already on disk.
    /// </summary>
    private ISpeechToTextProvider? ResolveStopgapProvider(ISpeechToTextProvider primary, SpeechSettings speech)
    {
        if (!string.Equals(primary.Id, SpeechSettings.ParakeetProvider, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ISpeechToTextProvider? whisper = _sttFactory.Resolve(SpeechSettings.WhisperProvider);
        if (whisper is null || ReferenceEquals(whisper, primary))
        {
            return null;
        }

        return whisper is IModelBackedSpeechProvider modelBacked &&
               modelBacked.IsModelAvailable(speech.WhisperModel)
            ? whisper
            : null;
    }

    /// <summary>
    /// Kicks off (at most one) background model download and toasts when the
    /// engine is ready. Deliberately not tied to the utterance's cancellation
    /// token — closing the pill must not abandon a half-fetched model.
    /// </summary>
    private void StartBackgroundModelDownload(IModelBackedSpeechProvider provider, string model)
    {
        lock (_backgroundDownloadLock)
        {
            if (_backgroundModelDownload is { IsCompleted: false })
            {
                return;
            }

            _backgroundModelDownload = Task.Run(async () =>
            {
                try
                {
                    await provider.EnsureModelAsync(model, null, CancellationToken.None).ConfigureAwait(false);
                    _notifications.Notify(
                        "Dictation upgraded",
                        "The Parakeet speech model is ready — your next dictation uses the faster local engine.",
                        NotificationKind.Info);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background speech model download failed; will retry on next dictation.");
                }
            });
        }
    }

    private static SpeechSettings NormalizeSpeech(SpeechSettings speech)
        => speech with
        {
            Provider = string.IsNullOrWhiteSpace(speech.Provider)
                ? SpeechSettings.DefaultProvider
                : speech.Provider.Trim(),
            ParakeetModel = string.IsNullOrWhiteSpace(speech.ParakeetModel)
                ? SpeechSettings.DefaultParakeetModel
                : speech.ParakeetModel.Trim(),
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
            ActivationMode = string.IsNullOrWhiteSpace(speech.ActivationMode)
                ? SpeechSettings.DefaultActivationMode
                : speech.ActivationMode.Trim(),
            CustomDictionary = speech.CustomDictionary ?? string.Empty,
        };

    private static string? LanguageOrAuto(string language)
        => string.IsNullOrWhiteSpace(language) ? null : language.Trim();

    private static string ModelDownloadName(ISpeechToTextProvider provider)
    {
        if (string.Equals(provider.Id, SpeechSettings.ParakeetProvider, StringComparison.OrdinalIgnoreCase))
        {
            return "The Parakeet speech model";
        }

        if (string.Equals(provider.Id, SpeechSettings.WhisperProvider, StringComparison.OrdinalIgnoreCase))
        {
            return "The Whisper speech model";
        }

        return "The speech model";
    }

    private static string ModelForProvider(SpeechSettings speech, ISpeechToTextProvider provider)
    {
        if (string.Equals(provider.Id, SpeechSettings.OpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            return speech.OpenAiModel;
        }

        if (string.Equals(provider.Id, SpeechSettings.ParakeetProvider, StringComparison.OrdinalIgnoreCase))
        {
            return speech.ParakeetModel;
        }

        return speech.WhisperModel;
    }

    private static string UnavailableProviderMessage(ISpeechToTextProvider provider)
        => provider switch
        {
            OpenAiSttProvider { UnavailableReason: { Length: > 0 } reason } => reason,
            ParakeetSttProvider { UnavailableReason: { Length: > 0 } reason } => reason,
            _ => $"'{provider.Id}' is not available right now.",
        };

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
            _pill.DiscardRequested += (_, _) => _ = DiscardAsync();
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

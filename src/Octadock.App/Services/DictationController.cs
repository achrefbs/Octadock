using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Stt;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Licensing;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using Octadock.Platform.Windows.Audio;
using Octadock.Platform.Windows.Input;
using Octadock.Platform.Windows.Stt;

namespace Octadock.App.Services;

/// <summary>The externally useful outcome of one dictation toggle operation.</summary>
public enum DictationOperationStatus
{
    None,
    Started,
    Completed,
    Discarded,
    Declined,
    Cancelled,
    Busy,
    Failed,
}

/// <summary>
/// Result returned by the status-aware dictation API. <see cref="DictationController.ToggleAsync"/>
/// remains available for existing UI callers that intentionally ignore the result.
/// </summary>
public sealed record DictationOperationResult(DictationOperationStatus Status, string Message)
{
    public bool Succeeded => Status is DictationOperationStatus.Started
        or DictationOperationStatus.Completed
        or DictationOperationStatus.Discarded;
}

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
    private readonly ILicenseGate _licenseGate;
    private readonly IVoiceActivityDetector? _vad;
    private readonly ILogger<DictationController> _logger;
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private readonly object _stateLock = new();

    private DictationPill? _pill;
    private DispatcherTimer? _elapsedTimer;
    private DateTimeOffset _startedAt;
    private volatile bool _listening;
    private CancellationTokenSource? _preparationCts;
    private string _latestStableTranscript = string.Empty;
    private string _latestVolatileTranscript = string.Empty;
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
        ILicenseGate licenseGate,
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
        _licenseGate = licenseGate;
        _vad = vad;
        _logger = logger;
    }

    /// <summary>True while an utterance is being recorded (drives any toggle label).</summary>
    public bool IsListening => _listening;

    /// <summary>True while provider/model preparation is in progress and can be cancelled.</summary>
    public bool IsPreparing
    {
        get
        {
            lock (_stateLock)
            {
                return _preparationCts is not null;
            }
        }
    }

    /// <summary>The most recent status-aware operation result.</summary>
    public DictationOperationResult LastOperationResult { get; private set; } =
        new(DictationOperationStatus.None, "Dictation is idle.");

    /// <summary>
    /// Best live partial retained after a failed/cancelled transcription. It is
    /// also copied to the clipboard when possible so a provider failure does not
    /// destroy words the user already saw on the pill.
    /// </summary>
    public string? LastRecoveredTranscript { get; private set; }

    /// <summary>Starts dictation if idle; stops, transcribes, and inserts if listening.</summary>
    public async Task ToggleAsync(CancellationToken cancellationToken = default)
        => _ = await ToggleWithResultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Status-aware form of <see cref="ToggleAsync"/> used by automation. A
    /// second toggle during provider/model preparation cancels that preparation
    /// immediately instead of waiting behind the operation gate.
    /// </summary>
    public async Task<DictationOperationResult> ToggleWithResultAsync(
        CancellationToken cancellationToken = default)
    {
        if (RequestPreparationCancellation())
        {
            return SetOutcome(DictationOperationStatus.Cancelled, "Cancelling dictation preparation.");
        }

        // One toggle at a time: a double-press must not race start against stop.
        // Cancellation never participates in acquiring this gate: if a stop is
        // requested with an already-cancelled token, cleanup must still enter and
        // stop the microphone before observing that cancellation.
        if (!await _toggleGate.WaitAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return SetOutcome(DictationOperationStatus.Cancelled, "Dictation was cancelled.");
            }

            _notifications.Notify(
                "Dictation", "Still working on the last dictation — one moment.", NotificationKind.Info);
            return SetOutcome(DictationOperationStatus.Busy, "Still working on the last dictation.");
        }

        try
        {
            if (_listening)
            {
                return SetOutcome(await StopAndInsertAsync(cancellationToken).ConfigureAwait(false));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return SetOutcome(DictationOperationStatus.Cancelled, "Dictation was cancelled.");
            }

            if (_licenseGate.Allow(GatedFeature.Dictation))
            {
                // Trial/license gate (WS5): starting a new dictation is blocked
                // post-expiry; stopping an in-flight one always completes.
                return SetOutcome(await StartAsync(cancellationToken).ConfigureAwait(false));
            }

            return SetOutcome(
                DictationOperationStatus.Declined,
                "Starting dictation requires an active trial or license.");
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    private async Task<DictationOperationResult> StartAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource preparation = BeginPreparation(cancellationToken);
        CancellationToken preparationToken = preparation.Token;
        bool listeningStarted = false;
        bool audioStartAttempted = false;
        SpeechSettings speech = OctadockSettings.Defaults.Speech;

        try
        {
            ClearCurrentPartialTranscript();
            DisplayInfo monitor = _monitors.GetActiveMonitor();
            await ShowPillAsync(monitor, "Preparing…").ConfigureAwait(false);
            preparationToken.ThrowIfCancellationRequested();
            speech = NormalizeSpeech(_settings.Current.Speech);
            _activeSpeech = speech;

            ISpeechToTextProvider? provider = _sttFactory.Resolve(speech.Provider);
            if (provider is null)
            {
                await ClosePillAsync().ConfigureAwait(false);
                string message = $"'{speech.Provider}' is not available in this build.";
                _notifications.Notify(
                    "Speech provider unavailable",
                    message,
                    NotificationKind.Warning);
                return new DictationOperationResult(DictationOperationStatus.Declined, message);
            }

            // Model-backed providers (Parakeet, Whisper) fix their own
            // availability by downloading the model; anything else that reports
            // unavailable is a hard stop (e.g. cloud without an API key).
            if (!provider.IsAvailable && provider is not IModelBackedSpeechProvider)
            {
                await ClosePillAsync().ConfigureAwait(false);
                string message = UnavailableProviderMessage(provider);
                _notifications.Notify(
                    "Speech provider unavailable",
                    message,
                    NotificationKind.Warning);
                return new DictationOperationResult(DictationOperationStatus.Declined, message);
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
                        preparationToken).ConfigureAwait(false);
                    if (!consented)
                    {
                        await ClosePillAsync().ConfigureAwait(false);
                        _notifications.Notify(
                            "Dictation needs a model",
                            "Dictation stays off until you allow the one-time model download.",
                            NotificationKind.Info);
                        return new DictationOperationResult(
                            DictationOperationStatus.Declined,
                            "Dictation needs a speech model download.");
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
                        await modelBacked.EnsureModelAsync(model, progress, preparationToken).ConfigureAwait(false);
                    }
                }
            }

            if (provider is IPreparableSpeechProvider preparable)
            {
                UpdatePill("Warming up the speech engine…");
                await preparable
                    .PrepareAsync(ModelForProvider(speech, provider), preparationToken)
                    .ConfigureAwait(false);
            }

            preparationToken.ThrowIfCancellationRequested();
            _activeProvider = provider;
            StartStreamingSessionIfEligible(provider, speech);
            audioStartAttempted = true;
            _audio.Start();
            // A stop/discard can race the synchronous device start. Observe it
            // before publishing Listening so a cancelled preparation never leaves
            // an open microphone behind a closed pill.
            // Commit the transition from Preparing -> Listening under the same
            // lock used by cancellation. This gives a racing second toggle one
            // honest outcome: it either cancels preparation before the commit,
            // or sees preparation complete (and receives Busy until this toggle
            // releases the operation gate). It can never report "cancelled"
            // while this invocation quietly leaves the microphone listening.
            PublishListening(preparation);
            listeningStarted = true;
            _startedAt = DateTimeOffset.UtcNow;
            StartElapsedTimer();
            return new DictationOperationResult(DictationOperationStatus.Started, "Dictation started.");
        }
        catch (OperationCanceledException) when (preparationToken.IsCancellationRequested)
        {
            _listening = false;
            _activeProvider = null;
            TearDownStreamingSession();
            if (audioStartAttempted)
            {
                SafeStopAudio();
            }

            await ClosePillAsync().ConfigureAwait(false);
            _notifications.Notify(
                "Dictation", "Dictation preparation cancelled.", NotificationKind.Info);
            return new DictationOperationResult(
                DictationOperationStatus.Cancelled,
                "Dictation preparation was cancelled.");
        }
        catch (MicrophoneAccessDeniedException)
        {
            _listening = false;
            _activeProvider = null;
            TearDownStreamingSession();
            if (audioStartAttempted)
            {
                SafeStopAudio();
            }

            await ClosePillAsync().ConfigureAwait(false);
            _notifications.Notify(
                "Microphone blocked",
                "Windows privacy settings block Octadock. Opening the setting…",
                NotificationKind.Warning);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "ms-settings:privacy-microphone") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                // The actionable status is still Microphone blocked even if the
                // Windows Settings URI cannot be launched on this installation.
                _logger.LogDebug(ex, "Could not open Windows microphone privacy settings.");
            }

            return new DictationOperationResult(
                DictationOperationStatus.Declined,
                "Microphone access is blocked by Windows privacy settings.");
        }
        catch (Exception ex)
        {
            // Model download or Start() failed: undo any partial start so the next
            // toggle begins fresh (state reset, capture stopped, pill closed).
            _listening = false;
            _activeProvider = null;
            TearDownStreamingSession();
            if (audioStartAttempted)
            {
                SafeStopAudio();
            }

            await ClosePillAsync().ConfigureAwait(false);
            _logger.LogError(ex, "Failed to start dictation.");
            _notifications.Notify("Dictation failed", ex.Message, NotificationKind.Error);
            return new DictationOperationResult(DictationOperationStatus.Failed, ex.Message);
        }
        finally
        {
            CompletePreparation(preparation, listeningStarted);
        }
    }

    private async Task<DictationOperationResult> StopAndInsertAsync(CancellationToken cancellationToken)
    {
        _listening = false;
        bool audioStopCompleted = false;

        try
        {
            // Keep even UI/timer transition failures inside the cleanup region.
            // A dispatcher shutting down must never prevent the microphone stop.
            StopElapsedTimer();
            UpdatePill("Transcribing…");
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
            // Deliberately schedule cleanup with CancellationToken.None. Passing
            // the caller token to Task.Run can cancel the delegate before it ever
            // executes, which used to leave WASAPI recording behind a closed pill.
            SttResult result = await Task.Run(
                async () =>
                {
                    AudioBuffer utterance = _audio.Stop();
                    audioStopCompleted = true;
                    cancellationToken.ThrowIfCancellationRequested();
                    return session is not null
                        ? await session.FinalizeAsync(cancellationToken).ConfigureAwait(false)
                        : await provider.TranscribeAsync(utterance, options, cancellationToken).ConfigureAwait(false);
                },
                CancellationToken.None)
                .ConfigureAwait(false);

            if (result.IsEmpty)
            {
                _notifications.Notify("Dictation", "No speech detected.", NotificationKind.Info);
                return new DictationOperationResult(
                    DictationOperationStatus.Completed,
                    "No speech was detected.");
            }

            StoreCurrentTranscript(result.Text, string.Empty);

            // With hold-to-talk the user may still be holding Ctrl+Shift when the
            // key is released; pasting then would send Ctrl+Shift+V. Wait (bounded,
            // off the UI thread) for physical modifiers to clear first.
            if (!IsClipboardOnly(speech.InsertionMode))
            {
                KeyboardInjector.WaitForModifierRelease(TimeSpan.FromMilliseconds(800));
            }

            // Clipboard + SendInput require the UI thread; marshal the insert back.
            await InvokeOnUiAsync(() => Insert(result.Text, speech)).ConfigureAwait(false);
            LastRecoveredTranscript = null;
            return new DictationOperationResult(
                DictationOperationStatus.Completed,
                "Dictation completed.");
        }
        catch (OperationCanceledException)
        {
            bool recovered = await PreserveBestPartialAsync().ConfigureAwait(false);
            string message = recovered
                ? "Dictation was cancelled; the best partial transcript was preserved. Click this notification to copy it again."
                : "Dictation was cancelled.";
            _notifications.Notify(
                "Dictation",
                message,
                NotificationKind.Info,
                recovered ? CopyLastRecoveredTranscript : null);
            return new DictationOperationResult(DictationOperationStatus.Cancelled, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation failed.");
            bool recovered = await PreserveBestPartialAsync().ConfigureAwait(false);
            string message = recovered
                ? $"{ex.Message} The best partial transcript was preserved; click this notification to copy it again."
                : ex.Message;
            _notifications.Notify(
                "Dictation failed",
                message,
                NotificationKind.Error,
                recovered ? CopyLastRecoveredTranscript : null);
            return new DictationOperationResult(DictationOperationStatus.Failed, message);
        }
        finally
        {
            // Always reset UI + state so the next toggle starts fresh, even on failure.
            _listening = false;
            if (!audioStopCompleted)
            {
                SafeStopAudio();
            }

            TearDownStreamingSession();
            _activeSpeech = OctadockSettings.Defaults.Speech;
            _activeProvider = null;
            await ClosePillAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Stops listening and throws the utterance away (pill discard button).</summary>
    public async Task DiscardAsync()
    {
        if (RequestPreparationCancellation())
        {
            SetOutcome(DictationOperationStatus.Cancelled, "Cancelling dictation preparation.");
            return;
        }

        if (!await _toggleGate.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
        {
            return;
        }

        bool stopRequired = false;
        bool audioStopAttempted = false;
        try
        {
            if (!_listening)
            {
                return;
            }

            stopRequired = true;
            _listening = false;
            StopElapsedTimer();
            TearDownStreamingSession();
            await Task.Run(SafeStopAudio).ConfigureAwait(false);
            audioStopAttempted = true;
            _activeSpeech = OctadockSettings.Defaults.Speech;
            _activeProvider = null;
            await ClosePillAsync().ConfigureAwait(false);
            _notifications.Notify("Dictation", "Dictation discarded.", NotificationKind.Info);
            ClearCurrentPartialTranscript();
            SetOutcome(DictationOperationStatus.Discarded, "Dictation discarded.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discard dictation cleanly.");
            _notifications.Notify("Dictation failed", ex.Message, NotificationKind.Error);
            SetOutcome(DictationOperationStatus.Failed, ex.Message);
        }
        finally
        {
            _listening = false;
            if (stopRequired && !audioStopAttempted)
            {
                // Timer/session/pill cleanup can itself fail during application
                // shutdown; microphone cleanup remains non-negotiable.
                await Task.Run(SafeStopAudio).ConfigureAwait(false);
            }

            _toggleGate.Release();
        }
    }

    private CancellationTokenSource BeginPreparation(CancellationToken cancellationToken)
    {
        CancellationTokenSource preparation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_stateLock)
        {
            if (_preparationCts is not null)
            {
                preparation.Dispose();
                throw new InvalidOperationException("Dictation preparation is already active.");
            }

            _preparationCts = preparation;
        }

        return preparation;
    }

    private void CompletePreparation(CancellationTokenSource preparation, bool listeningStarted)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_preparationCts, preparation))
            {
                _preparationCts = null;
            }
        }

        if (!listeningStarted)
        {
            _listening = false;
        }
    }

    private void PublishListening(CancellationTokenSource preparation)
    {
        lock (_stateLock)
        {
            preparation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_preparationCts, preparation))
            {
                throw new OperationCanceledException(preparation.Token);
            }

            _preparationCts = null;
            _listening = true;
        }
    }

    /// <summary>
    /// Cancels provider/model preparation without waiting for the toggle gate.
    /// Cancellation is requested while holding the state lock so the owner cannot
    /// clear and dispose the CTS between lookup and Cancel().
    /// </summary>
    private bool RequestPreparationCancellation()
    {
        lock (_stateLock)
        {
            if (_preparationCts is null)
            {
                return false;
            }

            try
            {
                _preparationCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            UpdatePill("Cancelling…");
            return true;
        }
    }

    private DictationOperationResult SetOutcome(DictationOperationStatus status, string message)
        => SetOutcome(new DictationOperationResult(status, message));

    private DictationOperationResult SetOutcome(DictationOperationResult result)
    {
        LastOperationResult = result;
        return result;
    }

    private void StoreCurrentTranscript(string stable, string volatilePart)
    {
        lock (_stateLock)
        {
            _latestStableTranscript = stable?.Trim() ?? string.Empty;
            _latestVolatileTranscript = volatilePart?.Trim() ?? string.Empty;
        }
    }

    private void ClearCurrentPartialTranscript()
        => StoreCurrentTranscript(string.Empty, string.Empty);

    private string BestCurrentTranscript()
    {
        lock (_stateLock)
        {
            return string.Join(
                ' ',
                new[] { _latestStableTranscript, _latestVolatileTranscript }
                    .Where(static part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    private async Task<bool> PreserveBestPartialAsync()
    {
        string partial = BestCurrentTranscript();
        if (string.IsNullOrWhiteSpace(partial))
        {
            return false;
        }

        LastRecoveredTranscript = partial;
        try
        {
            await InvokeOnUiAsync(() => _clipboard.SetText(partial)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Keep the in-memory copy and expose a notification click retry even
            // when another app temporarily owns the clipboard.
            _logger.LogWarning(ex, "Could not copy the recovered partial transcript.");
        }

        return true;
    }

    private void CopyLastRecoveredTranscript()
    {
        string? transcript = LastRecoveredTranscript;
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return;
        }

        try
        {
            _clipboard.SetText(transcript);
            _notifications.Notify(
                "Dictation", "Recovered partial transcript copied.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not copy the recovered partial transcript on retry.");
            _notifications.Notify(
                "Dictation", "The clipboard is busy. Try again in a moment.", NotificationKind.Warning);
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
        session.PartialChanged += (_, e) =>
        {
            StoreCurrentTranscript(e.Stable, e.Volatile);
            if (speech.LivePartials)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _pill?.SetTranscript(e.Stable, e.Volatile);
                    _pill?.SetSpeechActive(session.IsSpeechActive);
                });
            }
        };

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
        uint transcriptClipboardSequence = GetClipboardSequenceNumber();

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
            _ = RestoreClipboardLaterAsync(saved, text, transcriptClipboardSequence);
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

    private async Task RestoreClipboardLaterAsync(
        IDataObject saved,
        string transcript,
        uint transcriptClipboardSequence)
    {
        await Task.Delay(300).ConfigureAwait(true); // Let the paste land first.
        try
        {
            uint currentSequence = GetClipboardSequenceNumber();
            string? currentText = _clipboard.TryGetText();
            if (!ShouldRestoreClipboard(
                    transcriptClipboardSequence,
                    currentSequence,
                    transcript,
                    currentText))
            {
                // The user or another app copied something after the injected
                // paste. That newer clipboard value always wins.
                return;
            }

            global::System.Windows.Clipboard.SetDataObject(saved, copy: true);
        }
        catch
        {
            // Clipboard contention is non-fatal; the transcript simply stays on it.
        }
    }

    /// <summary>Pure policy kept visible to tests: restore only our unchanged clipboard write.</summary>
    internal static bool ShouldRestoreClipboard(
        uint expectedSequence,
        uint currentSequence,
        string expectedTranscript,
        string? currentText)
    {
        if (expectedSequence != 0 && currentSequence != expectedSequence)
        {
            return false;
        }

        return string.Equals(currentText, expectedTranscript, StringComparison.Ordinal);
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
            _elapsedTimer = null;
            _pill = null;
            return;
        }

        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                _elapsedTimer?.Stop();
                _elapsedTimer = null;
                _pill?.Close();
                _pill = null;
            });
        }
        catch (Exception ex)
        {
            // Cleanup commonly races application shutdown. Do not let a dead
            // dispatcher replace the real dictation result or bypass audio
            // cleanup; release our references and finish best-effort.
            _elapsedTimer = null;
            _pill = null;
            _logger.LogDebug(ex, "Could not close the dictation pill cleanly.");
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}

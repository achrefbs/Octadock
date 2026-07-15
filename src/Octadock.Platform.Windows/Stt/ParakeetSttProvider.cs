using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using SherpaOnnx;

namespace Octadock.Platform.Windows.Stt;

/// <summary>
/// Local transcription with NVIDIA Parakeet TDT 0.6B v3 (int8) via the
/// sherpa-onnx bindings after its model files are available. This is the default
/// dictation engine: it runs 20-30x realtime on ordinary CPUs, produces native
/// punctuation and casing, and covers English plus 24 other European languages.
/// Lifecycle mirrors the Whisper provider: model files download once via
/// <see cref="ParakeetModelStore"/>, and the recognizer stays resident after the
/// first build (seconds) so later utterances decode in tens of milliseconds.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ParakeetSttProvider :
    ISpeechToTextProvider,
    IModelBackedSpeechProvider,
    IPreparableSpeechProvider,
    ILanguageScopedSpeechProvider,
    IStreamingSpeechToTextProvider,
    IDisposable
{
    /// <summary>Two-letter codes of the 25 languages Parakeet TDT v3 supports.</summary>
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "hr", "hu",
        "it", "lt", "lv", "mt", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sv", "uk",
    };

    private static readonly object ProbeLock = new();
    private static bool? _nativeAvailable;
    private static string? _nativeUnavailableReason;

    private readonly ParakeetModelStore _store;
    private readonly ILogger<ParakeetSttProvider> _logger;
    private readonly SemaphoreSlim _recognizerGate = new(1, 1);
    private OfflineRecognizer? _recognizer;
    private string? _loadedModel;

    /// <summary>Creates the Parakeet provider.</summary>
    public ParakeetSttProvider(ParakeetModelStore store, ILogger<ParakeetSttProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => SpeechSettings.ParakeetProvider;

    /// <summary>
    /// True when the sherpa-onnx native runtime loads on this machine. The
    /// model itself may still need downloading — that is
    /// <see cref="IsModelAvailable"/>'s job.
    /// </summary>
    public bool IsAvailable => ProbeNativeRuntime();

    /// <summary>Reason shown in settings when the native runtime cannot load.</summary>
    public string? UnavailableReason
    {
        get
        {
            ProbeNativeRuntime();
            return _nativeUnavailableReason;
        }
    }

    /// <inheritdoc />
    public bool IsModelAvailable(string? model) => _store.IsComplete(model);

    /// <inheritdoc />
    public long ModelDownloadBytes(string? model) => _store.TotalBytes;

    /// <inheritdoc />
    public Task EnsureModelAsync(string? model, IProgress<double>? progress, CancellationToken cancellationToken)
        => _store.EnsureAsync(model, progress, cancellationToken);

    /// <inheritdoc />
    public void DeleteModel(string? model)
    {
        _recognizerGate.Wait();
        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _loadedModel = null;
            _store.Delete(model);
        }
        finally
        {
            _recognizerGate.Release();
        }
    }

    /// <inheritdoc />
    public bool SupportsLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return true; // The model detects its own language across the supported set.
        }

        string code = languageCode.Trim();
        int separator = code.IndexOfAny(['-', '_']);
        if (separator > 0)
        {
            code = code[..separator]; // "pt-BR" → "pt".
        }

        return SupportedLanguages.Contains(code);
    }

    /// <summary>
    /// Builds the resident recognizer ahead of the first dictation so the
    /// first finalize is as fast as every later one. Safe to call from a
    /// background task at startup; a no-op unless natives and model are ready.
    /// </summary>
    public void WarmUp(string? model)
    {
        if (!IsAvailable || !IsModelAvailable(model))
        {
            return;
        }

        try
        {
            GetRecognizer(model, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parakeet warm-up failed; the first dictation will retry.");
        }
    }

    /// <inheritdoc />
    public Task PrepareAsync(string? model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable || !IsModelAvailable(model))
        {
            return Task.CompletedTask;
        }

        // Recognizer construction is synchronous native work. Keep it off the
        // UI thread, but do not pass the token to Task.Run: a pre-cancelled task
        // would skip the delegate and make lifecycle/cleanup behavior depend on
        // scheduler timing. The body observes cancellation before waiting for
        // the recognizer gate and again after any non-interruptible native build.
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = GetRecognizer(model, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            },
            CancellationToken.None);
    }

    /// <inheritdoc />
    /// <remarks>
    /// CPU-bound and blocking (the recognizer build takes seconds once; decode
    /// is synchronous native code). By contract this must be called off the UI
    /// thread — the dictation controller wraps it in Task.Run.
    /// </remarks>
    public Task<SttResult> TranscribeAsync(
        AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
    {
        if (audio.Samples.Length == 0)
        {
            return Task.FromResult(new SttResult(string.Empty, null, TimeSpan.Zero));
        }

        cancellationToken.ThrowIfCancellationRequested();

        Stopwatch stopwatch = Stopwatch.StartNew();
        string text = Decode(audio, options.Model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        stopwatch.Stop();

        string transcript = TranscriptDictionary.Apply(text.Trim(), options.Replacements);
        _logger.LogInformation(
            "Dictation transcribed {AudioSeconds:0.0}s with Parakeet; inference {ElapsedMs} ms (RTF {Rtf:0.000}).",
            audio.Duration.TotalSeconds,
            stopwatch.ElapsedMilliseconds,
            audio.Duration.TotalSeconds > 0
                ? stopwatch.Elapsed.TotalSeconds / audio.Duration.TotalSeconds
                : 0);

        string? language = string.IsNullOrWhiteSpace(options.Language) ? null : options.Language.Trim();
        return Task.FromResult(new SttResult(transcript, language, audio.Duration));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Raw per-segment decode for the simulated-streaming session: no
    /// dictionary post-processing (the session applies it once over the joined
    /// transcript) and no per-call logging (this runs several times a second).
    /// </remarks>
    public Task<string> TranscribeSegmentAsync(
        AudioBuffer segment, SttOptions options, CancellationToken cancellationToken)
    {
        if (segment.Samples.Length == 0)
        {
            return Task.FromResult(string.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string text = Decode(segment, options.Model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(text);
    }

    private string Decode(AudioBuffer audio, string? model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OfflineRecognizer recognizer = GetRecognizer(model, cancellationToken);
        using OfflineStream stream = recognizer.CreateStream();
        stream.AcceptWaveform(audio.SampleRate, audio.Samples);
        cancellationToken.ThrowIfCancellationRequested();
        recognizer.Decode(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return stream.Result.Text;
    }

    private OfflineRecognizer GetRecognizer(string? model, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = ParakeetModelStore.NormalizeModel(model);
        if (_recognizer is not null && _loadedModel == normalized)
        {
            return _recognizer;
        }

        _recognizerGate.Wait(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_recognizer is null || _loadedModel != normalized)
            {
                if (!_store.IsComplete(normalized))
                {
                    throw new InvalidOperationException(
                        "The Parakeet speech model is not downloaded yet. Settings → Speech to text.");
                }

                ParakeetModelPaths paths = _store.PathsFor(normalized);
                var config = new OfflineRecognizerConfig();
                config.ModelConfig.Transducer.Encoder = paths.Encoder;
                config.ModelConfig.Transducer.Decoder = paths.Decoder;
                config.ModelConfig.Transducer.Joiner = paths.Joiner;
                config.ModelConfig.Tokens = paths.Tokens;
                config.ModelConfig.ModelType = "nemo_transducer";
                config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount - 1, 2, 8);
                config.ModelConfig.Provider = "cpu";
                config.ModelConfig.Debug = 0;
                config.DecodingMethod = "greedy_search";

                Stopwatch stopwatch = Stopwatch.StartNew();
                _recognizer?.Dispose();
                _recognizer = new OfflineRecognizer(config); // The slow, resident part.
                _loadedModel = normalized;
                _logger.LogInformation(
                    "Parakeet recognizer ready in {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
            }

            // Native construction itself cannot be interrupted safely. Honor a
            // cancellation that arrived during it before publishing success;
            // the resident recognizer remains valid for the next attempt.
            cancellationToken.ThrowIfCancellationRequested();
            return _recognizer;
        }
        finally
        {
            _recognizerGate.Release();
        }
    }

    /// <summary>
    /// Checks once per process that the sherpa-onnx native library loads
    /// (missing runtime folder, wrong architecture, or blocked DLL all surface
    /// here instead of exploding mid-dictation).
    /// </summary>
    private bool ProbeNativeRuntime()
    {
        bool? known = _nativeAvailable;
        if (known is not null)
        {
            return known.Value;
        }

        lock (ProbeLock)
        {
            if (_nativeAvailable is null)
            {
                try
                {
                    nint versionPtr = SherpaOnnxGetVersionStr();
                    string version = Marshal.PtrToStringUTF8(versionPtr) ?? "unknown";
                    _logger.LogInformation("sherpa-onnx native runtime {Version} loaded.", version);
                    _nativeAvailable = true;
                }
                catch (Exception ex) when (
                    ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                {
                    _nativeUnavailableReason =
                        "The local speech runtime is not available on this device.";
                    _logger.LogWarning(ex, "sherpa-onnx native runtime unavailable; Parakeet is disabled.");
                    _nativeAvailable = false;
                }
            }

            return _nativeAvailable.Value;
        }
    }

    [DllImport("sherpa-onnx-c-api", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SherpaOnnxGetVersionStr();

    /// <inheritdoc />
    public void Dispose()
    {
        // Startup warm-up and dictation can be inside native construction when
        // shutdown begins. Serialize disposal with that work so the recognizer
        // is never torn down while another thread is still building it.
        _recognizerGate.Wait();
        try
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _loadedModel = null;
        }
        finally
        {
            _recognizerGate.Release();
            _recognizerGate.Dispose();
        }
    }
}

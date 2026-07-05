namespace Octadock.Core.Abstractions;

/// <summary>
/// The microphone source dictation records from. Abstracts the WASAPI capture
/// service so the dictation controller is unit-testable and so streaming
/// consumers can observe samples while the utterance is still being spoken.
/// </summary>
public interface IDictationAudioSource
{
    /// <summary>Starts capturing the default microphone.</summary>
    void Start();

    /// <summary>Stops capturing and returns the full utterance recorded so far.</summary>
    AudioBuffer Stop();

    /// <summary>
    /// Raised roughly every 100 ms with newly captured 16 kHz mono samples while
    /// recording. Raised on a background thread; the memory is only valid for
    /// the duration of the callback unless copied.
    /// </summary>
    event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;

    /// <summary>Peak level 0..1 of recent audio, for level indicators.</summary>
    float LastPeak { get; }
}

/// <summary>New 16 kHz mono float samples captured since the previous event.</summary>
public sealed class AudioSamplesEventArgs(ReadOnlyMemory<float> samples) : EventArgs
{
    public ReadOnlyMemory<float> Samples { get; } = samples;
}

/// <summary>
/// Capability of providers whose engine is backed by locally downloaded model
/// files (Whisper ggml, Parakeet onnx). The dictation controller uses this to
/// drive first-run downloads without referencing concrete provider types.
/// </summary>
public interface IModelBackedSpeechProvider : ISpeechToTextProvider
{
    /// <summary>True when the given model variant (null = default) is on disk.</summary>
    bool IsModelAvailable(string? model);

    /// <summary>Approximate download size in bytes for UX copy; 0 when unknown.</summary>
    long ModelDownloadBytes(string? model);

    /// <summary>Downloads the model variant with progress (0..1); resumable and atomic.</summary>
    Task EnsureModelAsync(string? model, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes the local model files for the given variant.</summary>
    void DeleteModel(string? model);
}

/// <summary>
/// Capability of providers that only support a fixed language set (e.g.
/// Parakeet's 25 European languages). Lets the controller route unsupported
/// explicit languages to a broader-coverage provider.
/// </summary>
public interface ILanguageScopedSpeechProvider : ISpeechToTextProvider
{
    /// <summary>True for null/empty (auto-detect) and supported BCP-47-ish codes.</summary>
    bool SupportsLanguage(string? languageCode);
}

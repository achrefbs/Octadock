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

/// <summary>
/// Capability of providers fast enough to decode audio segments repeatedly
/// while the user is still speaking (Parakeet). The simulated-streaming
/// session calls this instead of <see cref="ISpeechToTextProvider.TranscribeAsync"/>
/// because segment decodes must stay raw — dictionary replacements are applied
/// once, over the joined final transcript.
/// </summary>
public interface IStreamingSpeechToTextProvider : ISpeechToTextProvider
{
    /// <summary>Decodes one audio segment to raw text (no dictionary post-processing).</summary>
    Task<string> TranscribeSegmentAsync(AudioBuffer segment, SttOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Push-mode voice activity detection over 16 kHz mono samples. One utterance
/// at a time: <see cref="Reset"/> is called at the start of each dictation.
/// Closed speech segments become available as the speaker pauses; the session
/// decodes those once and re-decodes only the open tail.
/// </summary>
public interface IVoiceActivityDetector
{
    /// <summary>False when the native VAD runtime or model cannot load on this device.</summary>
    bool IsAvailable { get; }

    /// <summary>True while the most recent samples look like speech.</summary>
    bool IsSpeechActive { get; }

    /// <summary>Clears all detector state for a new utterance.</summary>
    void Reset();

    /// <summary>Feeds newly captured samples.</summary>
    void Accept(ReadOnlyMemory<float> samples);

    /// <summary>Closes any in-progress speech segment (end of utterance).</summary>
    void Flush();

    /// <summary>Pops the next closed speech segment, if one is ready.</summary>
    bool TryPopSegment(out VadSpeechSegment segment);
}

/// <summary>A closed speech segment: where it started in the utterance and its samples.</summary>
public readonly record struct VadSpeechSegment(int StartSample, float[] Samples);

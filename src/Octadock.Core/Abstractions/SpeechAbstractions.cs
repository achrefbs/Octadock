namespace Octadock.Core.Abstractions;

/// <summary>
/// A speech-to-text engine. The current default is local Parakeet after its
/// model is available, with local Whisper fallback and explicit opt-in cloud
/// providers implementing the same contract.
/// </summary>
public interface ISpeechToTextProvider
{
    /// <summary>Engine id persisted in settings ("parakeet", "whisper", "openai", ...).</summary>
    string Id { get; }

    /// <summary>False when the engine cannot run here (e.g. the model is not downloaded yet).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Transcribes a complete utterance. Toggle/push-to-talk records first, then
    /// transcribes the buffered audio in one pass.
    /// </summary>
    Task<SttResult> TranscribeAsync(AudioBuffer audio, SttOptions options, CancellationToken cancellationToken);
}

/// <summary>Describes whether a speech provider can run in the current environment.</summary>
/// <param name="Id">Provider id persisted in settings.</param>
/// <param name="DisplayName">Human-readable provider name for settings UI.</param>
/// <param name="IsAvailable">True when the provider can transcribe now.</param>
/// <param name="Reason">Explanation when unavailable.</param>
public sealed record SpeechProviderDescription(
    string Id,
    string DisplayName,
    bool IsAvailable,
    string? Reason = null);

/// <summary>Resolves speech providers by the user's selected settings id.</summary>
public interface ISpeechToTextProviderFactory
{
    /// <summary>Returns the selected provider, or the build default if the id is unknown.</summary>
    ISpeechToTextProvider? Resolve(string providerId);

    /// <summary>Lists providers exposed by this build with current availability.</summary>
    IReadOnlyList<SpeechProviderDescription> Describe();
}

/// <summary>
/// PCM audio in the engine's canonical format: 16 kHz, mono, 32-bit float.
/// The capture service owns resampling so every engine sees the same shape.
/// </summary>
/// <param name="Samples">Interleaved-free mono samples in the range -1..1.</param>
/// <param name="SampleRate">Sample rate in Hz; the canonical value is 16 kHz.</param>
public sealed record AudioBuffer(float[] Samples, int SampleRate = 16_000)
{
    /// <summary>Length of the buffered audio.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}

/// <summary>Per-utterance transcription options.</summary>
public sealed record SttOptions
{
    /// <summary>BCP-47-ish language hint ("en", "es"); null = auto-detect.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// Dictation dictionary: ordered spoken-form → written-form replacements
    /// applied to the transcript ("arrow function" → "=&gt;"). Longest spoken form
    /// wins so more specific phrases take precedence.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Replacements { get; init; } = [];

    /// <summary>Model variant for engines that have one ("base", "small").</summary>
    public string? Model { get; init; }
}

/// <summary>The outcome of a transcription.</summary>
/// <param name="Text">The recognized text, post-dictionary.</param>
/// <param name="DetectedLanguage">The language the engine detected, when it reports one.</param>
/// <param name="AudioDuration">The length of the audio that produced this result.</param>
public sealed record SttResult(string Text, string? DetectedLanguage, TimeSpan AudioDuration)
{
    /// <summary>True when nothing usable was recognized.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

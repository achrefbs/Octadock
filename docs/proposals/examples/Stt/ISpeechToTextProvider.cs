// Example implementation for the STT roadmap (docs/proposals/vibe-coding-toolkit-roadmap.md).
// Target location: src/Octadock.Core/Abstractions/ — engine-agnostic contracts,
// mirroring the IOcrProvider / IOcrProviderFactory pattern already in the codebase.

namespace Octadock.Core.Abstractions;

/// <summary>
/// A speech-to-text engine. Implementations: WhisperSttProvider (local,
/// default), WindowsSttProvider (zero-dependency fallback), CloudSttProvider
/// (opt-in). Selected through a factory by settings, like OCR providers.
/// </summary>
public interface ISpeechToTextProvider
{
    /// <summary>Engine id persisted in settings ("whisper", "windows", "cloud").</summary>
    string Id { get; }

    /// <summary>False when the engine cannot run here (e.g. model not downloaded yet).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Transcribes a complete utterance. Push-to-talk/toggle records first,
    /// then transcribes; engines that support streaming can also implement
    /// <see cref="IStreamingSpeechToTextProvider"/> for live partials.
    /// </summary>
    Task<SttResult> TranscribeAsync(AudioBuffer audio, SttOptions options, CancellationToken cancellationToken);
}

/// <summary>Optional streaming capability (phase 2 — live partials in the HUD).</summary>
public interface IStreamingSpeechToTextProvider : ISpeechToTextProvider
{
    IAsyncEnumerable<SttPartial> TranscribeStreamingAsync(
        IAsyncEnumerable<AudioBuffer> chunks, SttOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// PCM audio in the engine's canonical format: 16 kHz, mono, 32-bit float.
/// The capture service owns resampling so every engine sees the same shape.
/// </summary>
public sealed record AudioBuffer(float[] Samples, int SampleRate = 16_000)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}

public sealed record SttOptions
{
    /// <summary>BCP-47-ish language hint ("en", "es"); null = auto-detect.</summary>
    public string? Language { get; init; }

    /// <summary>Dictation dictionary: ordered spoken-form → written-form replacements
    /// applied to the transcript ("arrow function" → "=>", "camel case X" …).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Replacements { get; init; } = [];

    /// <summary>Model variant for engines that have one ("base", "small").</summary>
    public string? Model { get; init; }
}

public sealed record SttResult(string Text, string? DetectedLanguage, TimeSpan AudioDuration)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

/// <summary>A streaming hypothesis; <see cref="IsFinal"/> marks a settled segment.</summary>
public sealed record SttPartial(string Text, bool IsFinal);

/// <summary>Where the transcript lands. Default: paste at cursor.</summary>
public enum SttInsertMode
{
    PasteAtCursor = 0,
    ClipboardOnly,
    ShelfItem,
}

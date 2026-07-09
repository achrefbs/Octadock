namespace Octadock.Core.Abstractions;

/// <summary>
/// Turns raw text into a concise spoken explanation. The initial implementation
/// shells out to user-installed AI CLIs; future cloud APIs can implement the same
/// seam with explicit consent.
/// </summary>
public interface ITextExplanationProvider
{
    /// <summary>Provider id used in diagnostics and settings.</summary>
    string Id { get; }

    /// <summary>True when the provider can run now.</summary>
    bool IsAvailable { get; }

    /// <summary>Explains the supplied source text in a form suitable for text-to-speech.</summary>
    Task<TextExplanationResult> ExplainAsync(
        TextExplanationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Input to the text explanation provider.</summary>
public sealed record TextExplanationRequest
{
    /// <summary>The raw source text to understand.</summary>
    public required string Text { get; init; }

    /// <summary>Optional source label, such as a file name or "clipboard".</summary>
    public string? SourceName { get; init; }

    /// <summary>Requested style, for example "explain", "brief", or "study".</summary>
    public string Style { get; init; } = "explain";

    /// <summary>Requested answer length: "short", "medium", or "long".</summary>
    public string Length { get; init; } = "medium";

    /// <summary>Optional AI CLI preference, for example "codex" or "claude".</summary>
    public string? ProviderPreference { get; init; }
}

/// <summary>The generated explanation and the provider that produced it.</summary>
public sealed record TextExplanationResult(string Text, string ProviderId);

/// <summary>Converts text into an audio file suitable for local playback.</summary>
public interface ITextToSpeechProvider
{
    /// <summary>Provider id used in diagnostics and settings.</summary>
    string Id { get; }

    /// <summary>True when the provider can synthesize now.</summary>
    bool IsAvailable { get; }

    /// <summary>Explanation shown when unavailable.</summary>
    string? UnavailableReason { get; }

    /// <summary>Synthesizes the supplied text into an audio file.</summary>
    Task<SynthesizedSpeech> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Input to a text-to-speech provider.</summary>
public sealed record TextToSpeechRequest
{
    /// <summary>Text to speak.</summary>
    public required string Text { get; init; }

    /// <summary>Provider-specific voice id. Null means provider default.</summary>
    public string? VoiceId { get; init; }

    /// <summary>Provider-specific model id. Null means provider default.</summary>
    public string? ModelId { get; init; }

    /// <summary>Speaking rate multiplier (1.0 = normal); null means provider default.</summary>
    public double? Rate { get; init; }
}

/// <summary>Result of a text-to-speech synthesis request.</summary>
public sealed record SynthesizedSpeech(string FilePath, TimeSpan? Duration = null);

/// <summary>Local audio playback for generated speech files.</summary>
public interface IAudioPlaybackService
{
    /// <summary>True while generated speech is currently playing.</summary>
    bool IsPlaying { get; }

    /// <summary>True while playback exists but is paused.</summary>
    bool IsPaused { get; }

    /// <summary>Stops current playback, if any, and plays the supplied audio file.</summary>
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Pauses current playback (no-op when nothing is playing).</summary>
    void Pause();

    /// <summary>Resumes paused playback (no-op otherwise).</summary>
    void Resume();

    /// <summary>Stops current playback, if any.</summary>
    void Stop();
}

using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Windows.Media.SpeechSynthesis;

namespace Octadock.Platform.Windows.Tts;

/// <summary>
/// Local, zero-download text-to-speech over the built-in Windows voices
/// (Windows.Media.SpeechSynthesis). This is the default read-aloud engine:
/// fully offline, starts speaking in well under a second, and needs no API
/// key. Voice selection matches the requested id/name fragment against the
/// installed voices; the speaking rate maps to the synthesizer's rate option.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsTtsProvider : ITextToSpeechProvider
{
    private readonly IStoragePaths _paths;
    private readonly ILogger<WindowsTtsProvider> _logger;

    /// <summary>Creates the Windows TTS provider.</summary>
    public WindowsTtsProvider(IStoragePaths paths, ILogger<WindowsTtsProvider> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => "windows";

    /// <inheritdoc />
    public bool IsAvailable => SpeechSynthesizer.AllVoices.Count > 0;

    /// <inheritdoc />
    public string? UnavailableReason => IsAvailable
        ? null
        : "No Windows text-to-speech voices are installed.";

    /// <inheritdoc />
    public async Task<SynthesizedSpeech> SynthesizeAsync(
        TextToSpeechRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var synthesizer = new SpeechSynthesizer();
        VoiceInformation? voice = PickVoice(request.VoiceId);
        if (voice is not null)
        {
            synthesizer.Voice = voice;
        }

        if (request.Rate is { } rate)
        {
            // SpeakingRate accepts 0.5–6.0; our settings clamp to 0.5–3.0.
            synthesizer.Options.SpeakingRate = Math.Clamp(rate, 0.5, 3.0);
        }

        SpeechSynthesisStream synthesized =
            await synthesizer.SynthesizeTextToStreamAsync(request.Text).AsTask(cancellationToken)
                .ConfigureAwait(false);

        string folder = Path.Combine(_paths.TempExportsDirectory, "ReadAloud");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"octadock-read-{Guid.NewGuid():N}.wav");

        await using (var destination = File.Create(path))
        {
            await synthesized.AsStreamForRead().CopyToAsync(destination, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Windows TTS synthesized {Chars} chars with voice {Voice}.",
            request.Text.Length,
            voice?.DisplayName ?? "default");
        return new SynthesizedSpeech(path);
    }

    /// <summary>
    /// Resolves a voice by exact id, then by case-insensitive display-name
    /// fragment ("Zira", "en-GB"), then null for the system default.
    /// </summary>
    private static VoiceInformation? PickVoice(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        string needle = requested.Trim();
        IReadOnlyList<VoiceInformation> voices = SpeechSynthesizer.AllVoices;
        return voices.FirstOrDefault(v => string.Equals(v.Id, needle, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault(v => v.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault(v => v.Language.StartsWith(needle, StringComparison.OrdinalIgnoreCase));
    }
}

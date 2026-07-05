using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;

namespace Octadock.Platform.Windows.Stt;

/// <summary>
/// Resolves the configured speech provider and reports availability. Parakeet
/// is the default engine; whenever its native runtime cannot load on this
/// device, resolution quietly degrades to local Whisper so dictation always
/// has an offline path.
/// </summary>
public sealed class SpeechToTextProviderFactory : ISpeechToTextProviderFactory
{
    private readonly ParakeetSttProvider _parakeet;
    private readonly WhisperSttProvider _whisper;
    private readonly OpenAiSttProvider _openAi;
    private readonly ILogger<SpeechToTextProviderFactory> _logger;

    /// <summary>Creates the provider factory.</summary>
    public SpeechToTextProviderFactory(
        ParakeetSttProvider parakeet,
        WhisperSttProvider whisper,
        OpenAiSttProvider openAi,
        ILogger<SpeechToTextProviderFactory> logger)
    {
        _parakeet = parakeet;
        _whisper = whisper;
        _openAi = openAi;
        _logger = logger;
    }

    /// <inheritdoc />
    public ISpeechToTextProvider? Resolve(string providerId)
    {
        if (string.Equals(providerId, _openAi.Id, StringComparison.OrdinalIgnoreCase))
        {
            return _openAi;
        }

        if (string.Equals(providerId, _whisper.Id, StringComparison.OrdinalIgnoreCase))
        {
            return _whisper;
        }

        if (!string.Equals(providerId, _parakeet.Id, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(providerId))
        {
            _logger.LogWarning(
                "Unknown speech provider '{Provider}'; using the default local engine.", providerId);
        }

        if (!_parakeet.IsAvailable)
        {
            _logger.LogWarning(
                "Parakeet native runtime is unavailable on this device; using {Fallback}.", _whisper.Id);
            return _whisper;
        }

        return _parakeet;
    }

    /// <inheritdoc />
    public IReadOnlyList<SpeechProviderDescription> Describe()
    {
        return
        [
            new SpeechProviderDescription(
                _parakeet.Id,
                "Local Parakeet",
                _parakeet.IsAvailable,
                _parakeet.IsAvailable
                    ? "Fast, accurate local engine (25 European languages, ~640 MB one-time download)."
                    : _parakeet.UnavailableReason),
            new SpeechProviderDescription(
                _whisper.Id,
                "Local Whisper",
                true,
                "Local fallback engine covering 99 languages; slower than Parakeet."),
            new SpeechProviderDescription(
                _openAi.Id,
                "OpenAI",
                _openAi.IsAvailable,
                _openAi.UnavailableReason),
        ];
    }
}

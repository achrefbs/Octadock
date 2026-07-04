using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;

namespace Octadock.Platform.Windows.Stt;

/// <summary>Resolves the configured speech provider and reports availability.</summary>
public sealed class SpeechToTextProviderFactory : ISpeechToTextProviderFactory
{
    private readonly WhisperSttProvider _whisper;
    private readonly OpenAiSttProvider _openAi;
    private readonly ILogger<SpeechToTextProviderFactory> _logger;

    /// <summary>Creates the provider factory.</summary>
    public SpeechToTextProviderFactory(
        WhisperSttProvider whisper,
        OpenAiSttProvider openAi,
        ILogger<SpeechToTextProviderFactory> logger)
    {
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

        if (string.Equals(providerId, _whisper.Id, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(providerId))
        {
            return _whisper;
        }

        _logger.LogWarning("Unknown speech provider '{Provider}'; falling back to {Fallback}.", providerId, _whisper.Id);
        return _whisper;
    }

    /// <inheritdoc />
    public IReadOnlyList<SpeechProviderDescription> Describe()
    {
        return
        [
            new SpeechProviderDescription(
                _whisper.Id,
                "Local Whisper",
                true,
                "Downloads/runs a local ggml model. Private, but slower and less accurate in this alpha."),
            new SpeechProviderDescription(
                _openAi.Id,
                "OpenAI",
                _openAi.IsAvailable,
                _openAi.UnavailableReason),
        ];
    }
}

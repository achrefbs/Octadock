using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.Tts;

/// <summary>
/// ElevenLabs text-to-speech provider. The API key is read from
/// OCTADOCK_ELEVENLABS_API_KEY, the legacy SNAPDOCK_ELEVENLABS_API_KEY, or
/// ELEVENLABS_API_KEY; Octadock does not persist it in the settings database.
/// </summary>
public sealed partial class ElevenLabsTtsProvider : ITextToSpeechProvider
{
    private const string OctadockApiKeyEnvironmentVariable = "OCTADOCK_ELEVENLABS_API_KEY";
    private const string LegacySnapDockApiKeyEnvironmentVariable = "SNAPDOCK_ELEVENLABS_API_KEY";
    private const string ElevenLabsApiKeyEnvironmentVariable = "ELEVENLABS_API_KEY";
    private const string OctadockVoiceEnvironmentVariable = "OCTADOCK_ELEVENLABS_VOICE_ID";
    private const string LegacySnapDockVoiceEnvironmentVariable = "SNAPDOCK_ELEVENLABS_VOICE_ID";
    private const string ElevenLabsVoiceEnvironmentVariable = "ELEVENLABS_VOICE_ID";
    private const string OctadockModelEnvironmentVariable = "OCTADOCK_ELEVENLABS_MODEL_ID";
    private const string LegacySnapDockModelEnvironmentVariable = "SNAPDOCK_ELEVENLABS_MODEL_ID";
    private const string ElevenLabsModelEnvironmentVariable = "ELEVENLABS_MODEL_ID";
    private const string DefaultVoiceId = "pNInz6obpgDQGcFmaJgB"; // Adam, from ElevenLabs examples.
    private const string DefaultModelId = "eleven_flash_v2_5";
    private const string OutputFormat = "mp3_44100_128";
    private const int MaxSpeechCharacters = 39_000;

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };

    private readonly IStoragePaths _paths;
    private readonly ILogger<ElevenLabsTtsProvider> _logger;

    /// <summary>Creates the ElevenLabs provider.</summary>
    public ElevenLabsTtsProvider(IStoragePaths paths, ILogger<ElevenLabsTtsProvider> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Id => "elevenlabs";

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ReadApiKey());

    /// <inheritdoc />
    public string? UnavailableReason => IsAvailable
        ? null
        : $"Set {OctadockApiKeyEnvironmentVariable}, {LegacySnapDockApiKeyEnvironmentVariable}, or {ElevenLabsApiKeyEnvironmentVariable}.";

    /// <inheritdoc />
    public async Task<SynthesizedSpeech> SynthesizeAsync(
        TextToSpeechRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? apiKey = ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(UnavailableReason ?? "ElevenLabs API key is not configured.");
        }

        string text = NormalizeSpeechText(request.Text);
        if (text.Length == 0)
        {
            throw new InvalidOperationException("There is no explanation to speak.");
        }

        string voiceId = FirstNonEmpty(
            request.VoiceId,
            Environment.GetEnvironmentVariable(OctadockVoiceEnvironmentVariable),
            Environment.GetEnvironmentVariable(LegacySnapDockVoiceEnvironmentVariable),
            Environment.GetEnvironmentVariable(ElevenLabsVoiceEnvironmentVariable),
            DefaultVoiceId);
        string modelId = FirstNonEmpty(
            request.ModelId,
            Environment.GetEnvironmentVariable(OctadockModelEnvironmentVariable),
            Environment.GetEnvironmentVariable(LegacySnapDockModelEnvironmentVariable),
            Environment.GetEnvironmentVariable(ElevenLabsModelEnvironmentVariable),
            DefaultModelId);

        string url =
            $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}?output_format={OutputFormat}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.TryAddWithoutValidation("xi-api-key", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                text,
                model_id = modelId,
            }),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await Client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        byte[] audio = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string body = DecodeForDisplay(audio);
            throw new InvalidOperationException(
                $"ElevenLabs TTS failed ({(int)response.StatusCode} {response.StatusCode}): {TrimForDisplay(body)}");
        }

        string path = BuildOutputPath();
        await File.WriteAllBytesAsync(path, audio, cancellationToken).ConfigureAwait(false);
        LogGeneratedSpeech(_logger, voiceId, modelId, audio.Length);

        return new SynthesizedSpeech(path);
    }

    private string BuildOutputPath()
    {
        _paths.EnsureDirectories();
        string folder = Path.Combine(_paths.TempExportsDirectory, "ReadAloud");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.mp3");
    }

    private static string? ReadApiKey()
    {
        string? key = Environment.GetEnvironmentVariable(OctadockApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        key = Environment.GetEnvironmentVariable(LegacySnapDockApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        return Environment.GetEnvironmentVariable(ElevenLabsApiKeyEnvironmentVariable);
    }

    private static string NormalizeSpeechText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();
        return trimmed.Length <= MaxSpeechCharacters
            ? trimmed
            : trimmed[..MaxSpeechCharacters] + "\n\nThe explanation was shortened before speech synthesis.";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string DecodeForDisplay(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return $"<binary {bytes.Length} bytes>";
        }
    }

    private static string TrimForDisplay(string value)
    {
        value = WebUtility.HtmlDecode(value).Trim();
        return value.Length <= 500
            ? value
            : value[..500] + "...";
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Generated ElevenLabs speech with voice {VoiceId}, model {ModelId}; {Bytes} bytes.")]
    private static partial void LogGeneratedSpeech(ILogger logger, string voiceId, string modelId, int bytes);
}

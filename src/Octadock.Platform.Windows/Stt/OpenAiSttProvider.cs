using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Octadock.Core.Speech;

namespace Octadock.Platform.Windows.Stt;

/// <summary>
/// Opt-in cloud transcription through OpenAI. The API key is read ONLY from the
/// Octadock-scoped OCTADOCK_OPENAI_API_KEY variable (WS7, R7): a bare
/// OPENAI_API_KEY that other tools set is never silently adopted, so audio is
/// only sent to the cloud when the user deliberately opts in. Octadock never
/// stores the key in local settings.
/// </summary>
public sealed class OpenAiSttProvider : ISpeechToTextProvider
{
    // Deliberately NOT read as a key source — only detected so we can tell a user
    // who set the generic variable how to opt in explicitly.
    private const string BareOpenAiApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string OctadockApiKeyEnvironmentVariable = "OCTADOCK_OPENAI_API_KEY";
    private const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(90),
    };

    private readonly ILogger<OpenAiSttProvider> _logger;

    /// <summary>Creates the OpenAI speech provider.</summary>
    public OpenAiSttProvider(ILogger<OpenAiSttProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => SpeechSettings.OpenAiProvider;

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(ReadApiKey());

    /// <summary>Reason shown in settings when the provider cannot run.</summary>
    public string? UnavailableReason
    {
        get
        {
            if (IsAvailable)
            {
                return null;
            }

            // A generic OPENAI_API_KEY is intentionally ignored; point the user at
            // the explicit opt-in variable rather than adopting their key silently.
            bool bareKeyPresent = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(BareOpenAiApiKeyEnvironmentVariable));
            return bareKeyPresent
                ? $"Set {OctadockApiKeyEnvironmentVariable} to enable cloud transcription " +
                  $"({BareOpenAiApiKeyEnvironmentVariable} is ignored so audio is never sent without your opt-in)."
                : $"Set {OctadockApiKeyEnvironmentVariable}.";
        }
    }

    /// <inheritdoc />
    public async Task<SttResult> TranscribeAsync(
        AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
    {
        string? apiKey = ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(UnavailableReason ?? "OpenAI API key is not configured.");
        }

        if (audio.Samples.Length == 0)
        {
            return new SttResult(string.Empty, null, TimeSpan.Zero);
        }

        string model = NormalizeModel(options.Model);
        byte[] wave = EncodeWavePcm16(audio);
        Stopwatch stopwatch = Stopwatch.StartNew();

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(model, Encoding.UTF8), "model");
        form.Add(new StringContent("text", Encoding.UTF8), "response_format");
        form.Add(new StringContent(
            "Accurate developer dictation. Preserve code terms, punctuation names, filenames, commands, and short prompts.",
            Encoding.UTF8),
            "prompt");

        if (!string.IsNullOrWhiteSpace(options.Language))
        {
            form.Add(new StringContent(options.Language.Trim(), Encoding.UTF8), "language");
        }

        var audioContent = new ByteArrayContent(wave);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "file", "octadock-dictation.wav");

        request.Content = form;

        using HttpResponseMessage response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenAI transcription failed ({(int)response.StatusCode}): {TrimForDisplay(body)}");
        }

        stopwatch.Stop();
        string transcript = TranscriptDictionary.Apply(body.Trim(), options.Replacements);
        _logger.LogInformation(
            "Dictation transcribed {AudioSeconds:0.0}s with OpenAI model {Model}; inference {ElapsedMs} ms.",
            audio.Duration.TotalSeconds,
            model,
            stopwatch.ElapsedMilliseconds);

        return new SttResult(transcript, options.Language, audio.Duration);
    }

    private static string? ReadApiKey()
        // Only the Octadock-scoped variable enables cloud transcription. A bare
        // OPENAI_API_KEY (set by many other dev tools) is never used as a source,
        // so Octadock cannot silently start sending audio to the cloud (WS7, R7).
        => Environment.GetEnvironmentVariable(OctadockApiKeyEnvironmentVariable);

    private static string NormalizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return SpeechSettings.DefaultOpenAiModel;
        }

        return model.Trim() switch
        {
            "gpt-4o-transcribe" => "gpt-4o-transcribe",
            "gpt-4o-mini-transcribe" => "gpt-4o-mini-transcribe",
            "whisper-1" => "whisper-1",
            _ => SpeechSettings.DefaultOpenAiModel,
        };
    }

    private static byte[] EncodeWavePcm16(AudioBuffer audio)
    {
        int sampleRate = audio.SampleRate;
        const short channels = 1;
        const short bitsPerSample = 16;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataLength = audio.Samples.Length * blockAlign;

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        foreach (float sample in audio.Samples)
        {
            float clamped = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)Math.Round(clamped * short.MaxValue, MidpointRounding.AwayFromZero));
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static string TrimForDisplay(string value)
    {
        value = value.Trim();
        return value.Length <= 500
            ? value
            : value[..500] + "...";
    }
}

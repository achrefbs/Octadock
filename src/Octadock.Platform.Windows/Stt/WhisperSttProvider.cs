using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using Whisper.net;

namespace Octadock.Platform.Windows.Stt;

/// <summary>
/// Local transcription with whisper.cpp via the Whisper.net bindings after the
/// selected model is available. Two lifecycle rules make it feel good:
/// <list type="number">
///   <item>Download the ggml model once, with progress, into
///   <see cref="IStoragePaths.RootDirectory"/>\models.</item>
///   <item>Keep the <see cref="WhisperFactory"/> resident after first use —
///   building it is the slow part (seconds); per-utterance processors are cheap.</item>
/// </list>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WhisperSttProvider : IModelBackedSpeechProvider, ILocalSpeechModelImport, IDisposable
{
    private const string DefaultModel = SpeechSettings.DefaultWhisperModel;

    private readonly IStoragePaths _paths;
    private readonly ILogger<WhisperSttProvider> _logger;
    private readonly SemaphoreSlim _factoryGate = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    /// <summary>Creates the Whisper provider.</summary>
    public WhisperSttProvider(IStoragePaths paths, ILogger<WhisperSttProvider> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Id => "whisper";

    /// <inheritdoc />
    public bool IsAvailable => IsModelAvailable(DefaultModel);

    /// <summary>Returns whether a specific ggml model variant is already available locally.</summary>
    public bool IsModelAvailable(string? model)
        => File.Exists(ModelPath(NormalizeModel(model)));

    /// <inheritdoc />
    public long ModelDownloadBytes(string? model)
        => ApproximateModelBytes(NormalizeModel(model));

    /// <inheritdoc />
    public void DeleteModel(string? model)
    {
        string path = ModelPath(NormalizeModel(model));
        _factoryGate.Wait();
        try
        {
            if (_loadedModelPath == path)
            {
                _factory?.Dispose();
                _factory = null;
                _loadedModelPath = null;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _factoryGate.Release();
        }
    }

    private string ModelPath(string model)
        => Path.Combine(_paths.RootDirectory, "models", $"ggml-{model}.bin");

    /// <summary>
    /// Ensures the ggml model exists locally, downloading with progress on first
    /// use (~142 MB for "base"). The dictation controller and a future Settings →
    /// Speech to text picker both call this before transcribing.
    /// </summary>
    public Task EnsureModelAsync(string? model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsModelAvailable(model)) throw new FileNotFoundException(
            "Import a Whisper GGML model in Settings → Voice → Advanced. Octadock does not download models.");
        progress?.Report(1);
        return Task.CompletedTask;
    }

    public bool ImportUsesFolder => false;

    public async Task ImportModelAsync(string? model, string source, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        source = Path.GetFullPath(source);
        if (source.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose a file on a local drive.", nameof(source));
        string path = ModelPath(NormalizeModel(model));
        if (string.Equals(path, source, StringComparison.OrdinalIgnoreCase)) return;
        await _factoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string staging = path + ".import-" + Guid.NewGuid().ToString("N");
        try
        {
            await using FileStream input = File.OpenRead(source);
            byte[] magic = new byte[4];
            await input.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
            if (BitConverter.ToUInt32(magic) != 0x67676d6c || input.Length < 1024)
                throw new InvalidDataException("This is not a complete Whisper GGML model.");
            input.Position = 0;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (FileStream output = File.Create(staging))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (_loadedModelPath == path)
            {
                _factory?.Dispose();
                _factory = null;
                _loadedModelPath = null;
            }
            File.Move(staging, path, overwrite: true);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
            _factoryGate.Release();
        }
    }

    private static long ApproximateModelBytes(string model)
        => model switch
        {
            "tiny" => 75L * 1024 * 1024,
            "tiny.en" => 75L * 1024 * 1024,
            "base.en" => 142L * 1024 * 1024,
            "small" => 466L * 1024 * 1024,
            "small.en" => 466L * 1024 * 1024,
            "medium" => 1_500L * 1024 * 1024,
            "medium.en" => 1_500L * 1024 * 1024,
            _ => 142L * 1024 * 1024,
        };

    /// <inheritdoc />
    /// <remarks>
    /// CPU-bound and blocking (factory build takes seconds on first use, Whisper
    /// inference is synchronous under the async surface). By contract this must be
    /// called off the UI thread — the dictation controller wraps it in Task.Run.
    /// Nothing here touches the WPF Dispatcher, so it never marshals back.
    /// </remarks>
    public async Task<SttResult> TranscribeAsync(
        AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
    {
        AudioBuffer utterance = NormalizeForRecognition(TrimSilence(audio), out AudioDiagnostics diagnostics);
        if (utterance.Samples.Length == 0)
        {
            return new SttResult(string.Empty, null, TimeSpan.Zero);
        }

        string model = NormalizeModel(options.Model ?? DefaultModel);
        WhisperFactory factory = await GetFactoryAsync(model, cancellationToken)
            .ConfigureAwait(false);

        WhisperProcessorBuilder builder = factory.CreateBuilder()
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 2, 8))
            .WithProbabilities()
            .WithNoSpeechThreshold(0.55f)
            .WithLogProbThreshold(-1.0f)
            .WithEntropyThreshold(2.4f)
            .WithPrompt("Accurate dictation. Preserve natural speech, developer terms, punctuation names, file names, and commands when spoken.");
        builder = options.Language is null
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(options.Language);

        await using WhisperProcessor processor = builder.Build();

        var text = new global::System.Text.StringBuilder();
        string? language = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        await foreach (SegmentData segment in processor.ProcessAsync(utterance.Samples, cancellationToken)
            .ConfigureAwait(false))
        {
            text.Append(segment.Text);
            language ??= segment.Language;
        }

        stopwatch.Stop();

        string transcript = TranscriptDictionary.Apply(text.ToString().Trim(), options.Replacements);
        _logger.LogInformation(
            "Dictation transcribed {AudioSeconds:0.0}s with model {Model} and language {Language}; peak {Peak:0.000}, RMS {Rms:0.000}, gain {Gain:0.0}x, inference {ElapsedMs} ms.",
            utterance.Duration.TotalSeconds,
            model,
            options.Language ?? "auto",
            diagnostics.Peak,
            diagnostics.Rms,
            diagnostics.Gain,
            stopwatch.ElapsedMilliseconds);
        return new SttResult(transcript, language, utterance.Duration);
    }

    private async Task<WhisperFactory> GetFactoryAsync(string model, CancellationToken cancellationToken)
    {
        model = NormalizeModel(model);
        string path = ModelPath(model);
        if (_factory is not null && _loadedModelPath == path)
        {
            return _factory;
        }

        await _factoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_factory is null || _loadedModelPath != path)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"The Whisper model '{model}' is not downloaded yet. Settings → Speech to text.", path);
                }

                _factory?.Dispose();
                _factory = WhisperFactory.FromPath(path); // The slow, resident part.
                _loadedModelPath = path;
            }

            return _factory;
        }
        finally
        {
            _factoryGate.Release();
        }
    }

    private static string NormalizeModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return DefaultModel;
        }

        return model.Trim().ToLowerInvariant() switch
        {
            "tiny" => "tiny",
            "tiny.en" => "tiny.en",
            "base" => "base",
            "base.en" => "base.en",
            "small" => "small",
            "small.en" => "small.en",
            "medium" => "medium",
            "medium.en" => "medium.en",
            _ => DefaultModel,
        };
    }

    private static AudioBuffer TrimSilence(AudioBuffer audio)
    {
        ReadOnlySpan<float> samples = audio.Samples;
        int start = 0;
        int end = samples.Length - 1;
        const float threshold = 0.006f;

        while (start < samples.Length && Math.Abs(samples[start]) < threshold)
        {
            start++;
        }

        while (end >= start && Math.Abs(samples[end]) < threshold)
        {
            end--;
        }

        if (start >= samples.Length || end < start)
        {
            return new AudioBuffer([], audio.SampleRate);
        }

        int padding = Math.Max(1, audio.SampleRate / 5);
        start = Math.Max(0, start - padding);
        end = Math.Min(samples.Length - 1, end + padding);
        int length = end - start + 1;
        if (start == 0 && length == samples.Length)
        {
            return audio;
        }

        var trimmed = new float[length];
        samples.Slice(start, length).CopyTo(trimmed);
        return new AudioBuffer(trimmed, audio.SampleRate);
    }

    private static AudioBuffer NormalizeForRecognition(AudioBuffer audio, out AudioDiagnostics diagnostics)
    {
        ReadOnlySpan<float> samples = audio.Samples;
        if (samples.Length == 0)
        {
            diagnostics = new AudioDiagnostics(0, 0, 1);
            return audio;
        }

        double sumSquares = 0;
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = samples[i];
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
        }

        float rms = (float)Math.Sqrt(sumSquares / samples.Length);
        float gain = CalculateRecognitionGain(peak, rms);
        diagnostics = new AudioDiagnostics(peak, rms, gain);

        if (gain <= 1.05f)
        {
            return audio;
        }

        var normalized = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            normalized[i] = Math.Clamp(samples[i] * gain, -0.98f, 0.98f);
        }

        return new AudioBuffer(normalized, audio.SampleRate);
    }

    private static float CalculateRecognitionGain(float peak, float rms)
    {
        if (peak <= 0 || rms <= 0)
        {
            return 1;
        }

        const float targetSpeechRms = 0.075f;
        const float maxGain = 5.0f;
        float gain = Math.Min(maxGain, targetSpeechRms / rms);
        gain = Math.Min(gain, 0.98f / peak);
        return Math.Max(1, gain);
    }

    private readonly record struct AudioDiagnostics(float Peak, float Rms, float Gain);

    /// <inheritdoc />
    private int _disposeState;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        _factoryGate.Wait();
        try { _factory?.Dispose(); _factory = null; }
        finally { _factoryGate.Release(); _factoryGate.Dispose(); }
    }
}

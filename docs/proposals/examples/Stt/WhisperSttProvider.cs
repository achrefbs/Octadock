// Example implementation for the STT roadmap.
// Target location: src/Octadock.Platform.Windows/Stt/ (or a new Octadock.Stt project)
// NuGet: Whisper.net + Whisper.net.Runtime (CPU; GPU runtimes are opt-in later).
//
// Local, fully offline transcription with whisper.cpp via the Whisper.net
// bindings. The two lifecycle rules that make it feel good:
//   1. Download the ggml model once, with progress, into StoragePaths.
//   2. Keep the WhisperFactory resident after first use — building it is the
//      slow part (seconds); per-utterance processors are cheap.

using System.IO;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Whisper.net;
using Whisper.net.Ggml;

namespace Octadock.Platform.Windows.Stt;

public sealed class WhisperSttProvider : ISpeechToTextProvider, IDisposable
{
    private readonly IStoragePaths _paths;
    private readonly ILogger<WhisperSttProvider> _logger;
    private readonly SemaphoreSlim _factoryGate = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    public WhisperSttProvider(IStoragePaths paths, ILogger<WhisperSttProvider> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string Id => "whisper";

    public bool IsAvailable => File.Exists(ModelPath("base"));

    private string ModelPath(string model)
        => Path.Combine(_paths.DataDirectory, "models", $"ggml-{model}.bin");

    /// <summary>
    /// Ensures the ggml model exists locally, downloading with progress on first
    /// use (~142 MB for "base"). Settings → STT calls this from the model picker.
    /// </summary>
    public async Task EnsureModelAsync(
        string model, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string path = ModelPath(model);
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        GgmlType type = model switch
        {
            "tiny" => GgmlType.Tiny,
            "small" => GgmlType.Small,
            "medium" => GgmlType.Medium,
            _ => GgmlType.Base,
        };

        _logger.LogInformation("Downloading Whisper model {Model}…", model);
        await using Stream source = await WhisperGgmlDownloader.Default
            .GetGgmlModelAsync(type, cancellationToken: cancellationToken);

        // Temp + atomic move: a cancelled download must not leave a truncated
        // model that fails to load forever after.
        string temp = path + ".partial";
        await using (var destination = File.Create(temp))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    public async Task<SttResult> TranscribeAsync(
        AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
    {
        if (audio.Samples.Length == 0)
        {
            return new SttResult(string.Empty, null, TimeSpan.Zero);
        }

        WhisperFactory factory = await GetFactoryAsync(options.Model ?? "base", cancellationToken)
            .ConfigureAwait(false);

        var builder = factory.CreateBuilder().WithThreads(Math.Max(2, Environment.ProcessorCount / 2));
        builder = options.Language is null
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(options.Language);

        await using WhisperProcessor processor = builder.Build();

        var text = new System.Text.StringBuilder();
        string? language = null;
        await foreach (SegmentData segment in processor.ProcessAsync(audio.Samples, cancellationToken))
        {
            text.Append(segment.Text);
            language ??= segment.Language;
        }

        string transcript = ApplyDictionary(text.ToString().Trim(), options.Replacements);
        return new SttResult(transcript, language, audio.Duration);
    }

    /// <summary>
    /// The dictation dictionary: what separates a coding STT from a generic one.
    /// Ordered longest-spoken-form-first so "arrow function body" beats "arrow function".
    /// </summary>
    private static string ApplyDictionary(
        string transcript, IReadOnlyList<KeyValuePair<string, string>> replacements)
    {
        foreach ((string spoken, string written) in replacements.OrderByDescending(r => r.Key.Length))
        {
            transcript = transcript.Replace(spoken, written, StringComparison.OrdinalIgnoreCase);
        }

        return transcript;
    }

    private async Task<WhisperFactory> GetFactoryAsync(string model, CancellationToken cancellationToken)
    {
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

    public void Dispose()
    {
        _factory?.Dispose();
        _factoryGate.Dispose();
    }
}

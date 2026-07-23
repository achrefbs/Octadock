using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using Octadock.Platform.Windows.Audio;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// Versioned corpus benchmark for the local Parakeet engine, gated behind
/// OCTADOCK_LIVE_STT=1 (shares the cached ~640 MB model download with the other
/// live tests). Driven entirely by tools/acceptance/stt/manifest.json plus the
/// SAPI-generated WAVs produced by New-SttCorpus.ps1:
/// <list type="bullet">
///   <item>OCTADOCK_STT_MANIFEST — path to the corpus manifest.</item>
///   <item>OCTADOCK_STT_CORPUS — directory with generated WAVs + corpus.generated.json.</item>
///   <item>OCTADOCK_STT_RESULTS — where the raw per-case JSON is written.</item>
/// </list>
/// Batch and simulated-streaming runs measure cold/warm start, partial and
/// finalize latency stages, RTF, CPU/memory, WER against the reference, and
/// dropped/duplicated segment integrity. Cases without a generatable voice are
/// written as pending rows — never fabricated. Invoke-SttBenchmark.ps1 wraps
/// this harness with the machine profile and the markdown summary.
/// </summary>
public sealed class SttCorpusBenchmarkTests
{
    private const string GateVariable = "OCTADOCK_LIVE_STT";
    private const int Rate = 16_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Corpus_benchmark_measures_accuracy_latency_and_streaming_integrity()
    {
        if (Environment.GetEnvironmentVariable(GateVariable) != "1")
        {
            return; // Live-only: opt in with OCTADOCK_LIVE_STT=1.
        }

        string manifestPath = RequireEnvironment("OCTADOCK_STT_MANIFEST");
        string corpusDirectory = RequireEnvironment("OCTADOCK_STT_CORPUS");
        string resultsPath = RequireEnvironment("OCTADOCK_STT_RESULTS");

        CorpusManifest manifest = CorpusManifest.Load(manifestPath);
        CorpusRecord record = CorpusRecord.Load(Path.Combine(corpusDirectory, "corpus.generated.json"));

        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Octadock");
        var paths = new LivePaths(root);
        using var store = new ParakeetModelStore(paths, NullLogger<ParakeetModelStore>.Instance);
        using var provider = new ParakeetSttProvider(store, NullLogger<ParakeetSttProvider>.Instance);
        using var vad = new SileroVoiceActivityDetector(paths, NullLogger<SileroVoiceActivityDetector>.Instance);

        provider.IsAvailable.Should().BeTrue(provider.UnavailableReason);
        vad.IsAvailable.Should().BeTrue("the Silero model is embedded and shares the sherpa runtime");

        Stopwatch ensureWatch = Stopwatch.StartNew();
        await provider.EnsureModelAsync(null, null, CancellationToken.None);
        ensureWatch.Stop();

        // Cold start: one-time recognizer build plus the first decode through it.
        Stopwatch buildWatch = Stopwatch.StartNew();
        await provider.PrepareAsync(null, CancellationToken.None);
        buildWatch.Stop();

        var caseRows = new List<Dictionary<string, object?>>();
        foreach (CorpusCase benchmarkCase in manifest.Cases)
        {
            caseRows.Add(await RunCaseAsync(benchmarkCase, record, corpusDirectory, provider, vad));
        }

        var measured = caseRows.Where(r => (string)r["status"]! == "measured").ToList();
        double medianBatchRtf = Median(measured.Select(r => (double)((Dictionary<string, object?>)r["batch"]!)["rtf"]!).ToArray());
        double? coldFirstDecodeMs = measured.Count > 0
            ? (double)((Dictionary<string, object?>)measured[0]["batch"]!)["decodeMs"]!
            : null;

        var timing = new Dictionary<string, object?>
        {
            ["modelEnsureMs"] = Math.Round(ensureWatch.Elapsed.TotalMilliseconds, 1),
            ["recognizerBuildMs"] = Math.Round(buildWatch.Elapsed.TotalMilliseconds, 1),
            ["coldFirstDecodeMs"] = coldFirstDecodeMs,
            ["medianBatchRtf"] = Math.Round(medianBatchRtf, 4),
        };
        timing["coldStartMs"] = Math.Round(
            buildWatch.Elapsed.TotalMilliseconds +
            (coldFirstDecodeMs ?? 0) + ensureWatch.Elapsed.TotalMilliseconds, 1);

        var output = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["kind"] = "stt-corpus-benchmark",
            ["generatedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["engine"] = "local Parakeet via sherpa-onnx (no cloud)",
            ["engineModel"] = SpeechSettings.DefaultParakeetModel,
            ["corpusSchemaVersion"] = manifest.SchemaVersion,
            ["timing"] = timing,
            ["cases"] = caseRows,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(resultsPath)!);
        await File.WriteAllTextAsync(
            resultsPath,
            JsonSerializer.Serialize(output, JsonOptions));

        // ---- Honest structural gates (accuracy/latency are reported, not faked) ----
        measured.Count.Should().BeGreaterThanOrEqualTo(15,
            "most of the manifest must be measurable on a default Windows en-US install");
        foreach (Dictionary<string, object?> row in measured)
        {
            var batch = (Dictionary<string, object?>)row["batch"]!;
            ((string)batch["transcript"]!).Should().NotBeNullOrWhiteSpace(
                $"case {row["id"]} synthesized speech must decode to something");
        }

        int dropped = measured
            .Where(r => r["streaming"] is not null)
            .Sum(r => (int)((Dictionary<string, object?>)r["streaming"]!)["segmentsDropped"]!);
        int duplicated = measured
            .Where(r => r["streaming"] is not null)
            .Sum(r => (int)((Dictionary<string, object?>)r["streaming"]!)["segmentsDuplicated"]!);
        dropped.Should().Be(0, "closed VAD segments must never be lost from the final transcript");
        duplicated.Should().Be(0, "closed VAD segments must never be decoded into the transcript twice");

        Dictionary<string, object?> natural = measured.Single(r => (string)r["id"]! == "en-natural-01");
        ((double)((Dictionary<string, object?>)natural["batch"]!)["wer"]!).Should().BeLessThanOrEqualTo(0.34,
            "the simplest reference sentence must stay well under one wrong word in three");
        medianBatchRtf.Should().BeLessThan(1.0,
            "the engine must decode faster than realtime on the benchmark machine");
    }

    private static async Task<Dictionary<string, object?>> RunCaseAsync(
        CorpusCase benchmarkCase,
        CorpusRecord record,
        string corpusDirectory,
        ParakeetSttProvider provider,
        SileroVoiceActivityDetector vad)
    {
        var row = new Dictionary<string, object?>
        {
            ["id"] = benchmarkCase.Id,
            ["category"] = benchmarkCase.Category,
        };

        CorpusRecordCase? generated = record.Cases.GetValueOrDefault(benchmarkCase.Id);
        if (generated is null || generated.Status != "ready" || generated.Wav is null)
        {
            row["status"] = "pending";
            row["reason"] = generated?.Reason ?? "The case was not generated (missing voice or audio).";
            return row;
        }

        string wavPath = Path.Combine(corpusDirectory, generated.Wav);
        float[] samples = ReadWav(wavPath);
        var audio = new AudioBuffer(samples);
        // Both passes run without dictionary replacements so WER measures the
        // engine, not the user's dictionary (which intentionally rewrites
        // words). The dictionary step itself is timed as final→insertion-ready
        // in the streaming pass.
        var options = new SttOptions
        {
            Language = benchmarkCase.ExpectedLanguage ?? "en",
            Replacements = [],
        };
        if (benchmarkCase.Category == "auto-language")
        {
            options = new SttOptions
            {
                Language = null, // Exercise the engine's own language detection.
                Replacements = [],
            };
        }

        row["status"] = "measured";
        row["voiceName"] = generated.VoiceName;
        row["voiceCulture"] = generated.VoiceCulture;
        row["audioSeconds"] = Math.Round(audio.Duration.TotalSeconds, 3);
        row["reference"] = benchmarkCase.Text;

        // ---- Batch pass ----
        Process process = Process.GetCurrentProcess();
        TimeSpan cpuBefore = process.TotalProcessorTime;
        long managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch batchWatch = Stopwatch.StartNew();
        SttResult batch = await provider.TranscribeAsync(audio, options, CancellationToken.None);
        batchWatch.Stop();
        TimeSpan cpuDelta = process.TotalProcessorTime - cpuBefore;
        long managedDelta = GC.GetTotalMemory(false) - managedBefore;
        process.Refresh();

        WordErrorRate batchWer = WordErrorRate.Compute(benchmarkCase.Text, batch.Text);
        row["batch"] = new Dictionary<string, object?>
        {
            ["transcript"] = batch.Text,
            ["decodeMs"] = Math.Round(batchWatch.Elapsed.TotalMilliseconds, 1),
            ["rtf"] = Math.Round(batchWatch.Elapsed.TotalSeconds / audio.Duration.TotalSeconds, 4),
            ["cpuMs"] = Math.Round(cpuDelta.TotalMilliseconds, 1),
            ["managedMemoryDeltaBytes"] = managedDelta,
            ["peakWorkingSetBytes"] = process.PeakWorkingSet64,
            ["wer"] = Math.Round(batchWer.Rate, 4),
            ["substitutions"] = batchWer.Substitutions,
            ["deletions"] = batchWer.Deletions,
            ["insertions"] = batchWer.Insertions,
            ["referenceWords"] = batchWer.ReferenceWords,
            ["exactMatch"] = IsExactMatch(benchmarkCase.Text, batch.Text),
        };

        // ---- Simulated-streaming pass ----
        row["streaming"] = await RunStreamingAsync(benchmarkCase, provider, vad, samples, options);
        return row;
    }

    private static async Task<Dictionary<string, object?>> RunStreamingAsync(
        CorpusCase benchmarkCase,
        ParakeetSttProvider provider,
        SileroVoiceActivityDetector vad,
        float[] samples,
        SttOptions options)
    {
        // The session runs WITHOUT dictionary replacements so the final text is
        // the raw segment join — that keeps the dropped/duplicated-segment
        // integrity check comparable (the dictionary intentionally rewrites
        // words, which would look like a lost segment). The dictionary is then
        // applied below as the measured final→insertion-ready step, matching
        // the production contract (replacements applied once at finalize).
        var streamingOptions = new SttOptions
        {
            Language = options.Language,
            Model = options.Model,
            Replacements = [],
        };
        var countingVad = new CountingVad(vad);
        var countingProvider = new CountingStreamingProvider(provider, () => countingVad.PoppedSegments);
        countingVad.Reset();
        using var session = new SimulatedStreamingSession(countingProvider, countingVad, streamingOptions);

        long firstPartialWallMs = -1;
        long firstStableWallMs = -1;
        double? firstPartialAudioSeconds = null;
        double? firstStableAudioSeconds = null;
        Stopwatch sessionWatch = Stopwatch.StartNew();
        session.PartialChanged += (_, e) =>
        {
            double fedSeconds = session.AudioDuration.TotalSeconds;
            if (firstPartialWallMs < 0 &&
                (!string.IsNullOrWhiteSpace(e.Stable) || !string.IsNullOrWhiteSpace(e.Volatile)))
            {
                firstPartialWallMs = sessionWatch.ElapsedMilliseconds;
                firstPartialAudioSeconds = fedSeconds;
            }

            if (firstStableWallMs < 0 && !string.IsNullOrWhiteSpace(e.Stable))
            {
                firstStableWallMs = sessionWatch.ElapsedMilliseconds;
                firstStableAudioSeconds = fedSeconds;
            }
        };

        // Feed like the audio pump does: 100 ms chunks, a tick every ~400 ms.
        for (int offset = 0; offset < samples.Length; offset += 1_600)
        {
            int length = Math.Min(1_600, samples.Length - offset);
            session.Accept(samples.AsMemory(offset, length));
            if (offset / 1_600 % 4 == 3)
            {
                await session.TickAsync(CancellationToken.None);
            }
        }

        await session.TickAsync(CancellationToken.None);

        Stopwatch finalizeWatch = Stopwatch.StartNew();
        SttResult final = await session.FinalizeAsync(CancellationToken.None);
        finalizeWatch.Stop();

        // Final transcript -> insertion-ready: join normalization + dictionary
        // replacements (the Core-side post-processing before the App inserts).
        Stopwatch postWatch = Stopwatch.StartNew();
        string insertionReady = TranscriptDictionary.Apply(final.Text, TranscriptDictionary.DefaultCodeDictionary);
        postWatch.Stop();

        // Segment-boundary integrity: every closed-segment decode must appear
        // exactly once in the final transcript (no lost or duplicated words).
        int closedDecodes = 0;
        int dropped = 0;
        int duplicated = 0;
        foreach (string text in countingProvider.ClosedSegmentDecodeTexts)
        {
            closedDecodes++;
            if (text.Length == 0)
            {
                continue;
            }

            int occurrences = CountOccurrences(final.Text, text);
            if (occurrences == 0)
            {
                dropped++;
            }
            else if (occurrences > 1)
            {
                duplicated += occurrences - 1;
            }
        }

        WordErrorRate streamingWer = WordErrorRate.Compute(benchmarkCase.Text, final.Text);
        return new Dictionary<string, object?>
        {
            ["transcript"] = final.Text,
            ["insertionReady"] = insertionReady,
            ["firstPartialAudioMs"] = firstPartialAudioSeconds is null
                ? null : (int)Math.Round(firstPartialAudioSeconds.Value * 1000),
            ["firstPartialWallMs"] = firstPartialWallMs < 0 ? null : firstPartialWallMs,
            ["firstStableAudioMs"] = firstStableAudioSeconds is null
                ? null : (int)Math.Round(firstStableAudioSeconds.Value * 1000),
            ["firstStableWallMs"] = firstStableWallMs < 0 ? null : firstStableWallMs,
            ["endOfSpeechToFinalMs"] = Math.Round(finalizeWatch.Elapsed.TotalMilliseconds, 1),
            ["finalToInsertionReadyMs"] = Math.Round(postWatch.Elapsed.TotalMilliseconds, 2),
            ["decodeTotalMs"] = Math.Round(countingProvider.TotalDecodeMs, 1),
            ["rtf"] = Math.Round(
                countingProvider.TotalDecodeMs / 1000.0 / (samples.Length / (double)Rate), 4),
            ["segmentsClosed"] = countingVad.SegmentsClosed,
            ["closedSegmentDecodes"] = closedDecodes,
            ["tailDecodes"] = countingProvider.TailDecodes,
            ["emptySegmentDecodes"] = countingProvider.EmptyClosedDecodes,
            ["segmentsDropped"] = dropped,
            ["segmentsDuplicated"] = duplicated,
            ["wer"] = Math.Round(streamingWer.Rate, 4),
            ["exactMatch"] = IsExactMatch(benchmarkCase.Text, final.Text),
        };
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static bool IsExactMatch(string reference, string transcript)
        => string.Equals(
            NormalizeWhitespace(reference),
            NormalizeWhitespace(transcript),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWhitespace(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        Array.Sort(values);
        return values.Length % 2 == 1
            ? values[values.Length / 2]
            : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
    }

    private static string RequireEnvironment(string name)
        => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException(
                $"{name} must point at the corpus inputs/outputs when {GateVariable}=1 " +
                "(Invoke-SttBenchmark.ps1 sets it).");

    private static float[] ReadWav(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new WaveFileReader(stream);
        ISampleProvider samplesProvider = reader.ToSampleProvider();
        if (samplesProvider.WaveFormat.Channels > 1)
        {
            samplesProvider = samplesProvider.ToMono();
        }

        if (samplesProvider.WaveFormat.SampleRate != Rate)
        {
            samplesProvider = new WdlResamplingSampleProvider(samplesProvider, Rate);
        }

        var collected = new List<float>(Rate * 8);
        var buffer = new float[Rate];
        int read;
        while ((read = samplesProvider.Read(buffer, 0, buffer.Length)) > 0)
        {
            collected.AddRange(buffer.Take(read));
        }

        return [.. collected];
    }

    /// <summary>Word error rate over normalized tokens (lowercase, edge punctuation stripped).</summary>
    private sealed record WordErrorRate(
        double Rate, int Substitutions, int Deletions, int Insertions, int ReferenceWords)
    {
        public static WordErrorRate Compute(string reference, string hypothesis)
        {
            string[] referenceWords = Tokenize(reference);
            string[] hypothesisWords = Tokenize(hypothesis);
            (int distance, int substitutions, int deletions, int insertions) =
                Align(referenceWords, hypothesisWords);
            _ = distance;
            double rate = referenceWords.Length == 0
                ? 0
                : (substitutions + deletions + insertions) / (double)referenceWords.Length;
            return new WordErrorRate(rate, substitutions, deletions, insertions, referenceWords.Length);
        }

        private static string[] Tokenize(string text)
        {
            return text
                .ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim(' ', '.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '…'))
                .Where(token => token.Length > 0)
                .ToArray();
        }

        // Classic Levenshtein alignment over word tokens, preferring
        // substitution > deletion > insertion on ties for stable counters.
        private static (int Distance, int Substitutions, int Deletions, int Insertions) Align(
            string[] reference, string[] hypothesis)
        {
            int[,] costs = new int[reference.Length + 1, hypothesis.Length + 1];
            for (int i = 0; i <= reference.Length; i++)
            {
                costs[i, 0] = i;
            }

            for (int j = 0; j <= hypothesis.Length; j++)
            {
                costs[0, j] = j;
            }

            for (int i = 1; i <= reference.Length; i++)
            {
                for (int j = 1; j <= hypothesis.Length; j++)
                {
                    int substitution = costs[i - 1, j - 1] +
                        (string.Equals(reference[i - 1], hypothesis[j - 1], StringComparison.Ordinal) ? 0 : 1);
                    costs[i, j] = Math.Min(substitution,
                        Math.Min(costs[i - 1, j] + 1, costs[i, j - 1] + 1));
                }
            }

            int substitutions = 0, deletions = 0, insertions = 0;
            int row = reference.Length, column = hypothesis.Length;
            while (row > 0 || column > 0)
            {
                if (row > 0 && column > 0 &&
                    string.Equals(reference[row - 1], hypothesis[column - 1], StringComparison.Ordinal) &&
                    costs[row, column] == costs[row - 1, column - 1])
                {
                    row--;
                    column--;
                }
                else if (row > 0 && column > 0 && costs[row, column] == costs[row - 1, column - 1] + 1)
                {
                    substitutions++;
                    row--;
                    column--;
                }
                else if (row > 0 && costs[row, column] == costs[row - 1, column] + 1)
                {
                    deletions++;
                    row--;
                }
                else
                {
                    insertions++;
                    column--;
                }
            }

            return (costs[reference.Length, hypothesis.Length], substitutions, deletions, insertions);
        }
    }

    /// <summary>Counts the segments the session pops so integrity can be cross-checked.</summary>
    private sealed class CountingVad(IVoiceActivityDetector inner) : IVoiceActivityDetector
    {
        private readonly List<float[]> _popped = [];

        public bool IsAvailable => inner.IsAvailable;

        public bool IsSpeechActive => inner.IsSpeechActive;

        public int SegmentsClosed => _popped.Count;

        public IReadOnlyList<float[]> PoppedSegments => _popped;

        public void Reset() => inner.Reset();

        public void Accept(ReadOnlyMemory<float> samples) => inner.Accept(samples);

        public void Flush() => inner.Flush();

        public bool TryPopSegment(out VadSpeechSegment segment)
        {
            if (inner.TryPopSegment(out segment))
            {
                _popped.Add(segment.Samples);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Times every segment decode and separates closed-segment decodes from
    /// open-tail re-decodes by the sample array identity (the session passes
    /// the VAD's own array for closed segments and a fresh copy for the tail).
    /// </summary>
    private sealed class CountingStreamingProvider(
        IStreamingSpeechToTextProvider inner,
        Func<IReadOnlyList<float[]>> poppedSegments)
        : IStreamingSpeechToTextProvider
    {
        private readonly List<string> _closedDecodeTexts = [];

        public string Id => inner.Id;

        public bool IsAvailable => inner.IsAvailable;

        public double TotalDecodeMs { get; private set; }

        public int TailDecodes { get; private set; }

        public int EmptyClosedDecodes { get; private set; }

        public IReadOnlyList<string> ClosedSegmentDecodeTexts => _closedDecodeTexts;

        public async Task<string> TranscribeSegmentAsync(
            AudioBuffer segment, SttOptions options, CancellationToken cancellationToken)
        {
            Stopwatch watch = Stopwatch.StartNew();
            string text = (await inner.TranscribeSegmentAsync(segment, options, cancellationToken)).Trim();
            watch.Stop();
            TotalDecodeMs += watch.Elapsed.TotalMilliseconds;

            if (poppedSegments().Any(popped => ReferenceEquals(popped, segment.Samples)))
            {
                _closedDecodeTexts.Add(text);
                if (text.Length == 0)
                {
                    EmptyClosedDecodes++;
                }
            }
            else
            {
                TailDecodes++;
            }

            return text;
        }

        public Task<SttResult> TranscribeAsync(
            AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
            => inner.TranscribeAsync(audio, options, cancellationToken);
    }

    private sealed class LivePaths(string root) : IStoragePaths
    {
        public string RootDirectory => root;

        public string CapturesDirectory => Path.Combine(root, "Captures");

        public string ProjectsDirectory => Path.Combine(root, "Projects");

        public string RecordingsDirectory => Path.Combine(root, "Recordings");

        public string ThumbnailsDirectory => Path.Combine(root, "Thumbnails");

        public string TempExportsDirectory => Path.Combine(root, "TempExports");

        public string LogsDirectory => Path.Combine(root, "Logs");

        public string DatabasePath => Path.Combine(root, "octadock.db");

        public void EnsureDirectories() => Directory.CreateDirectory(root);

        public string ToAbsolute(string relativePath) => Path.Combine(root, relativePath);

        public string ToRelative(string absolutePath) => absolutePath;

        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Captures", $"{id}{extension}");

        public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id}.jpg");

        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Recordings", $"{id}{extension}");

        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => Path.Combine("Clipboard", $"{id}.png");
    }

    // ---- Corpus document models ----

    private sealed record CorpusCase(string Id, string Category, string Text, string? ExpectedLanguage);

    private sealed class CorpusManifest
    {
        public int SchemaVersion { get; private init; }

        public List<CorpusCase> Cases { get; private init; } = [];

        public static CorpusManifest Load(string path)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
            {
                throw new InvalidDataException($"Unsupported corpus manifest schema in {path}.");
            }

            var cases = new List<CorpusCase>();
            foreach (JsonElement element in root.GetProperty("cases").EnumerateArray())
            {
                cases.Add(new CorpusCase(
                    element.GetProperty("id").GetString()!,
                    element.GetProperty("category").GetString()!,
                    element.GetProperty("text").GetString()!,
                    element.TryGetProperty("expectedLanguage", out JsonElement language)
                        ? language.GetString()
                        : null));
            }

            return new CorpusManifest { SchemaVersion = 1, Cases = cases };
        }
    }

    private sealed record CorpusRecordCase(string Status, string? Wav, string? Reason, string? VoiceName, string? VoiceCulture);

    private sealed class CorpusRecord
    {
        public Dictionary<string, CorpusRecordCase> Cases { get; private init; } = [];

        public static CorpusRecord Load(string path)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var cases = new Dictionary<string, CorpusRecordCase>(StringComparer.Ordinal);
            foreach (JsonElement element in document.RootElement.GetProperty("cases").EnumerateArray())
            {
                string id = element.GetProperty("id").GetString()!;
                cases[id] = new CorpusRecordCase(
                    element.GetProperty("status").GetString()!,
                    element.TryGetProperty("wav", out JsonElement wav) ? wav.GetString() : null,
                    element.TryGetProperty("reason", out JsonElement reason) ? reason.GetString() : null,
                    element.TryGetProperty("voiceName", out JsonElement voiceName) ? voiceName.GetString() : null,
                    element.TryGetProperty("voiceCulture", out JsonElement voiceCulture) ? voiceCulture.GetString() : null);
            }

            return new CorpusRecord { Cases = cases };
        }
    }
}

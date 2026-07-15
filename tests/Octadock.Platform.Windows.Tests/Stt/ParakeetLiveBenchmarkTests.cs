using System.Diagnostics;
using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Core.Abstractions;
using Octadock.Core.Speech;
using Octadock.Platform.Windows.Stt;
using Windows.Media.SpeechSynthesis;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// End-to-end Parakeet benchmark, gated behind OCTADOCK_LIVE_STT=1 because it
/// downloads the ~640 MB model on first run and exercises the native runtime.
/// Speech input is synthesized on the fly with the built-in Windows voice so
/// no audio fixture needs to live in the repository. Results land in
/// %TEMP%\octadock-stt-bench.txt.
/// </summary>
public sealed class ParakeetLiveBenchmarkTests
{
    private const string GateVariable = "OCTADOCK_LIVE_STT";

    [Fact]
    public async Task Parakeet_transcribes_synthesized_speech_quickly_and_accurately()
    {
        if (Environment.GetEnvironmentVariable(GateVariable) != "1")
        {
            return; // Live-only: opt in with OCTADOCK_LIVE_STT=1.
        }

        const string spoken =
            "The quick brown fox jumps over the lazy dog. " +
            "Octadock dictation should be fast, accurate, and completely local.";

        using var store = new LiveStore();
        ParakeetSttProvider provider = store.Provider;
        provider.IsAvailable.Should().BeTrue(provider.UnavailableReason);

        Stopwatch downloadWatch = Stopwatch.StartNew();
        await provider.EnsureModelAsync(null, null, CancellationToken.None);
        downloadWatch.Stop();
        provider.IsModelAvailable(null).Should().BeTrue();

        AudioBuffer audio = await SynthesizeAsync(spoken);
        audio.Duration.TotalSeconds.Should().BeGreaterThan(3);

        // First decode includes the one-time recognizer build; the second is
        // what a warmed-up dictation actually feels like.
        var options = new SttOptions { Language = "en" };
        Stopwatch coldWatch = Stopwatch.StartNew();
        SttResult cold = await provider.TranscribeAsync(audio, options, CancellationToken.None);
        coldWatch.Stop();

        Stopwatch warmWatch = Stopwatch.StartNew();
        SttResult warm = await provider.TranscribeAsync(audio, options, CancellationToken.None);
        warmWatch.Stop();

        double warmRtf = warmWatch.Elapsed.TotalSeconds / audio.Duration.TotalSeconds;
        WriteReport(spoken, audio, downloadWatch.Elapsed, coldWatch.Elapsed, warmWatch.Elapsed, warmRtf, cold, warm);

        string normalized = warm.Text.ToUpperInvariant();
        normalized.Should().Contain("QUICK BROWN FOX");
        normalized.Should().Contain("LAZY DOG");

        // 20-30x realtime is typical; anything above 0.5 RTF means the engine
        // is not delivering the instant-dictation promise on this machine.
        warmRtf.Should().BeLessThan(0.5);
    }

    private static async Task<AudioBuffer> SynthesizeAsync(string text)
    {
        using var synthesizer = new SpeechSynthesizer();

        // The benchmark text is English; on non-English systems the default
        // voice reads it with local phonemes and the accuracy assertions turn
        // into a test of the wrong thing. Pick an installed English voice.
        VoiceInformation? english = SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        english.Should().NotBeNull("the live benchmark needs an installed English TTS voice");
        synthesizer.Voice = english;

        SpeechSynthesisStream synthesized = await synthesizer.SynthesizeTextToStreamAsync(text);

        using var wav = new MemoryStream();
        await synthesized.AsStreamForRead().CopyToAsync(wav);
        wav.Position = 0;

        await using var reader = new WaveFileReader(wav);
        ISampleProvider samples = reader.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1)
        {
            samples = samples.ToMono();
        }

        var resampled = new WdlResamplingSampleProvider(samples, 16_000);
        var collected = new List<float>(16_000 * 16);
        var buffer = new float[16_000];
        int read;
        while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0)
        {
            collected.AddRange(buffer.Take(read));
        }

        return new AudioBuffer([.. collected]);
    }

    private static void WriteReport(
        string spoken,
        AudioBuffer audio,
        TimeSpan download,
        TimeSpan cold,
        TimeSpan warm,
        double warmRtf,
        SttResult coldResult,
        SttResult warmResult)
    {
        var report = new StringBuilder()
            .AppendLine("Octadock Parakeet live benchmark")
            .AppendLine(CultureInfo.InvariantCulture, $"Run at            : {DateTimeOffset.Now:O}")
            .AppendLine(CultureInfo.InvariantCulture, $"Audio duration    : {audio.Duration.TotalSeconds:0.00}s")
            .AppendLine(CultureInfo.InvariantCulture, $"Model ensure      : {download.TotalSeconds:0.0}s (0 when cached)")
            .AppendLine(CultureInfo.InvariantCulture, $"Cold transcription: {cold.TotalMilliseconds:0} ms (includes recognizer build)")
            .AppendLine(CultureInfo.InvariantCulture, $"Warm transcription: {warm.TotalMilliseconds:0} ms")
            .AppendLine(CultureInfo.InvariantCulture, $"Warm RTF          : {warmRtf:0.000}")
            .AppendLine(CultureInfo.InvariantCulture, $"Spoken            : {spoken}")
            .AppendLine(CultureInfo.InvariantCulture, $"Cold transcript   : {coldResult.Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Warm transcript   : {warmResult.Text}");

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "octadock-stt-bench.txt"),
            report.ToString());
    }

    /// <summary>
    /// Keeps the live model under %LOCALAPPDATA%\Octadock so repeated benchmark
    /// runs (and the real app) share one download instead of re-fetching 640 MB.
    /// </summary>
    private sealed class LiveStore : IDisposable
    {
        private readonly ParakeetModelStore _modelStore;

        public LiveStore()
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Octadock");
            _modelStore = new ParakeetModelStore(
                new LiveStoragePaths(root), NullLogger<ParakeetModelStore>.Instance);
            Provider = new ParakeetSttProvider(_modelStore, NullLogger<ParakeetSttProvider>.Instance);
        }

        public ParakeetSttProvider Provider { get; }

        public void Dispose()
        {
            Provider.Dispose();
            _modelStore.Dispose();
        }
    }

    private sealed class LiveStoragePaths(string root) : IStoragePaths
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
}

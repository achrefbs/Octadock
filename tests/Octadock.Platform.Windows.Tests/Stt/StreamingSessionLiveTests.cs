using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Core.Abstractions;
using Octadock.Core.Speech;
using Octadock.Platform.Windows.Audio;
using Octadock.Platform.Windows.Stt;
using Windows.Media.SpeechSynthesis;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// Live wiring test for the whole streaming stack — Silero VAD (embedded
/// resource extraction + native runtime) feeding <see cref="SimulatedStreamingSession"/>
/// over the real Parakeet recognizer. Gated behind OCTADOCK_LIVE_STT=1 like
/// the engine benchmark (shares its cached model download).
/// </summary>
public sealed class StreamingSessionLiveTests
{
    [Fact]
    public async Task Vad_segments_freeze_early_and_finalize_decodes_only_the_tail()
    {
        if (Environment.GetEnvironmentVariable("OCTADOCK_LIVE_STT") != "1")
        {
            return; // Live-only: opt in with OCTADOCK_LIVE_STT=1.
        }

        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Octadock");
        var paths = new LivePaths(root);
        var store = new ParakeetModelStore(paths, NullLogger<ParakeetModelStore>.Instance);
        using var provider = new ParakeetSttProvider(store, NullLogger<ParakeetSttProvider>.Instance);
        using var vad = new SileroVoiceActivityDetector(paths, NullLogger<SileroVoiceActivityDetector>.Instance);

        provider.IsAvailable.Should().BeTrue(provider.UnavailableReason);
        vad.IsAvailable.Should().BeTrue("the Silero model is embedded and shares the sherpa runtime");
        await provider.EnsureModelAsync(null, null, CancellationToken.None);
        provider.WarmUp(null);

        // Two sentences separated by 1.2 s of silence — enough for the VAD
        // (min silence 0.6 s) to close the first sentence as a stable segment
        // while the second is still "being spoken".
        float[] first = await SynthesizeAsync("The quick brown fox jumps over the lazy dog.");
        float[] second = await SynthesizeAsync("Everything runs completely local.");
        float[] gap = new float[16_000 * 12 / 10];
        float[] utterance = [.. first, .. gap, .. second];

        vad.Reset();
        var session = new SimulatedStreamingSession(provider, vad, new SttOptions { Language = "en" });
        string stableBeforeFinalize = string.Empty;
        session.PartialChanged += (_, e) => stableBeforeFinalize = e.Stable;

        // Feed like the audio pump does: 100 ms chunks, ticking every ~400 ms.
        for (int offset = 0; offset < utterance.Length; offset += 1_600)
        {
            int length = Math.Min(1_600, utterance.Length - offset);
            session.Accept(utterance.AsMemory(offset, length));
            if (offset / 1_600 % 4 == 3)
            {
                await session.TickAsync(CancellationToken.None);
            }
        }

        await session.TickAsync(CancellationToken.None);
        stableBeforeFinalize.ToUpperInvariant().Should().Contain(
            "QUICK BROWN FOX", "the first sentence must be frozen stable before the utterance ends");

        Stopwatch finalizeWatch = Stopwatch.StartNew();
        SttResult result = await session.FinalizeAsync(CancellationToken.None);
        finalizeWatch.Stop();

        string normalized = result.Text.ToUpperInvariant();
        normalized.Should().Contain("QUICK BROWN FOX");
        normalized.Should().Contain("COMPLETELY LOCAL");

        // The whole point of segment-freeze: stopping only decodes the short
        // open tail, so stop-to-text is instant even after long dictations.
        finalizeWatch.ElapsedMilliseconds.Should().BeLessThan(1_000);
    }

    private static async Task<float[]> SynthesizeAsync(string text)
    {
        using var synthesizer = new SpeechSynthesizer();
        VoiceInformation? english = SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        english.Should().NotBeNull("the live test needs an installed English TTS voice");
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
        var collected = new List<float>(16_000 * 8);
        var buffer = new float[16_000];
        int read;
        while ((read = resampled.Read(buffer, 0, buffer.Length)) > 0)
        {
            collected.AddRange(buffer.Take(read));
        }

        return [.. collected];
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
}

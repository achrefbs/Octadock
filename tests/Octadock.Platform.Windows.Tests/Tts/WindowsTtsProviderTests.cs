using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;
using Octadock.Core.Abstractions;
using Octadock.Platform.Windows.Tts;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Tts;

/// <summary>
/// Exercises the built-in Windows voices for real — they are always installed,
/// fully offline, and synthesis of a short phrase takes well under a second,
/// so this runs in the normal suite (no gating).
/// </summary>
public sealed class WindowsTtsProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "octadock-tests", Path.GetRandomFileName());

    [Fact]
    public async Task Synthesizes_text_to_a_playable_wav_file()
    {
        var provider = new WindowsTtsProvider(
            new FakePaths(_root), NullLogger<WindowsTtsProvider>.Instance);
        provider.IsAvailable.Should().BeTrue(provider.UnavailableReason);

        SynthesizedSpeech speech = await provider.SynthesizeAsync(
            new TextToSpeechRequest { Text = "Octadock reads this aloud.", Rate = 1.25 },
            CancellationToken.None);

        File.Exists(speech.FilePath).Should().BeTrue();
        await using var reader = new WaveFileReader(speech.FilePath);
        reader.TotalTime.Should().BeGreaterThan(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task Faster_rate_produces_shorter_audio()
    {
        var provider = new WindowsTtsProvider(
            new FakePaths(_root), NullLogger<WindowsTtsProvider>.Instance);
        const string text = "This sentence is spoken twice at different speaking rates for comparison.";

        SynthesizedSpeech normal = await provider.SynthesizeAsync(
            new TextToSpeechRequest { Text = text, Rate = 1.0 }, CancellationToken.None);
        SynthesizedSpeech fast = await provider.SynthesizeAsync(
            new TextToSpeechRequest { Text = text, Rate = 2.0 }, CancellationToken.None);

        TimeSpan normalDuration;
        await using (var reader = new WaveFileReader(normal.FilePath))
        {
            normalDuration = reader.TotalTime;
        }

        TimeSpan fastDuration;
        await using (var reader = new WaveFileReader(fast.FilePath))
        {
            fastDuration = reader.TotalTime;
        }

        fastDuration.Should().BeLessThan(normalDuration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakePaths(string root) : IStoragePaths
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

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// Trust-model routing (WS7): cloud transcription may only ever surface when
/// the user explicitly selects the OpenAI provider id AND holds the scoped env
/// key. Every other resolution path must stay on a local engine — unknown or
/// empty ids degrade to local defaults, never to the cloud.
/// </summary>
public sealed class SpeechToTextProviderFactoryTests : IDisposable
{
    private readonly ParakeetModelStore _store = new(
        new FakeStoragePaths(), NullLogger<ParakeetModelStore>.Instance);
    private readonly ParakeetSttProvider _parakeet;
    private readonly WhisperSttProvider _whisper;
    private readonly SpeechToTextProviderFactory _factory;

    public SpeechToTextProviderFactoryTests()
    {
        _parakeet = new ParakeetSttProvider(_store, NullLogger<ParakeetSttProvider>.Instance);
        _whisper = new WhisperSttProvider(new FakeStoragePaths(), NullLogger<WhisperSttProvider>.Instance);
        _factory = new SpeechToTextProviderFactory(
            _parakeet,
            _whisper,
            new OpenAiSttProvider(NullLogger<OpenAiSttProvider>.Instance),
            NullLogger<SpeechToTextProviderFactory>.Instance);
    }

    [Fact]
    public void Explicit_openai_id_is_the_only_path_that_resolves_to_cloud()
    {
        _factory.Resolve(SpeechSettings.OpenAiProvider).Should().BeOfType<OpenAiSttProvider>();
        _factory.Resolve("OPENAI").Should().BeOfType<OpenAiSttProvider>(
            "ids are case-insensitive; an explicit selection is honored");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("parakeet")]
    [InlineData("whisper")]
    [InlineData("azure")]
    [InlineData("google-cloud-speech")]
    [InlineData("openai-turbo")]
    public void Unknown_or_local_ids_never_resolve_to_cloud(string providerId)
    {
        ISpeechToTextProvider? resolved = _factory.Resolve(providerId);

        resolved.Should().NotBeNull();
        resolved.Should().NotBeOfType<OpenAiSttProvider>(
            "only the explicit OpenAI id may activate a cloud path");
        (resolved!.Id == SpeechSettings.ParakeetProvider || resolved.Id == "whisper")
            .Should().BeTrue("every non-explicit id must stay on a local engine");
    }

    [Fact]
    public void Describe_lists_openai_as_opt_in_with_the_scoped_variable_name()
    {
        IReadOnlyList<SpeechProviderDescription> descriptions = _factory.Describe();

        descriptions.Should().HaveCount(3);
        SpeechProviderDescription openAi = descriptions.Single(d => d.Id == SpeechSettings.OpenAiProvider);
        // Without OCTADOCK_OPENAI_API_KEY the provider is unavailable and the
        // reason names the scoped opt-in variable (never a bare OPENAI_API_KEY).
        if (!openAi.IsAvailable)
        {
            openAi.Reason.Should().Contain("OCTADOCK_OPENAI_API_KEY");
        }

        descriptions.Count(d => d.Id == SpeechSettings.ParakeetProvider).Should().Be(1);
        SpeechProviderDescription whisper = descriptions.Single(d => d.Id == "whisper");
        whisper.IsAvailable.Should().BeTrue("the local fallback is always listable as available");
    }

    public void Dispose()
    {
        _parakeet.Dispose();
        _whisper.Dispose();
        _store.Dispose();
    }

    private sealed class FakeStoragePaths : IStoragePaths
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "octadock-factory-tests");

        public string RootDirectory => _root;

        public string CapturesDirectory => Path.Combine(_root, "Captures");

        public string ProjectsDirectory => Path.Combine(_root, "Projects");

        public string RecordingsDirectory => Path.Combine(_root, "Recordings");

        public string ThumbnailsDirectory => Path.Combine(_root, "Thumbnails");

        public string TempExportsDirectory => Path.Combine(_root, "TempExports");

        public string LogsDirectory => Path.Combine(_root, "Logs");

        public string DatabasePath => Path.Combine(_root, "octadock.db");

        public void EnsureDirectories() => Directory.CreateDirectory(_root);

        public string ToAbsolute(string relativePath) => Path.Combine(_root, relativePath);

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

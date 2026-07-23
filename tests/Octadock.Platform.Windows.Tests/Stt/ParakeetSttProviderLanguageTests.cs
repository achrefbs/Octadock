using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Platform.Windows.Stt;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Stt;

/// <summary>
/// Language coverage drives the controller's auto-language routing: an
/// explicit language outside Parakeet's 25-language set reroutes to Whisper,
/// so the coverage answers must be exact (including BCP-47 region forms).
/// </summary>
public sealed class ParakeetSttProviderLanguageTests : IDisposable
{
    private readonly ParakeetModelStore _store = new(
        new FakeStoragePaths(), NullLogger<ParakeetModelStore>.Instance);

    private readonly ParakeetSttProvider _provider;

    public ParakeetSttProviderLanguageTests()
    {
        _provider = new ParakeetSttProvider(_store, NullLogger<ParakeetSttProvider>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Auto_detect_is_always_supported(string? language)
    {
        _provider.SupportsLanguage(language).Should().BeTrue(
            "null/empty means the model detects the language itself");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("uk")]
    [InlineData("pt-BR")]
    [InlineData("pt_BR")]
    [InlineData("es-MX")]
    public void Supported_languages_include_region_variants(string language)
    {
        _provider.SupportsLanguage(language).Should().BeTrue();
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("zh-CN")]
    [InlineData("ko")]
    [InlineData("ar")]
    [InlineData("hi")]
    public void Unsupported_languages_are_reported_honestly(string language)
    {
        _provider.SupportsLanguage(language).Should().BeFalse(
            "unsupported explicit languages must reroute to the broader local engine");
    }

    [Fact]
    public void Odd_tags_normalize_to_their_base_language()
    {
        // "en-us-x" trims to base "en" — a supported language. Document the
        // normalization rather than letting an odd tag read as unsupported.
        _provider.SupportsLanguage("en-us-x").Should().BeTrue();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _store.Dispose();
    }

    private sealed class FakeStoragePaths : IStoragePaths
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "octadock-parakeet-language-tests");

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

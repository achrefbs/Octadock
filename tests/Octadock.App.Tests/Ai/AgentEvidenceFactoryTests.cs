using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Context;
using Octadock.Core.Io;
using Octadock.Core.Imaging;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AgentEvidenceFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"octadock-agent-evidence-{Guid.NewGuid():N}");
    private readonly TestStoragePaths _paths;

    public AgentEvidenceFactoryTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new TestStoragePaths(_root);
    }

    [Fact]
    public async Task Text_file_gets_bounded_content_hash_but_original_is_not_attached()
    {
        string path = Path.Combine(_root, "notes with spaces.md");
        await File.WriteAllTextAsync(path, "Evidence text", Encoding.UTF8);
        AgentEvidenceFactory factory = BuildFactory();

        AgentWorkspaceEvidence evidence = await factory.FromFileAsync(path);

        evidence.Kind.Should().Be(AgentPacketSourceKind.File);
        evidence.TextContent.Should().Contain("Evidence text");
        evidence.Sha256.Should().MatchRegex("^[a-f0-9]{64}$");
        evidence.RelativeAssetPath.Should().BeNull(
            "the reviewed text in TASK.md must not sit beside an unredacted original file");
        evidence.ToPacketSource().Kind.Should().Be(AgentPacketSourceKind.Text);
        evidence.ToPacketSource().Assets.Should().BeEmpty();
    }

    [Fact]
    public async Task Context_reference_is_rehashed_and_rejected_when_changed()
    {
        string path = Path.Combine(_root, "referenced.txt");
        await File.WriteAllTextAsync(path, "original", Encoding.UTF8);
        byte[] original = await File.ReadAllBytesAsync(path);
        var package = new ContextPackage
        {
            Id = Guid.NewGuid(),
            Name = "Bug context",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var item = new ContextItem
        {
            Id = Guid.NewGuid(),
            DisplayName = "referenced.txt",
            Ownership = ContextOwnership.Reference,
            ReferenceSourcePath = path,
            ReferenceSha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
            SizeBytes = original.Length,
            AddedAt = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(path, "changed", Encoding.UTF8);

        Func<Task> act = () => BuildFactory().FromContextItemAsync(package, item);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed after it was added*");
    }

    [Fact]
    public async Task Text_context_item_becomes_a_valid_text_only_context_source()
    {
        string path = Path.Combine(_root, "context.md");
        await File.WriteAllTextAsync(path, "Context evidence", Encoding.UTF8);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        var package = new ContextPackage
        {
            Id = Guid.NewGuid(),
            Name = "Bug context",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var item = new ContextItem
        {
            Id = Guid.NewGuid(),
            DisplayName = "context.md",
            Ownership = ContextOwnership.Reference,
            ReferenceSourcePath = path,
            ReferenceSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            SizeBytes = bytes.Length,
            AddedAt = DateTimeOffset.UtcNow,
        };

        AgentWorkspaceEvidence evidence = await BuildFactory().FromContextItemAsync(package, item);
        AgentPacketSourceItem source = evidence.ToPacketSource();

        source.Kind.Should().Be(AgentPacketSourceKind.Context);
        source.Assets.Should().BeEmpty();
        source.TextContent.Should().Contain("Context evidence");
        Action build = () => new AgentPacketBuilder(new TextSecretDetector()).Build(new AgentPacketBuildRequest
        {
            Metadata = new AgentPacketMetadata
            {
                Id = "context-test",
                Title = "Context test",
                Goal = "Review the supplied context.",
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Sources = [source],
            AcceptanceCriteria =
            [
                new AgentPacketAcceptanceCriterion
                {
                    Id = "reviewed",
                    Description = "The context is reviewed.",
                },
            ],
        });
        build.Should().NotThrow();
    }

    [Fact]
    public async Task Invalid_clipboard_image_is_removed_when_inspection_fails()
    {
        AgentEvidenceFactory factory = BuildFactory();
        var invalid = new EncodedImage(new byte[] { 1, 2, 3, 4 }, ExportImageFormat.Png);

        Func<Task> add = () => factory.FromClipboardImageAsync(invalid);

        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("*corrupt*unsupported*");
        string clipboard = Path.Combine(_paths.TempExportsDirectory, "AgentWorkspace", "Clipboard");
        Directory.Exists(clipboard).Should().BeTrue();
        Directory.EnumerateFiles(clipboard).Should().BeEmpty();
    }

    private AgentEvidenceFactory BuildFactory()
    {
        var safeWriter = new SafeFileWriter(new FileRevisionStore(Path.Combine(_root, "revisions")));
        return new AgentEvidenceFactory(new AiTextFileLoader(), _paths, safeWriter);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestStoragePaths(string root) : IStoragePaths
    {
        public string RootDirectory { get; } = root;
        public string CapturesDirectory => Path.Combine(RootDirectory, "Captures");
        public string ProjectsDirectory => Path.Combine(RootDirectory, "Projects");
        public string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");
        public string ThumbnailsDirectory => Path.Combine(RootDirectory, "Thumbnails");
        public string TempExportsDirectory => Path.Combine(RootDirectory, "TempExports");
        public string LogsDirectory => Path.Combine(RootDirectory, "Logs");
        public string DatabasePath => Path.Combine(RootDirectory, "octadock.db");
        public void EnsureDirectories() => Directory.CreateDirectory(TempExportsDirectory);
        public string ToAbsolute(string relativePath) => Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
        public string ToRelative(string absolutePath) => Path.GetRelativePath(RootDirectory, absolutePath);
        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Captures", $"{id}{extension}");
        public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id}.jpg");
        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Recordings", $"{id}{extension}");
        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => Path.Combine("Clipboard", $"{id}.png");
    }
}

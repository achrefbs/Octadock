using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Ai;
using Octadock.Core.Io;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AgentPacketExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"octadock-agent-export-{Guid.NewGuid():N}");

    public AgentPacketExportServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Export_materializes_exact_reviewed_documents_and_hashed_assets_without_absolute_paths()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("asset bytes");
        string sourcePath = Path.Combine(_root, "private-machine-name.txt");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        const string relative = "assets/src-1-evidence.txt";
        ReviewedAgentPacket packet = BuildPacket(relative, sha);
        AgentWorkspaceEvidence evidence = BuildEvidence(sourcePath, relative, sha);
        var service = new AgentPacketExportService(BuildSafeWriter());

        AgentPacketBundle bundle = await service.ExportAsync(packet, [evidence], Path.Combine(_root, "exports"));

        (await File.ReadAllTextAsync(bundle.TaskPath)).Should().Be(packet.OutboundMarkdown);
        (await File.ReadAllTextAsync(bundle.ManifestPath)).Should().Be(packet.ManifestJson);
        (await File.ReadAllBytesAsync(Path.Combine(bundle.DirectoryPath, relative))).Should().Equal(bytes);
        string checksums = await File.ReadAllTextAsync(Path.Combine(bundle.DirectoryPath, "SHA256SUMS"));
        checksums.Should().Contain($"{packet.OutboundSha256}  TASK.md")
            .And.Contain($"{packet.ManifestSha256}  manifest.json")
            .And.Contain($"{sha}  {relative}");
        packet.ManifestJson.Should().NotContain(sourcePath).And.NotContain(_root);
        bundle.ImagePaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_fails_closed_when_source_changes_after_review_and_removes_staging()
    {
        byte[] reviewedBytes = Encoding.UTF8.GetBytes("reviewed");
        string sourcePath = Path.Combine(_root, "mutable.txt");
        await File.WriteAllBytesAsync(sourcePath, reviewedBytes);
        string sha = Convert.ToHexString(SHA256.HashData(reviewedBytes)).ToLowerInvariant();
        const string relative = "assets/src-1-mutable.txt";
        ReviewedAgentPacket packet = BuildPacket(relative, sha);
        AgentWorkspaceEvidence evidence = BuildEvidence(sourcePath, relative, sha);
        await File.WriteAllTextAsync(sourcePath, "changed after review");
        string exports = Path.Combine(_root, "exports");
        var service = new AgentPacketExportService(BuildSafeWriter());

        Func<Task> act = () => service.ExportAsync(packet, [evidence], exports);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*changed after review*");
        Directory.GetDirectories(exports).Should().BeEmpty();
    }

    private ReviewedAgentPacket BuildPacket(string relativePath, string sha)
    {
        var builder = new AgentPacketBuilder(new TextSecretDetector());
        return builder.Build(new AgentPacketBuildRequest
        {
            Metadata = new AgentPacketMetadata
            {
                Id = "export-test",
                Title = "Export test",
                Goal = "Inspect the supplied evidence.",
                CreatedAt = new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            },
            Sources =
            [
                new AgentPacketSourceItem
                {
                    Id = "src-1",
                    Kind = AgentPacketSourceKind.File,
                    Label = "Evidence",
                    Provenance = new AgentPacketProvenance { Kind = AgentPacketProvenanceKind.FileSystem },
                    Assets =
                    [
                        new AgentPacketAssetReference
                        {
                            Role = AgentPacketAssetRoles.File,
                            RelativePath = relativePath,
                            MediaType = "text/plain",
                            Sha256 = sha,
                        },
                    ],
                },
            ],
            AcceptanceCriteria =
            [
                new AgentPacketAcceptanceCriterion
                {
                    Id = "ac-1",
                    Description = "The evidence is inspected.",
                },
            ],
        });
    }

    private static AgentWorkspaceEvidence BuildEvidence(string sourcePath, string relativePath, string sha) => new()
    {
        Id = Guid.NewGuid(),
        Kind = AgentPacketSourceKind.File,
        Label = "Evidence",
        LocalPath = sourcePath,
        RelativeAssetPath = relativePath,
        MediaType = "text/plain",
        Sha256 = sha,
        SizeBytes = new FileInfo(sourcePath).Length,
        Provenance = new AgentPacketProvenance { Kind = AgentPacketProvenanceKind.FileSystem },
    };

    private ISafeFileWriter BuildSafeWriter() => new SafeFileWriter(
        new FileRevisionStore(Path.Combine(_root, "revisions")));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

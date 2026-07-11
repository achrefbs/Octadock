using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Abstractions;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AgentTemporaryLeaseStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"octadock-agent-lease-{Guid.NewGuid():N}");

    [Fact]
    public void Release_deletes_unclaimed_managed_pin_and_claim_transfers_ownership()
    {
        var paths = new TestStoragePaths(_root);
        paths.EnsureDirectories();
        using var leases = new AgentTemporaryLeaseStore(paths);
        string first = Path.Combine(paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(first, [1, 2, 3]);

        string firstToken = leases.CreateLease(first);
        leases.Release(firstToken);

        File.Exists(first).Should().BeFalse();

        string second = Path.Combine(paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(second, [4, 5, 6]);
        string secondToken = leases.CreateLease(second);

        leases.TryClaim(secondToken, second).Should().BeTrue();
        File.Exists(second).Should().BeTrue("the claimant now owns cleanup");
        leases.Release(secondToken);
        File.Exists(second).Should().BeTrue("a claimed token cannot delete the transferred file");
        File.Delete(second);
    }

    [Fact]
    public void Refuses_to_lease_arbitrary_files_outside_the_managed_pin_shape()
    {
        var paths = new TestStoragePaths(_root);
        paths.EnsureDirectories();
        using var leases = new AgentTemporaryLeaseStore(paths);
        string arbitrary = Path.Combine(_root, "important.png");
        File.WriteAllBytes(arbitrary, [1]);

        Action create = () => leases.CreateLease(arbitrary);

        create.Should().Throw<InvalidOperationException>().WithMessage("*managed composed pin*");
        File.Exists(arbitrary).Should().BeTrue();
    }

    [Fact]
    public void Forged_mismatched_and_replayed_claims_cannot_transfer_or_delete_another_pin()
    {
        var paths = new TestStoragePaths(_root);
        paths.EnsureDirectories();
        using var leases = new AgentTemporaryLeaseStore(paths);
        string leased = Path.Combine(paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
        string foreign = Path.Combine(paths.TempExportsDirectory, $"pin-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(leased, [1, 2, 3]);
        File.WriteAllBytes(foreign, [4, 5, 6]);
        string token = leases.CreateLease(leased);

        leases.TryClaim("forged-token", leased).Should().BeFalse();
        leases.TryClaim(token, foreign).Should().BeFalse();
        File.Exists(leased).Should().BeTrue();
        File.Exists(foreign).Should().BeTrue();

        leases.TryClaim(token, leased).Should().BeTrue();
        leases.TryClaim(token, leased).Should().BeFalse("a lease can transfer ownership only once");
        leases.Release(token);

        File.Exists(leased).Should().BeTrue("a claimed lease no longer has deletion authority");
        File.Exists(foreign).Should().BeTrue();
        File.Delete(leased);
        File.Delete(foreign);
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
        public string CapturesDirectory => Path.Combine(root, "Captures");
        public string ProjectsDirectory => Path.Combine(root, "Projects");
        public string RecordingsDirectory => Path.Combine(root, "Recordings");
        public string ThumbnailsDirectory => Path.Combine(root, "Thumbnails");
        public string TempExportsDirectory => Path.Combine(root, "TempExports");
        public string LogsDirectory => Path.Combine(root, "Logs");
        public string DatabasePath => Path.Combine(root, "octadock.db");
        public void EnsureDirectories() => Directory.CreateDirectory(TempExportsDirectory);
        public string ToAbsolute(string relativePath) => Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.GetFullPath(Path.Combine(root, relativePath));
        public string ToRelative(string absolutePath) => Path.GetRelativePath(root, absolutePath);
        public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Captures", $"{id}{extension}");
        public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id}.jpg");
        public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
            => Path.Combine("Recordings", $"{id}{extension}");
        public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
            => Path.Combine("Clipboard", $"{id}.png");
    }
}

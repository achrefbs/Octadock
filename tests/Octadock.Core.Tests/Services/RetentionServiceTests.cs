using FluentAssertions;
using NSubstitute;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Octadock.Core.Tests.Fakes;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class RetentionServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root;
    private readonly StoragePaths _paths;
    private readonly ICaptureRepository _repo = Substitute.For<ICaptureRepository>();
    private readonly TestClock _clock = new(Now);

    public RetentionServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OctadockRetention", Guid.NewGuid().ToString("N"));
        _paths = new StoragePaths(_root);
        _paths.EnsureDirectories();

        // By default the repo returns no candidates.
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([]));
        _repo.GetSoftDeletedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([]));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort
        }
    }

    private RetentionService CreateService() => new(_repo, _paths, _clock);

    private static OctadockSettings ThirtyDays => OctadockSettings.Defaults with
    {
        History = new HistorySettings { Enabled = true, Retention = HistoryRetention.ThirtyDays },
    };

    private static OctadockSettings HistoryDisabled => OctadockSettings.Defaults with
    {
        History = new HistorySettings { Enabled = false, Retention = HistoryRetention.Disabled },
    };

    private CaptureRecord WriteCapture(
        DateTimeOffset createdAt,
        bool withThumb = true,
        bool withProject = false,
        bool withMockup = false)
    {
        var id = Guid.NewGuid();
        string originalRel = _paths.BuildCaptureRelativePath(id, createdAt, ".png");
        string originalAbs = _paths.ToAbsolute(originalRel);
        Directory.CreateDirectory(Path.GetDirectoryName(originalAbs)!);
        File.WriteAllBytes(originalAbs, new byte[100]);

        string? thumbRel = null;
        if (withThumb)
        {
            thumbRel = _paths.BuildThumbnailRelativePath(id);
            File.WriteAllBytes(_paths.ToAbsolute(thumbRel), new byte[10]);
        }

        string? projectRel = null;
        if (withProject)
        {
            projectRel = $"Projects/{id:D}.octadock";
            File.WriteAllBytes(_paths.ToAbsolute(projectRel), new byte[50]);
        }

        string? mockupRel = null;
        if (withMockup)
        {
            mockupRel = _paths.BuildMockupRelativePath(id, Guid.NewGuid(), createdAt);
            string mockupAbs = _paths.ToAbsolute(mockupRel);
            Directory.CreateDirectory(Path.GetDirectoryName(mockupAbs)!);
            File.WriteAllBytes(mockupAbs, new byte[70]);
        }

        return new CaptureRecord
        {
            Id = id,
            Type = CaptureType.Area,
            CreatedAt = createdAt,
            OriginalPath = originalRel,
            ThumbnailPath = thumbRel,
            ProjectPath = projectRel,
            ApprovedMockupPath = mockupRel,
        };
    }

    [Fact]
    public async Task PlanAsync_queries_repository_with_computed_cutoffs()
    {
        RetentionService service = CreateService();

        await service.PlanAsync(ThirtyDays, Now);

        await _repo.Received(1).GetOlderThanAsync(Now.AddDays(-30), Arg.Any<CancellationToken>());
        await _repo.Received(1).GetSoftDeletedBeforeAsync(
            Now - RetentionPolicy.SoftDeleteGrace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAsync_queries_all_live_captures_when_history_is_disabled()
    {
        RetentionService service = CreateService();

        await service.PlanAsync(HistoryDisabled, Now);

        await _repo.Received(1).GetOlderThanAsync(DateTimeOffset.MaxValue, Arg.Any<CancellationToken>());
        await _repo.Received(1).GetSoftDeletedBeforeAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAsync_reflects_policy_result()
    {
        var expired = WriteCapture(Now.AddDays(-40));
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([expired]));

        RetentionService service = CreateService();
        RetentionPlan plan = await service.PlanAsync(ThirtyDays, Now);

        plan.ExpiredToDelete.Should().ContainSingle().Which.Should().Be(expired.Id);
    }

    [Fact]
    public async Task RunAsync_hard_deletes_rows_and_files()
    {
        CaptureRecord expired = WriteCapture(
            Now.AddDays(-40), withThumb: true, withProject: true, withMockup: true);
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([expired]));

        string originalAbs = _paths.ToAbsolute(expired.OriginalPath);
        string thumbAbs = _paths.ToAbsolute(expired.ThumbnailPath!);
        string projectAbs = _paths.ToAbsolute(expired.ProjectPath!);
        string mockupAbs = _paths.ToAbsolute(expired.ApprovedMockupPath!);

        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        await _repo.Received(1).HardDeleteAsync(expired.Id, Arg.Any<CancellationToken>());
        result.CapturesDeleted.Should().Be(1);
        result.FilesDeleted.Should().Be(4);
        result.BytesReclaimed.Should().Be(230);

        File.Exists(originalAbs).Should().BeFalse();
        File.Exists(thumbAbs).Should().BeFalse();
        File.Exists(projectAbs).Should().BeFalse();
        File.Exists(mockupAbs).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_does_not_delete_project_files_outside_storage_root()
    {
        CaptureRecord expired = WriteCapture(Now.AddDays(-40), withThumb: false);
        string externalProject = Path.Combine(
            Path.GetTempPath(),
            $"octadock-external-{Guid.NewGuid():N}.octadock");
        File.WriteAllBytes(externalProject, new byte[50]);

        expired = expired with { ProjectPath = externalProject };
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([expired]));

        try
        {
            RetentionService service = CreateService();
            RetentionResult result = await service.RunAsync(ThirtyDays);

            result.CapturesDeleted.Should().Be(1);
            result.FilesDeleted.Should().Be(1);
            File.Exists(_paths.ToAbsolute(expired.OriginalPath)).Should().BeFalse();
            File.Exists(externalProject).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(externalProject))
            {
                File.Delete(externalProject);
            }
        }
    }

    [Fact]
    public async Task RunAsync_deletes_old_temp_exports_without_capture_candidates()
    {
        string oldTemp = Path.Combine(_paths.TempExportsDirectory, "old.png");
        string newTemp = Path.Combine(_paths.TempExportsDirectory, "new.png");
        File.WriteAllBytes(oldTemp, new byte[20]);
        File.WriteAllBytes(newTemp, new byte[30]);
        File.SetLastWriteTimeUtc(oldTemp, Now.UtcDateTime - TimeSpan.FromDays(2));
        File.SetLastWriteTimeUtc(newTemp, Now.UtcDateTime);

        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        result.CapturesDeleted.Should().Be(0);
        result.FilesDeleted.Should().Be(1);
        result.BytesReclaimed.Should().Be(20);
        File.Exists(oldTemp).Should().BeFalse();
        File.Exists(newTemp).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_recursively_deletes_old_agent_workspace_crash_leftovers()
    {
        string bundle = Path.Combine(_paths.TempExportsDirectory, "octadock-agent-task-old");
        string clipboard = Path.Combine(_paths.TempExportsDirectory, "AgentWorkspace", "Clipboard");
        Directory.CreateDirectory(bundle);
        Directory.CreateDirectory(clipboard);
        string task = Path.Combine(bundle, "TASK.md");
        string image = Path.Combine(clipboard, "clipboard-old.png");
        File.WriteAllBytes(task, new byte[20]);
        File.WriteAllBytes(image, new byte[30]);
        DateTime old = Now.UtcDateTime - TimeSpan.FromDays(2);
        File.SetLastWriteTimeUtc(task, old);
        File.SetLastWriteTimeUtc(image, old);
        Directory.SetLastWriteTimeUtc(bundle, old);
        Directory.SetLastWriteTimeUtc(clipboard, old);
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(clipboard)!, old);

        RetentionResult result = await CreateService().RunAsync(ThirtyDays);

        result.FilesDeleted.Should().Be(2);
        result.BytesReclaimed.Should().Be(50);
        Directory.Exists(bundle).Should().BeFalse();
        Directory.Exists(clipboard).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_refuses_a_reparse_temp_exports_root()
    {
        string target = Path.Combine(Path.GetTempPath(), "OctadockRetentionTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        string foreign = Path.Combine(target, "foreign-old.txt");
        File.WriteAllBytes(foreign, new byte[19]);
        File.SetLastWriteTimeUtc(foreign, Now.UtcDateTime - TimeSpan.FromDays(2));
        Directory.Delete(_paths.TempExportsDirectory);

        try
        {
            try
            {
                Directory.CreateSymbolicLink(_paths.TempExportsDirectory, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // Some locked-down Windows CI hosts do not grant symlink creation.
                // The production guard is still exercised by the normal root tests.
                return;
            }

            RetentionResult result = await CreateService().RunAsync(ThirtyDays);

            result.FilesDeleted.Should().Be(0);
            File.Exists(foreign).Should().BeTrue(
                "retention must not traverse a symlink or junction used as its enumeration root");
        }
        finally
        {
            if (Directory.Exists(_paths.TempExportsDirectory))
            {
                Directory.Delete(_paths.TempExportsDirectory);
            }

            Directory.CreateDirectory(_paths.TempExportsDirectory);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunAsync_purges_soft_deleted_beyond_grace()
    {
        CaptureRecord purged = WriteCapture(Now.AddDays(-40)) with { DeletedAt = Now.AddDays(-3) };
        _repo.GetSoftDeletedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([purged]));

        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        await _repo.Received(1).HardDeleteAsync(purged.Id, Arg.Any<CancellationToken>());
        result.CapturesDeleted.Should().Be(1);
        File.Exists(_paths.ToAbsolute(purged.OriginalPath)).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_returns_nothing_when_no_candidates()
    {
        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        result.Should().Be(RetentionResult.Nothing);
        await _repo.DidNotReceive().HardDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_uses_the_clock_for_now()
    {
        // Move the clock so a capture created 40 days before "Now" is well past cutoff.
        _clock.UtcNow = Now;
        CaptureRecord expired = WriteCapture(Now.AddDays(-40));
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([expired]));

        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        result.CapturesDeleted.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_tolerates_missing_files()
    {
        // Record points at files that do not exist on disk.
        var id = Guid.NewGuid();
        var record = new CaptureRecord
        {
            Id = id,
            Type = CaptureType.Area,
            CreatedAt = Now.AddDays(-40),
            OriginalPath = $"Captures/2026/05/01/{id:D}.png",
            ThumbnailPath = _paths.BuildThumbnailRelativePath(id),
        };
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([record]));

        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        // Row is still deleted; no files were present to remove.
        await _repo.Received(1).HardDeleteAsync(id, Arg.Any<CancellationToken>());
        result.FilesDeleted.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_continues_when_a_hard_delete_throws()
    {
        CaptureRecord a = WriteCapture(Now.AddDays(-40), withThumb: false);
        CaptureRecord b = WriteCapture(Now.AddDays(-41), withThumb: false);
        _repo.GetOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CaptureRecord>>([a, b]));
        _repo.HardDeleteAsync(a.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("boom")));

        RetentionService service = CreateService();
        RetentionResult result = await service.RunAsync(ThirtyDays);

        // b still deleted despite a's failure.
        await _repo.Received(1).HardDeleteAsync(b.Id, Arg.Any<CancellationToken>());
        result.CapturesDeleted.Should().Be(1);
    }
}

using FluentAssertions;
using Octadock.Core.Persistence;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class CaptureLifecycleTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SoftDelete_sets_deleted_at_and_hides_from_default_query()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture(createdAt: BaseTime);
        await db.Captures.AddAsync(capture);
        var deletedAt = BaseTime.AddDays(1);

        await db.Captures.SoftDeleteAsync(capture.Id, deletedAt);

        var loaded = await db.Captures.GetAsync(capture.Id);
        loaded!.DeletedAt.Should().Be(deletedAt);
        loaded.IsDeleted.Should().BeTrue();

        var defaultQuery = await db.Captures.QueryAsync(new CaptureFilter());
        defaultQuery.Should().NotContain(c => c.Id == capture.Id);
    }

    [Fact]
    public async Task Restore_clears_deleted_at()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture(createdAt: BaseTime);
        await db.Captures.AddAsync(capture);
        await db.Captures.SoftDeleteAsync(capture.Id, BaseTime.AddDays(1));

        await db.Captures.RestoreAsync(capture.Id);

        var loaded = await db.Captures.GetAsync(capture.Id);
        loaded!.DeletedAt.Should().BeNull();
        loaded.IsDeleted.Should().BeFalse();

        var defaultQuery = await db.Captures.QueryAsync(new CaptureFilter());
        defaultQuery.Should().Contain(c => c.Id == capture.Id);
    }

    [Fact]
    public async Task HardDelete_removes_row()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture(createdAt: BaseTime);
        await db.Captures.AddAsync(capture);

        await db.Captures.HardDeleteAsync(capture.Id);

        (await db.Captures.GetAsync(capture.Id)).Should().BeNull();
        (await db.Captures.QueryAsync(new CaptureFilter { IncludeDeleted = true }))
            .Should().NotContain(c => c.Id == capture.Id);
    }

    [Fact]
    public async Task GetOlderThan_returns_only_live_captures_before_cutoff()
    {
        await using var db = await TestDatabase.CreateAsync();
        var old = RecordFactory.MinimalCapture(createdAt: BaseTime);
        var atCutoff = RecordFactory.MinimalCapture(createdAt: BaseTime.AddDays(5));
        var recent = RecordFactory.MinimalCapture(createdAt: BaseTime.AddDays(10));
        var oldButDeleted = RecordFactory.MinimalCapture(createdAt: BaseTime.AddDays(1)) with { DeletedAt = BaseTime.AddDays(2) };
        foreach (var c in new[] { old, atCutoff, recent, oldButDeleted })
        {
            await db.Captures.AddAsync(c);
        }

        var cutoff = BaseTime.AddDays(5);
        var result = await db.Captures.GetOlderThanAsync(cutoff);

        // strictly before the cutoff, and only non-deleted rows
        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(old.Id);
        result.Should().NotContain(c => c.Id == atCutoff.Id, "the cutoff comparison is strict (<)");
        result.Should().NotContain(c => c.Id == oldButDeleted.Id, "soft-deleted rows are excluded");
    }

    [Fact]
    public async Task GetSoftDeletedBefore_returns_only_deleted_at_or_before_cutoff()
    {
        await using var db = await TestDatabase.CreateAsync();
        var deletedEarly = RecordFactory.MinimalCapture(createdAt: BaseTime) with { DeletedAt = BaseTime.AddDays(1) };
        var deletedAtCutoff = RecordFactory.MinimalCapture(createdAt: BaseTime) with { DeletedAt = BaseTime.AddDays(5) };
        var deletedLate = RecordFactory.MinimalCapture(createdAt: BaseTime) with { DeletedAt = BaseTime.AddDays(10) };
        var live = RecordFactory.MinimalCapture(createdAt: BaseTime);
        foreach (var c in new[] { deletedEarly, deletedAtCutoff, deletedLate, live })
        {
            await db.Captures.AddAsync(c);
        }

        var cutoff = BaseTime.AddDays(5);
        var result = await db.Captures.GetSoftDeletedBeforeAsync(cutoff);

        result.Select(c => c.Id).Should().BeEquivalentTo(new[] { deletedEarly.Id, deletedAtCutoff.Id });
        result.Should().NotContain(c => c.Id == live.Id, "live rows have no deletion timestamp");
    }
}

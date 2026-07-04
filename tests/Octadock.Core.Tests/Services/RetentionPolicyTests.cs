using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class RetentionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static CaptureRecord Live(Guid id, DateTimeOffset createdAt) => new()
    {
        Id = id,
        Type = CaptureType.Area,
        CreatedAt = createdAt,
        OriginalPath = $"Captures/2026/07/01/{id:D}.png",
    };

    private static CaptureRecord SoftDeleted(Guid id, DateTimeOffset createdAt, DateTimeOffset deletedAt) =>
        Live(id, createdAt) with { DeletedAt = deletedAt };

    private static OctadockSettings WithRetention(HistoryRetention retention, bool enabled = true)
        => OctadockSettings.Defaults with
        {
            History = new HistorySettings { Enabled = enabled, Retention = retention },
        };

    [Fact]
    public void Empty_input_yields_empty_plan()
    {
        RetentionPlan plan = RetentionPolicy.Evaluate([], WithRetention(HistoryRetention.ThirtyDays), Now);
        plan.IsEmpty.Should().BeTrue();
        plan.Should().Be(RetentionPlan.Empty);
    }

    [Fact]
    public void Forever_retention_never_expires_live_captures()
    {
        var ancient = Live(Guid.NewGuid(), Now.AddYears(-5));
        RetentionPlan plan = RetentionPolicy.Evaluate([ancient], WithRetention(HistoryRetention.Forever), Now);

        plan.ExpiredToDelete.Should().BeEmpty();
    }

    [Fact]
    public void Thirty_day_retention_expires_older_and_keeps_newer()
    {
        var old = Live(Guid.NewGuid(), Now.AddDays(-31));
        var fresh = Live(Guid.NewGuid(), Now.AddDays(-5));

        RetentionPlan plan = RetentionPolicy.Evaluate([old, fresh], WithRetention(HistoryRetention.ThirtyDays), Now);

        plan.ExpiredToDelete.Should().ContainSingle().Which.Should().Be(old.Id);
        plan.ExpiredToDelete.Should().NotContain(fresh.Id);
    }

    [Fact]
    public void Thirty_day_boundary_is_strict_less_than()
    {
        // Exactly at the cutoff (created 30 days ago) is NOT expired.
        var atBoundary = Live(Guid.NewGuid(), Now.AddDays(-30));

        // Just past the cutoff (one second older than 30 days) IS expired.
        var justPast = Live(Guid.NewGuid(), Now.AddDays(-30).AddSeconds(-1));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [atBoundary, justPast], WithRetention(HistoryRetention.ThirtyDays), Now);

        plan.ExpiredToDelete.Should().NotContain(atBoundary.Id);
        plan.ExpiredToDelete.Should().Contain(justPast.Id);
    }

    [Fact]
    public void One_day_retention_boundary()
    {
        var old = Live(Guid.NewGuid(), Now.AddHours(-25));
        var fresh = Live(Guid.NewGuid(), Now.AddHours(-1));

        RetentionPlan plan = RetentionPolicy.Evaluate([old, fresh], WithRetention(HistoryRetention.OneDay), Now);

        plan.ExpiredToDelete.Should().Contain(old.Id);
        plan.ExpiredToDelete.Should().NotContain(fresh.Id);
    }

    [Fact]
    public void History_disabled_expires_all_live_captures()
    {
        var a = Live(Guid.NewGuid(), Now.AddMinutes(-1));
        var b = Live(Guid.NewGuid(), Now.AddDays(-100));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [a, b], WithRetention(HistoryRetention.Disabled, enabled: false), Now);

        plan.ExpiredToDelete.Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    public void Disabled_retention_keeps_live_when_history_enabled()
    {
        var a = Live(Guid.NewGuid(), Now.AddMinutes(-1));
        var b = Live(Guid.NewGuid(), Now.AddDays(-100));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [a, b], WithRetention(HistoryRetention.Disabled), Now);

        plan.ExpiredToDelete.Should().BeEmpty();
    }

    [Fact]
    public void History_disabled_via_enabled_flag_expires_live()
    {
        // enabled=false overrides even a Forever retention setting.
        var a = Live(Guid.NewGuid(), Now.AddMinutes(-1));
        RetentionPlan plan = RetentionPolicy.Evaluate(
            [a], WithRetention(HistoryRetention.Forever, enabled: false), Now);

        plan.ExpiredToDelete.Should().Contain(a.Id);
    }

    [Fact]
    public void Soft_deleted_within_grace_are_not_purged()
    {
        var recentlyDeleted = SoftDeleted(Guid.NewGuid(), Now.AddDays(-10), deletedAt: Now.AddHours(-2));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [recentlyDeleted], WithRetention(HistoryRetention.ThirtyDays), Now);

        plan.SoftDeletedToPurge.Should().BeEmpty();
    }

    [Fact]
    public void Soft_deleted_beyond_grace_are_purged()
    {
        var longDeleted = SoftDeleted(Guid.NewGuid(), Now.AddDays(-10), deletedAt: Now.AddDays(-2));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [longDeleted], WithRetention(HistoryRetention.ThirtyDays), Now);

        plan.SoftDeletedToPurge.Should().ContainSingle().Which.Should().Be(longDeleted.Id);
        // Soft-deleted rows are never also in the expiry list.
        plan.ExpiredToDelete.Should().NotContain(longDeleted.Id);
    }

    [Fact]
    public void Soft_deleted_grace_boundary_is_one_day()
    {
        var exactlyGrace = SoftDeleted(Guid.NewGuid(), Now.AddDays(-5), deletedAt: Now - RetentionPolicy.SoftDeleteGrace);
        var withinGrace = SoftDeleted(Guid.NewGuid(), Now.AddDays(-5), deletedAt: Now.AddHours(-23));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [exactlyGrace, withinGrace], WithRetention(HistoryRetention.ThirtyDays), Now);

        plan.SoftDeletedToPurge.Should().Contain(exactlyGrace.Id);   // deletedAt <= cutoff
        plan.SoftDeletedToPurge.Should().NotContain(withinGrace.Id);
    }

    [Fact]
    public void History_disabled_purges_soft_deleted_immediately()
    {
        var justDeleted = SoftDeleted(Guid.NewGuid(), Now.AddDays(-1), deletedAt: Now);

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [justDeleted], WithRetention(HistoryRetention.Disabled, enabled: false), Now);

        plan.SoftDeletedToPurge.Should().Contain(justDeleted.Id);
    }

    [Fact]
    public void Disabled_retention_uses_soft_delete_grace_when_history_enabled()
    {
        var justDeleted = SoftDeleted(Guid.NewGuid(), Now.AddDays(-1), deletedAt: Now);

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [justDeleted], WithRetention(HistoryRetention.Disabled), Now);

        plan.SoftDeletedToPurge.Should().BeEmpty();
    }

    [Fact]
    public void Mixed_set_partitions_into_expired_and_purge()
    {
        var expired = Live(Guid.NewGuid(), Now.AddDays(-40));
        var keep = Live(Guid.NewGuid(), Now.AddDays(-1));
        var purge = SoftDeleted(Guid.NewGuid(), Now.AddDays(-40), deletedAt: Now.AddDays(-3));
        var trashRecent = SoftDeleted(Guid.NewGuid(), Now.AddDays(-40), deletedAt: Now.AddHours(-1));

        RetentionPlan plan = RetentionPolicy.Evaluate(
            [expired, keep, purge, trashRecent], WithRetention(HistoryRetention.ThirtyDays), Now);

        plan.ExpiredToDelete.Should().BeEquivalentTo([expired.Id]);
        plan.SoftDeletedToPurge.Should().BeEquivalentTo([purge.Id]);
    }

    [Fact]
    public void ExpiryCutoff_is_null_for_forever_and_disabled()
    {
        RetentionPolicy.ExpiryCutoff(WithRetention(HistoryRetention.Forever), Now).Should().BeNull();
        RetentionPolicy.ExpiryCutoff(WithRetention(HistoryRetention.Disabled), Now).Should().BeNull();
        RetentionPolicy.ExpiryCutoff(WithRetention(HistoryRetention.ThirtyDays, enabled: false), Now)
            .Should().BeNull();
    }

    [Fact]
    public void ExpiryCutoff_subtracts_retention_days()
    {
        RetentionPolicy.ExpiryCutoff(WithRetention(HistoryRetention.SevenDays), Now)
            .Should().Be(Now.AddDays(-7));
    }

    [Fact]
    public void PurgeCutoff_uses_grace_when_enabled_and_now_when_disabled()
    {
        RetentionPolicy.PurgeCutoff(WithRetention(HistoryRetention.ThirtyDays), Now)
            .Should().Be(Now - RetentionPolicy.SoftDeleteGrace);
        RetentionPolicy.PurgeCutoff(WithRetention(HistoryRetention.Disabled), Now)
            .Should().Be(Now - RetentionPolicy.SoftDeleteGrace);
        RetentionPolicy.PurgeCutoff(WithRetention(HistoryRetention.ThirtyDays, enabled: false), Now)
            .Should().Be(Now);
    }
}

using FluentAssertions;
using Octadock.App.AiSessions;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests.AiSessions;

public sealed class AiSessionOverlayViewModelTests
{
    [Fact]
    public void RemoveItem_removes_matching_session()
    {
        var viewModel = new AiSessionOverlayViewModel();
        Guid keep = Guid.NewGuid();
        Guid remove = Guid.NewGuid();

        viewModel.SyncItems(
        [
            CreateItem(keep),
            CreateItem(remove),
        ]);

        viewModel.RemoveItem(remove);

        viewModel.Sessions.Should().ContainSingle();
        viewModel.Sessions[0].Id.Should().Be(keep);
        viewModel.HasItems.Should().BeTrue();
    }

    [Fact]
    public void SyncItems_keeps_existing_instances_and_updates_them_in_place()
    {
        var viewModel = new AiSessionOverlayViewModel();
        Guid id = Guid.NewGuid();
        viewModel.SyncItems([CreateItem(id, status: AiSessionStatus.Running)]);
        AiSessionOverlayItemViewModel original = viewModel.Sessions[0];

        bool changed = viewModel.SyncItems([CreateItem(id, status: AiSessionStatus.Completed)]);

        changed.Should().BeFalse("same row set — containers must not be rebuilt");
        viewModel.Sessions.Should().ContainSingle();
        viewModel.Sessions[0].Should().BeSameAs(original, "the row instance must survive a refresh");
        original.Status.Should().Be(AiSessionStatus.Completed);
        original.IsActive.Should().BeFalse();
    }

    [Fact]
    public void SyncItems_reports_membership_changes()
    {
        var viewModel = new AiSessionOverlayViewModel();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        viewModel.SyncItems([CreateItem(first)]).Should().BeTrue("first population adds a row");
        viewModel.SyncItems([CreateItem(first)]).Should().BeFalse("nothing changed");
        viewModel.SyncItems([CreateItem(first), CreateItem(second)]).Should().BeTrue("a row arrived");
        viewModel.SyncItems([CreateItem(second)]).Should().BeTrue("a row left");
        viewModel.Sessions.Should().ContainSingle();
        viewModel.Sessions[0].Id.Should().Be(second);
    }

    [Fact]
    public void SyncItems_reorders_surviving_rows()
    {
        var viewModel = new AiSessionOverlayViewModel();
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        viewModel.SyncItems([CreateItem(a), CreateItem(b)]);
        AiSessionOverlayItemViewModel rowA = viewModel.Sessions[0];
        AiSessionOverlayItemViewModel rowB = viewModel.Sessions[1];

        bool changed = viewModel.SyncItems([CreateItem(b), CreateItem(a)]);

        changed.Should().BeTrue();
        viewModel.Sessions[0].Should().BeSameAs(rowB);
        viewModel.Sessions[1].Should().BeSameAs(rowA);
    }

    [Fact]
    public void Item_commands_open_and_dismiss_session()
    {
        Guid dismissed = Guid.Empty;
        bool opened = false;
        AiSessionOverlayItemViewModel item = CreateItem(
            Guid.NewGuid(),
            id => dismissed = id,
            () => opened = true);

        item.OpenCommand.Execute(null);
        item.DismissCommand.Execute(null);

        opened.Should().BeTrue();
        dismissed.Should().Be(item.Id);
    }

    private static AiSessionOverlayItemViewModel CreateItem(
        Guid id,
        Action<Guid>? dismiss = null,
        Action? open = null,
        AiSessionStatus status = AiSessionStatus.Running)
    {
        var now = new DateTimeOffset(2026, 7, 3, 20, 0, 0, TimeSpan.Zero);
        var record = new AiSessionRecord
        {
            Id = id,
            Provider = AiSessionProvider.Codex,
            Title = "Codex run",
            Status = status,
            StartedAt = now.AddMinutes(-2),
            LastEventAt = now.AddSeconds(-5),
        };

        return new AiSessionOverlayItemViewModel(record, now, dismiss, open);
    }
}

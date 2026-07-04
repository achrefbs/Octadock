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

        viewModel.ReplaceItems(
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
        Action? open = null)
    {
        var now = new DateTimeOffset(2026, 7, 3, 20, 0, 0, TimeSpan.Zero);
        var record = new AiSessionRecord
        {
            Id = id,
            Provider = AiSessionProvider.Codex,
            Title = "Codex run",
            Status = AiSessionStatus.Running,
            StartedAt = now.AddMinutes(-2),
            LastEventAt = now.AddSeconds(-5),
        };

        return new AiSessionOverlayItemViewModel(record, now, dismiss, open);
    }
}

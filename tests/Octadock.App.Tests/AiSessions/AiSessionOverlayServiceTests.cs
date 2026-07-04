using FluentAssertions;
using Octadock.App.AiSessions;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests.AiSessions;

public sealed class AiSessionOverlayServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 3, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldShowRecord_keeps_active_sessions_even_when_recent_completions_are_hidden()
    {
        AiSessionRecord record = CreateRecord(AiSessionStatus.Running, Now.AddMinutes(-2));

        bool show = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: false,
            isDismissed: false,
            showRecentCompletions: false);

        show.Should().BeTrue();
    }

    [Fact]
    public void ShouldShowRecord_hides_recent_completed_sessions_when_setting_is_off()
    {
        AiSessionRecord record = CreateRecord(AiSessionStatus.Completed, Now.AddSeconds(-5));

        bool show = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: true,
            isDismissed: false,
            showRecentCompletions: false);

        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShowRecord_keeps_recent_completed_sessions_when_setting_is_on()
    {
        AiSessionRecord record = CreateRecord(AiSessionStatus.Completed, Now.AddSeconds(-5));

        bool show = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: true,
            isDismissed: false,
            showRecentCompletions: true);

        show.Should().BeTrue();
    }

    [Fact]
    public void ShouldShowRecord_hides_old_completed_sessions()
    {
        AiSessionRecord record = CreateRecord(AiSessionStatus.Completed, Now.AddMinutes(-2));

        bool show = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: true,
            isDismissed: false,
            showRecentCompletions: true);

        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShowRecord_hides_dismissed_sessions()
    {
        AiSessionRecord record = CreateRecord(AiSessionStatus.Running, Now.AddSeconds(-5));

        bool show = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: true,
            isDismissed: true,
            showRecentCompletions: true);

        show.Should().BeFalse();
    }

    [Fact]
    public void ShouldShowRecord_hides_process_discovery_completion_unless_it_was_observed_active()
    {
        AiSessionRecord record = CreateRecord(
            AiSessionStatus.Completed,
            Now.AddSeconds(-5),
            """{"source":"process-discovery"}""");

        bool neverObserved = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: false,
            isDismissed: false,
            showRecentCompletions: true);
        bool observed = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: true,
            isDismissed: false,
            showRecentCompletions: true);

        neverObserved.Should().BeFalse();
        observed.Should().BeTrue();
    }

    [Fact]
    public void ShouldShowRecord_hides_discovery_shell_helpers()
    {
        AiSessionRecord record = CreateRecord(
            AiSessionStatus.Running,
            Now.AddSeconds(-5),
            """{"source":"process-discovery","detector":"codex-desktop"}""");

        bool show = AiSessionOverlayService.ShouldShowRecord(
            record,
            Now,
            wasObservedActive: true,
            isDismissed: false,
            showRecentCompletions: true);

        show.Should().BeFalse();
    }

    private static AiSessionRecord CreateRecord(
        AiSessionStatus status,
        DateTimeOffset activity,
        string? metadataJson = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Provider = AiSessionProvider.Codex,
            Title = "Codex run",
            Status = status,
            StartedAt = Now.AddMinutes(-10),
            EndedAt = status is AiSessionStatus.Completed or AiSessionStatus.Failed or AiSessionStatus.Cancelled
                ? activity
                : null,
            LastEventAt = activity,
            MetadataJson = metadataJson,
        };
}

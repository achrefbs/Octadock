using FluentAssertions;
using Microsoft.Data.Sqlite;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class AiSessionRepositoryTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 3, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_then_Get_round_trips_all_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var session = RecordFactory.AiSession(
            provider: AiSessionProvider.ClaudeCode,
            status: AiSessionStatus.WaitingForInput,
            startedAt: BaseTime,
            lastEventAt: BaseTime.AddMinutes(5));

        await db.AiSessions.AddAsync(session);
        var loaded = await db.AiSessions.GetAsync(session.Id);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(session);
        loaded!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Add_then_Get_round_trips_null_optional_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var session = RecordFactory.AiSession() with
        {
            Cwd = null,
            GitBranch = null,
            Command = null,
            Pid = null,
            EndedAt = null,
            ExitCode = null,
            LastEventAt = null,
            MetadataJson = null,
        };

        await db.AiSessions.AddAsync(session);
        var loaded = await db.AiSessions.GetAsync(session.Id);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(session);
    }

    [Fact]
    public async Task Update_replaces_stored_session()
    {
        await using var db = await TestDatabase.CreateAsync();
        var session = RecordFactory.AiSession(status: AiSessionStatus.Running, startedAt: BaseTime);
        await db.AiSessions.AddAsync(session);

        var completed = session with
        {
            Status = AiSessionStatus.Completed,
            EndedAt = BaseTime.AddMinutes(20),
            ExitCode = 0,
            LastEventAt = BaseTime.AddMinutes(20),
            MetadataJson = "{\"summary\":\"done\"}",
        };
        await db.AiSessions.UpdateAsync(completed);

        var loaded = await db.AiSessions.GetAsync(session.Id);
        loaded!.Should().BeEquivalentTo(completed);
        loaded!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task List_filters_by_provider_status_date_and_search()
    {
        await using var db = await TestDatabase.CreateAsync();
        var match = RecordFactory.AiSession(
            provider: AiSessionProvider.Codex,
            status: AiSessionStatus.Running,
            startedAt: BaseTime) with
        {
            Title = "Implement AI session storage",
        };
        var wrongProvider = RecordFactory.AiSession(
            provider: AiSessionProvider.Cursor,
            status: AiSessionStatus.Running,
            startedAt: BaseTime.AddMinutes(1));
        var wrongStatus = RecordFactory.AiSession(
            provider: AiSessionProvider.Codex,
            status: AiSessionStatus.Completed,
            startedAt: BaseTime.AddMinutes(2));

        await db.AiSessions.AddAsync(match);
        await db.AiSessions.AddAsync(wrongProvider);
        await db.AiSessions.AddAsync(wrongStatus);

        var results = await db.AiSessions.ListAsync(new AiSessionFilter
        {
            Providers = new[] { AiSessionProvider.Codex },
            Statuses = new[] { AiSessionStatus.Running },
            StartedAfter = BaseTime.AddMinutes(-1),
            StartedBefore = BaseTime.AddMinutes(1),
            SearchText = "storage",
        });

        results.Select(s => s.Id).Should().ContainSingle().Which.Should().Be(match.Id);
    }

    [Fact]
    public async Task Events_round_trip_in_order_and_touch_last_event_at()
    {
        await using var db = await TestDatabase.CreateAsync();
        var session = RecordFactory.AiSession(startedAt: BaseTime, lastEventAt: null);
        await db.AiSessions.AddAsync(session);
        var later = RecordFactory.AiSessionEvent(
            session.Id,
            AiSessionEventType.Completed,
            createdAt: BaseTime.AddMinutes(10),
            message: "Done.");
        var earlier = RecordFactory.AiSessionEvent(
            session.Id,
            AiSessionEventType.Started,
            createdAt: BaseTime.AddMinutes(1),
            message: "Started.");

        await db.AiSessions.AddEventAsync(later);
        await db.AiSessions.AddEventAsync(earlier);

        var events = await db.AiSessions.GetEventsAsync(session.Id);
        events.Select(e => e.Id).Should().ContainInOrder(earlier.Id, later.Id);
        events.Should().BeEquivalentTo(new[] { earlier, later });

        var loadedSession = await db.AiSessions.GetAsync(session.Id);
        loadedSession!.LastEventAt.Should().Be(later.CreatedAt);
    }

    [Fact]
    public async Task Artifacts_round_trip_and_touch_last_event_at()
    {
        await using var db = await TestDatabase.CreateAsync();
        var session = RecordFactory.AiSession(startedAt: BaseTime, lastEventAt: null);
        var capture = RecordFactory.MinimalCapture();
        await db.AiSessions.AddAsync(session);
        await db.Captures.AddAsync(capture);
        var artifact = RecordFactory.AiSessionArtifact(
            session.Id,
            AiSessionArtifactKind.Capture,
            createdAt: BaseTime.AddMinutes(4),
            captureId: capture.Id);

        await db.AiSessions.AddArtifactAsync(artifact);

        var artifacts = await db.AiSessions.GetArtifactsAsync(session.Id);
        artifacts.Should().ContainSingle().Which.Should().BeEquivalentTo(artifact);
        (await db.AiSessions.GetAsync(session.Id))!.LastEventAt.Should().Be(artifact.CreatedAt);
    }

    [Fact]
    public async Task Events_and_artifacts_cascade_delete_with_session()
    {
        await using var db = await TestDatabase.CreateAsync();
        var session = RecordFactory.AiSession();
        await db.AiSessions.AddAsync(session);
        await db.AiSessions.AddEventAsync(RecordFactory.AiSessionEvent(session.Id));
        await db.AiSessions.AddArtifactAsync(RecordFactory.AiSessionArtifact(session.Id));

        await using (var connection = db.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM ai_sessions WHERE id = $id;";
            command.Parameters.AddWithValue("$id", session.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }

        (await db.AiSessions.GetEventsAsync(session.Id)).Should().BeEmpty();
        (await db.AiSessions.GetArtifactsAsync(session.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task AddEvent_rejects_missing_session_reference()
    {
        await using var db = await TestDatabase.CreateAsync();
        var evt = RecordFactory.AiSessionEvent(Guid.NewGuid());

        Func<Task> act = () => db.AiSessions.AddEventAsync(evt);

        await act.Should().ThrowAsync<SqliteException>();
    }
}

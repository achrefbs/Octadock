using FluentAssertions;
using Octadock.Core.Models;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class ActionRepositoryTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_then_GetForCapture_round_trips_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);
        var action = RecordFactory.Action(
            capture.Id,
            ActionType.Uploaded,
            createdAt: BaseTime,
            destination: "https://uploads.example.com/x",
            metadataJson: "{\"size\":123}");

        await db.Actions.AddAsync(action);
        var loaded = await db.Actions.GetForCaptureAsync(capture.Id);

        loaded.Should().ContainSingle().Which.Should().BeEquivalentTo(action);
    }

    [Fact]
    public async Task GetForCapture_orders_by_created_at_ascending()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);
        var third = RecordFactory.Action(capture.Id, ActionType.Copied, createdAt: BaseTime.AddMinutes(2));
        var first = RecordFactory.Action(capture.Id, ActionType.Shelved, createdAt: BaseTime);
        var second = RecordFactory.Action(capture.Id, ActionType.Annotated, createdAt: BaseTime.AddMinutes(1));

        // Insert out of chronological order.
        await db.Actions.AddAsync(third);
        await db.Actions.AddAsync(first);
        await db.Actions.AddAsync(second);

        var loaded = await db.Actions.GetForCaptureAsync(capture.Id);

        loaded.Select(a => a.Id).Should().ContainInOrder(first.Id, second.Id, third.Id);
    }

    [Fact]
    public async Task GetForCapture_returns_empty_for_capture_without_actions()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);

        var loaded = await db.Actions.GetForCaptureAsync(capture.Id);

        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task GetForCapture_isolates_actions_per_capture()
    {
        await using var db = await TestDatabase.CreateAsync();
        var captureA = RecordFactory.MinimalCapture();
        var captureB = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(captureA);
        await db.Captures.AddAsync(captureB);
        await db.Actions.AddAsync(RecordFactory.Action(captureA.Id));
        await db.Actions.AddAsync(RecordFactory.Action(captureB.Id));
        await db.Actions.AddAsync(RecordFactory.Action(captureB.Id));

        (await db.Actions.GetForCaptureAsync(captureA.Id)).Should().HaveCount(1);
        (await db.Actions.GetForCaptureAsync(captureB.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Actions_are_cascade_deleted_when_capture_is_hard_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);
        await db.Actions.AddAsync(RecordFactory.Action(capture.Id, ActionType.Copied));
        await db.Actions.AddAsync(RecordFactory.Action(capture.Id, ActionType.Saved));

        await db.Captures.HardDeleteAsync(capture.Id);

        var loaded = await db.Actions.GetForCaptureAsync(capture.Id);
        loaded.Should().BeEmpty("ON DELETE CASCADE should remove the capture's actions");
    }
}

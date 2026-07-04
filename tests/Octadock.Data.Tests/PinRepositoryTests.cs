using FluentAssertions;
using Microsoft.Data.Sqlite;
using Octadock.Core.Geometry;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class PinRepositoryTests
{
    [Fact]
    public async Task Upsert_inserts_new_pin_and_round_trips_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var captureId = Guid.NewGuid();
        await db.Captures.AddAsync(RecordFactory.MinimalCapture(id: captureId));
        var pin = RecordFactory.Pin(
            captureId: captureId,
            clickThrough: true,
            lastVisibleAt: new DateTimeOffset(2026, 6, 20, 8, 0, 0, TimeSpan.Zero));

        await db.Pins.UpsertAsync(pin);
        var loaded = await db.Pins.GetAsync(pin.Id);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(pin);
    }

    [Fact]
    public async Task Upsert_round_trips_null_capture_and_null_last_visible()
    {
        await using var db = await TestDatabase.CreateAsync();
        var pin = RecordFactory.Pin(
            captureId: null,
            imagePath: "Pins/source.png",
            lastVisibleAt: null);

        await db.Pins.UpsertAsync(pin);
        var loaded = await db.Pins.GetAsync(pin.Id);

        loaded!.CaptureId.Should().BeNull();
        loaded.ImagePath.Should().Be("Pins/source.png");
        loaded.LastVisibleAt.Should().BeNull();
        loaded.Should().BeEquivalentTo(pin);
    }

    [Fact]
    public async Task Upsert_updates_existing_pin_in_place()
    {
        await using var db = await TestDatabase.CreateAsync();
        var pin = RecordFactory.Pin();
        await db.Pins.UpsertAsync(pin);

        var moved = pin with
        {
            X = 500,
            Y = 600,
            Width = 640,
            Height = 480,
            Opacity = 0.5,
            ClickThrough = true,
            MonitorId = new MonitorId("\\\\.\\DISPLAY3"),
            LastVisibleAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await db.Pins.UpsertAsync(moved);

        var loaded = await db.Pins.GetAsync(pin.Id);
        loaded!.Should().BeEquivalentTo(moved);
        (await db.Pins.GetAllAsync()).Should().ContainSingle("upsert must update, not insert a duplicate");
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_id()
    {
        await using var db = await TestDatabase.CreateAsync();

        (await db.Pins.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetAll_returns_every_pin()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = RecordFactory.Pin(lastVisibleAt: new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var b = RecordFactory.Pin(lastVisibleAt: new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var c = RecordFactory.Pin(lastVisibleAt: null);
        await db.Pins.UpsertAsync(a);
        await db.Pins.UpsertAsync(b);
        await db.Pins.UpsertAsync(c);

        var all = await db.Pins.GetAllAsync();

        all.Select(p => p.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id, c.Id });
    }

    [Fact]
    public async Task Delete_removes_pin()
    {
        await using var db = await TestDatabase.CreateAsync();
        var pin = RecordFactory.Pin();
        await db.Pins.UpsertAsync(pin);

        await db.Pins.DeleteAsync(pin.Id);

        (await db.Pins.GetAsync(pin.Id)).Should().BeNull();
        (await db.Pins.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Pins_are_cascade_deleted_when_capture_is_hard_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);
        var pin = RecordFactory.Pin(captureId: capture.Id);
        await db.Pins.UpsertAsync(pin);

        await db.Captures.HardDeleteAsync(capture.Id);

        (await db.Pins.GetAsync(pin.Id)).Should().BeNull();
        (await db.Pins.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_rejects_missing_capture_reference()
    {
        await using var db = await TestDatabase.CreateAsync();
        var pin = RecordFactory.Pin(captureId: Guid.NewGuid());

        Func<Task> act = () => db.Pins.UpsertAsync(pin);

        await act.Should().ThrowAsync<SqliteException>();
    }
}

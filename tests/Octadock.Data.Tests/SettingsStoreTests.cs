using FluentAssertions;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task Set_then_Get_returns_value()
    {
        await using var db = await TestDatabase.CreateAsync();

        await db.Settings.SetAsync("history.retentionDays", "30");

        (await db.Settings.GetAsync("history.retentionDays")).Should().Be("30");
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_key()
    {
        await using var db = await TestDatabase.CreateAsync();

        (await db.Settings.GetAsync("does.not.exist")).Should().BeNull();
    }

    [Fact]
    public async Task Set_overwrites_value_and_updates_timestamp()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = await TestDatabase.CreateAsync(clock);
        await db.Settings.SetAsync("capture.defaultAction", "shelf");
        var firstTimestamp = await ReadUpdatedAtAsync(db, "capture.defaultAction");

        clock.Advance(TimeSpan.FromHours(1));
        await db.Settings.SetAsync("capture.defaultAction", "copy");

        (await db.Settings.GetAsync("capture.defaultAction")).Should().Be("copy");
        var secondTimestamp = await ReadUpdatedAtAsync(db, "capture.defaultAction");
        secondTimestamp.Should().BeAfter(firstTimestamp);
    }

    [Fact]
    public async Task GetAll_returns_every_setting()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Settings.SetAsync("a", "1");
        await db.Settings.SetAsync("b", "2");

        var all = await db.Settings.GetAllAsync();

        all.Should().HaveCount(2);
        all["a"].Should().Be("1");
        all["b"].Should().Be("2");
    }

    [Fact]
    public async Task GetAll_returns_empty_dictionary_when_no_settings()
    {
        await using var db = await TestDatabase.CreateAsync();

        (await db.Settings.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SetMany_persists_all_values()
    {
        await using var db = await TestDatabase.CreateAsync();
        var values = new Dictionary<string, string>
        {
            ["general.launchAtLogin"] = "true",
            ["ocr.provider"] = "windows",
            ["recording.fps"] = "60",
        };

        await db.Settings.SetManyAsync(values);

        var all = await db.Settings.GetAllAsync();
        all.Should().BeEquivalentTo(values);
    }

    [Fact]
    public async Task SetMany_updates_existing_and_shares_one_timestamp()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await using var db = await TestDatabase.CreateAsync(clock);
        await db.Settings.SetAsync("k1", "old");

        clock.Advance(TimeSpan.FromMinutes(5));
        await db.Settings.SetManyAsync(new Dictionary<string, string>
        {
            ["k1"] = "new",
            ["k2"] = "brand-new",
        });

        (await db.Settings.GetAsync("k1")).Should().Be("new");
        (await db.Settings.GetAsync("k2")).Should().Be("brand-new");

        var t1 = await ReadUpdatedAtAsync(db, "k1");
        var t2 = await ReadUpdatedAtAsync(db, "k2");
        t1.Should().Be(t2, "all rows in one SetMany transaction share the same timestamp");
    }

    [Fact]
    public async Task SetMany_with_empty_dictionary_is_noop()
    {
        await using var db = await TestDatabase.CreateAsync();

        await db.Settings.SetManyAsync(new Dictionary<string, string>());

        (await db.Settings.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_deletes_key()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Settings.SetAsync("temp.key", "value");

        await db.Settings.RemoveAsync("temp.key");

        (await db.Settings.GetAsync("temp.key")).Should().BeNull();
    }

    [Fact]
    public async Task Remove_unknown_key_does_not_throw()
    {
        await using var db = await TestDatabase.CreateAsync();

        var act = async () => await db.Settings.RemoveAsync("nope");

        await act.Should().NotThrowAsync();
    }

    private static async Task<DateTimeOffset> ReadUpdatedAtAsync(TestDatabase db, string key)
    {
        await using var connection = db.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT updated_at FROM settings WHERE key = $key;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        var raw = (string)(await command.ExecuteScalarAsync())!;
        return DateTimeOffset.Parse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
    }
}

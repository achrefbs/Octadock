using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Repositories;
using Octadock.Data.Sqlite;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class CaptureRepositoryTests
{
    [Fact]
    public async Task Add_then_Get_round_trips_all_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.FullCapture();

        await db.Captures.AddAsync(capture);
        var loaded = await db.Captures.GetAsync(capture.Id);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(capture);
    }

    [Fact]
    public async Task Add_then_Get_round_trips_null_optional_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();

        await db.Captures.AddAsync(capture);
        var loaded = await db.Captures.GetAsync(capture.Id);

        loaded.Should().NotBeNull();
        loaded!.Source.Should().Be(CaptureSource.Empty);
        loaded.ThumbnailPath.Should().BeNull();
        loaded.ProjectPath.Should().BeNull();
        loaded.ApprovedMockupPath.Should().BeNull();
        loaded.DurationMs.Should().BeNull();
        loaded.DeletedAt.Should().BeNull();
        loaded.Should().BeEquivalentTo(capture);
    }

    [Fact]
    public async Task Capture_remains_queryable_after_a_database_restart()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        CaptureRecord capture = RecordFactory.FullCapture();
        await db.Captures.AddAsync(capture);
        await db.Database.CheckpointAsync();

        SqliteConnectionFactory restartedFactory = new(
            SqliteConnectionFactory.BuildFileConnectionString(db.DatabasePath),
            NullLogger<SqliteConnectionFactory>.Instance);
        using var restartedDatabase = new OctadockDatabase(
            restartedFactory,
            NullLogger<OctadockDatabase>.Instance);
        await restartedDatabase.InitializeAsync();
        var restartedRepository = new CaptureRepository(
            restartedFactory,
            NullLogger<CaptureRepository>.Instance);

        CaptureRecord? recovered = await restartedRepository.GetAsync(capture.Id);

        recovered.Should().BeEquivalentTo(capture);
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_id()
    {
        await using var db = await TestDatabase.CreateAsync();

        var loaded = await db.Captures.GetAsync(Guid.NewGuid());

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task CreatedAt_is_normalized_to_utc_on_round_trip()
    {
        await using var db = await TestDatabase.CreateAsync();
        var withOffset = RecordFactory.MinimalCapture(createdAt: new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(5)));

        await db.Captures.AddAsync(withOffset);
        var loaded = await db.Captures.GetAsync(withOffset.Id);

        loaded!.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        loaded.CreatedAt.Should().Be(withOffset.CreatedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Update_replaces_stored_record()
    {
        await using var db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);

        var updated = capture with
        {
            ThumbnailPath = "Thumbnails\\new.jpg",
            ProjectPath = "Projects\\new.octadock",
            ApprovedMockupPath = "Mockups\\new.png",
            DurationMs = 999,
        };
        await db.Captures.UpdateAsync(updated);

        var loaded = await db.Captures.GetAsync(capture.Id);
        loaded!.Should().BeEquivalentTo(updated);
    }

    [Fact]
    public async Task GetRecent_returns_newest_first_and_excludes_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var oldest = RecordFactory.MinimalCapture(createdAt: baseTime);
        var middle = RecordFactory.MinimalCapture(createdAt: baseTime.AddHours(1));
        var newest = RecordFactory.MinimalCapture(createdAt: baseTime.AddHours(2));
        var deleted = RecordFactory.MinimalCapture(createdAt: baseTime.AddHours(3)) with { DeletedAt = baseTime.AddHours(4) };

        foreach (var c in new[] { oldest, middle, newest, deleted })
        {
            await db.Captures.AddAsync(c);
        }

        var recent = await db.Captures.GetRecentAsync(10);

        recent.Select(c => c.Id).Should().ContainInOrder(newest.Id, middle.Id, oldest.Id);
        recent.Should().NotContain(c => c.Id == deleted.Id);
    }

    [Fact]
    public async Task GetRecent_respects_count_limit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
        {
            await db.Captures.AddAsync(RecordFactory.MinimalCapture(createdAt: baseTime.AddMinutes(i)));
        }

        var recent = await db.Captures.GetRecentAsync(2);

        recent.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecent_with_zero_count_returns_no_rows()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Captures.AddAsync(RecordFactory.MinimalCapture());

        var recent = await db.Captures.GetRecentAsync(0);

        recent.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_filters_by_single_type()
    {
        await using var db = await TestDatabase.CreateAsync();
        var area = RecordFactory.MinimalCapture(type: CaptureType.Area);
        var window = RecordFactory.MinimalCapture(type: CaptureType.Window);
        var recording = RecordFactory.MinimalCapture(type: CaptureType.Recording);
        foreach (var c in new[] { area, window, recording })
        {
            await db.Captures.AddAsync(c);
        }

        var result = await db.Captures.QueryAsync(new CaptureFilter { Types = new[] { CaptureType.Window } });

        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(window.Id);
    }

    [Fact]
    public async Task Query_filters_by_multiple_types()
    {
        await using var db = await TestDatabase.CreateAsync();
        var area = RecordFactory.MinimalCapture(type: CaptureType.Area);
        var window = RecordFactory.MinimalCapture(type: CaptureType.Window);
        var recording = RecordFactory.MinimalCapture(type: CaptureType.Recording);
        foreach (var c in new[] { area, window, recording })
        {
            await db.Captures.AddAsync(c);
        }

        var result = await db.Captures.QueryAsync(new CaptureFilter
        {
            Types = new[] { CaptureType.Area, CaptureType.Recording },
        });

        result.Select(c => c.Id).Should().BeEquivalentTo(new[] { area.Id, recording.Id });
    }

    [Fact]
    public async Task Query_filters_by_date_range()
    {
        await using var db = await TestDatabase.CreateAsync();
        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var before = RecordFactory.MinimalCapture(createdAt: baseTime);
        var inRange = RecordFactory.MinimalCapture(createdAt: baseTime.AddDays(5));
        var after = RecordFactory.MinimalCapture(createdAt: baseTime.AddDays(10));
        foreach (var c in new[] { before, inRange, after })
        {
            await db.Captures.AddAsync(c);
        }

        var result = await db.Captures.QueryAsync(new CaptureFilter
        {
            CreatedAfter = baseTime.AddDays(1),
            CreatedBefore = baseTime.AddDays(9),
        });

        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(inRange.Id);
    }

    [Fact]
    public async Task Query_date_range_boundaries_are_inclusive()
    {
        await using var db = await TestDatabase.CreateAsync();
        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var atStart = RecordFactory.MinimalCapture(createdAt: start);
        var atEnd = RecordFactory.MinimalCapture(createdAt: end);
        await db.Captures.AddAsync(atStart);
        await db.Captures.AddAsync(atEnd);

        var result = await db.Captures.QueryAsync(new CaptureFilter
        {
            CreatedAfter = start,
            CreatedBefore = end,
        });

        result.Select(c => c.Id).Should().BeEquivalentTo(new[] { atStart.Id, atEnd.Id });
    }

    [Fact]
    public async Task Query_search_matches_process_or_window()
    {
        await using var db = await TestDatabase.CreateAsync();
        var chrome = RecordFactory.MinimalCapture() with { Source = new CaptureSource("chrome.exe", "GitHub", null) };
        var explorerWindow = RecordFactory.MinimalCapture() with { Source = new CaptureSource("explorer.exe", "Chrome downloads", null) };
        var unrelated = RecordFactory.MinimalCapture() with { Source = new CaptureSource("notepad.exe", "Untitled", null) };
        foreach (var c in new[] { chrome, explorerWindow, unrelated })
        {
            await db.Captures.AddAsync(c);
        }

        var result = await db.Captures.QueryAsync(new CaptureFilter { SearchText = "chrome" });

        result.Select(c => c.Id).Should().BeEquivalentTo(new[] { chrome.Id, explorerWindow.Id });
    }

    [Fact]
    public async Task Query_search_treats_wildcards_as_literals()
    {
        await using var db = await TestDatabase.CreateAsync();
        var literal = RecordFactory.MinimalCapture() with { Source = new CaptureSource("100%.exe", null, null) };
        var wouldMatchWildcard = RecordFactory.MinimalCapture() with { Source = new CaptureSource("abc.exe", null, null) };
        await db.Captures.AddAsync(literal);
        await db.Captures.AddAsync(wouldMatchWildcard);

        // "%" must be matched literally, not as a wildcard that matches everything.
        var result = await db.Captures.QueryAsync(new CaptureFilter { SearchText = "100%" });

        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(literal.Id);
    }

    [Fact]
    public async Task Query_excludes_deleted_by_default()
    {
        await using var db = await TestDatabase.CreateAsync();
        var live = RecordFactory.MinimalCapture();
        var deleted = RecordFactory.MinimalCapture() with { DeletedAt = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero) };
        await db.Captures.AddAsync(live);
        await db.Captures.AddAsync(deleted);

        var result = await db.Captures.QueryAsync(new CaptureFilter());

        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(live.Id);
    }

    [Fact]
    public async Task Query_includes_deleted_when_requested()
    {
        await using var db = await TestDatabase.CreateAsync();
        var live = RecordFactory.MinimalCapture();
        var deleted = RecordFactory.MinimalCapture() with { DeletedAt = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero) };
        await db.Captures.AddAsync(live);
        await db.Captures.AddAsync(deleted);

        var result = await db.Captures.QueryAsync(new CaptureFilter { IncludeDeleted = true });

        result.Select(c => c.Id).Should().BeEquivalentTo(new[] { live.Id, deleted.Id });
    }

    [Fact]
    public async Task Query_filters_by_has_project()
    {
        await using var db = await TestDatabase.CreateAsync();
        var withProject = RecordFactory.MinimalCapture() with { ProjectPath = "Projects\\p.octadock" };
        var withoutProject = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(withProject);
        await db.Captures.AddAsync(withoutProject);

        var hasProject = await db.Captures.QueryAsync(new CaptureFilter { HasProject = true });
        var noProject = await db.Captures.QueryAsync(new CaptureFilter { HasProject = false });

        hasProject.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(withProject.Id);
        noProject.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(withoutProject.Id);
    }

    [Fact]
    public async Task Query_sorts_newest_and_oldest_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var first = RecordFactory.MinimalCapture(createdAt: baseTime);
        var second = RecordFactory.MinimalCapture(createdAt: baseTime.AddHours(1));
        var third = RecordFactory.MinimalCapture(createdAt: baseTime.AddHours(2));
        foreach (var c in new[] { second, first, third })
        {
            await db.Captures.AddAsync(c);
        }

        var newestFirst = await db.Captures.QueryAsync(new CaptureFilter { SortOrder = CaptureSortOrder.NewestFirst });
        var oldestFirst = await db.Captures.QueryAsync(new CaptureFilter { SortOrder = CaptureSortOrder.OldestFirst });

        newestFirst.Select(c => c.Id).Should().ContainInOrder(third.Id, second.Id, first.Id);
        oldestFirst.Select(c => c.Id).Should().ContainInOrder(first.Id, second.Id, third.Id);
    }

    [Fact]
    public async Task Query_pages_with_limit_and_offset()
    {
        await using var db = await TestDatabase.CreateAsync();
        var baseTime = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var captures = Enumerable.Range(0, 5)
            .Select(i => RecordFactory.MinimalCapture(createdAt: baseTime.AddMinutes(i)))
            .ToList();
        foreach (var c in captures)
        {
            await db.Captures.AddAsync(c);
        }

        var page = await db.Captures.QueryAsync(new CaptureFilter
        {
            SortOrder = CaptureSortOrder.OldestFirst,
            Limit = 2,
            Offset = 1,
        });

        page.Select(c => c.Id).Should().ContainInOrder(captures[1].Id, captures[2].Id);
        page.Should().HaveCount(2);
    }

    [Fact]
    public async Task Query_with_zero_limit_returns_no_rows()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Captures.AddAsync(RecordFactory.MinimalCapture());

        var result = await db.Captures.QueryAsync(new CaptureFilter { Limit = 0 });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_falls_back_for_unknown_enum_values_in_rows()
    {
        await using var db = await TestDatabase.CreateAsync();
        var id = Guid.NewGuid();

        await using (var connection = db.OpenConnection())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO captures (
                    id, type, created_at, monitor_id, pixel_width, pixel_height, dpi_scale, original_path)
                VALUES (
                    $id, $type, $created_at, $monitor_id, $pixel_width, $pixel_height, $dpi_scale, $original_path);
                """;
            SqliteValues.AddParameter(command, "$id", id.ToString());
            SqliteValues.AddParameter(command, "$type", "FutureCaptureKind");
            SqliteValues.AddParameter(command, "$created_at", SqliteValues.ToStorage(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
            SqliteValues.AddParameter(command, "$monitor_id", "\\\\.\\DISPLAY1");
            SqliteValues.AddParameter(command, "$pixel_width", 100);
            SqliteValues.AddParameter(command, "$pixel_height", 80);
            SqliteValues.AddParameter(command, "$dpi_scale", 1.0);
            SqliteValues.AddParameter(command, "$original_path", "Captures\\future.png");
            await command.ExecuteNonQueryAsync();
        }

        var result = await db.Captures.QueryAsync(new CaptureFilter());

        result.Should().ContainSingle();
        result[0].Id.Should().Be(id);
        result[0].Type.Should().Be(default(CaptureType));
    }

    [Fact]
    public async Task Count_honors_filter()
    {
        await using var db = await TestDatabase.CreateAsync();
        for (var i = 0; i < 3; i++)
        {
            await db.Captures.AddAsync(RecordFactory.MinimalCapture(type: CaptureType.Area));
        }

        await db.Captures.AddAsync(RecordFactory.MinimalCapture(type: CaptureType.Window));
        await db.Captures.AddAsync(RecordFactory.MinimalCapture() with { DeletedAt = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero) });

        (await db.Captures.CountAsync(new CaptureFilter())).Should().Be(4); // excludes the deleted one
        (await db.Captures.CountAsync(new CaptureFilter { Types = new[] { CaptureType.Area } })).Should().Be(3);
        (await db.Captures.CountAsync(new CaptureFilter { IncludeDeleted = true })).Should().Be(5);
    }
}

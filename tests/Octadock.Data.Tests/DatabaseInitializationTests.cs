using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Context;
using Octadock.Data.Sqlite;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class DatabaseInitializationTests
{
    private static readonly string[] ExpectedTables =
    [
        "captures",
        "actions",
        "pins",
        "settings",
        "clipboard_clips",
        "context_packages",
        "context_items",
        "context_item_derivatives",
    ];

    // Created by migration 4 and dropped by migration 6 (AI Sessions removal).
    private static readonly string[] RemovedTables =
    [
        "ai_sessions",
        "ai_session_events",
        "ai_session_artifacts",
    ];

    private static readonly string[] ExpectedIndexes =
    [
        "ix_captures_created_at",
        "ix_captures_type",
        "ix_captures_deleted_at",
        "ix_actions_capture_id",
        "ix_pins_capture_id",
        "ix_clipboard_clips_created_at",
        "ix_clipboard_clips_last_seen_at",
        "ix_clipboard_clips_kind",
        "ix_clipboard_clips_deleted_at",
        "ix_clipboard_clips_favorite",
        "ix_clipboard_clips_content_hash",
        "ix_context_items_package",
        "ix_context_items_source_capture",
        "ix_context_item_derivatives_item",
    ];

    [Fact]
    public async Task Initialize_creates_all_tables()
    {
        await using var db = await TestDatabase.CreateAsync();

        var tables = await ReadObjectsAsync(db, "table");

        tables.Should().Contain(ExpectedTables);
    }

    [Fact]
    public async Task Initialize_drops_removed_ai_session_tables()
    {
        await using var db = await TestDatabase.CreateAsync();

        var tables = await ReadObjectsAsync(db, "table");

        tables.Should().NotContain(RemovedTables);
    }

    [Fact]
    public async Task Initialize_creates_expected_indexes()
    {
        await using var db = await TestDatabase.CreateAsync();

        var indexes = await ReadObjectsAsync(db, "index");

        indexes.Should().Contain(ExpectedIndexes);
    }

    [Fact]
    public async Task Initialize_sets_user_version_to_latest()
    {
        await using var db = await TestDatabase.CreateAsync();

        var version = await ReadUserVersionAsync(db);

        version.Should().Be(SchemaMigrations.LatestVersion);
    }

    [Fact]
    public async Task Initialize_is_idempotent_when_run_twice()
    {
        await using var db = await TestDatabase.CreateAsync();

        // Second and third calls must not throw (e.g., "table already exists").
        var act = async () =>
        {
            await db.Database.InitializeAsync();
            await db.Database.InitializeAsync();
        };

        await act.Should().NotThrowAsync();
        (await ReadUserVersionAsync(db)).Should().Be(SchemaMigrations.LatestVersion);

        // The schema must still contain unique table names, not duplicates.
        var tables = await ReadObjectsAsync(db, "table");
        tables.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Foreign_keys_pragma_is_enabled_on_connections()
    {
        await using var db = await TestDatabase.CreateAsync();

        await using var connection = db.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        var result = Convert.ToInt64(await command.ExecuteScalarAsync());

        result.Should().Be(1);
    }

    [Fact]
    public async Task Initialize_creates_pin_capture_foreign_key()
    {
        await using var db = await TestDatabase.CreateAsync();

        var foreignKeys = await ReadForeignKeysAsync(db.ConnectionFactory);

        foreignKeys.Should().Contain(fk =>
            fk.Table == "captures" &&
            fk.From == "capture_id" &&
            fk.To == "id" &&
            fk.OnDelete == "CASCADE");
    }

    [Fact]
    public async Task Initialize_migrates_v1_pins_to_capture_foreign_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"octadock-migration-{Guid.NewGuid():N}.db");
        var connectionString = SqliteConnectionFactory.BuildFileConnectionString(path);
        var factory = new SqliteConnectionFactory(connectionString, NullLogger<SqliteConnectionFactory>.Instance);
        var database = new OctadockDatabase(factory, NullLogger<OctadockDatabase>.Instance);

        Guid captureId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Guid validPinId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Guid orphanPinId = Guid.Parse("ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb");

        try
        {
            await CreateVersion1DatabaseWithPinsAsync(factory, captureId, validPinId, orphanPinId);

            await database.InitializeAsync();

            (await ReadUserVersionAsync(factory)).Should().Be(SchemaMigrations.LatestVersion);
            var foreignKeys = await ReadForeignKeysAsync(factory);
            foreignKeys.Should().Contain(fk =>
                fk.Table == "captures" &&
                fk.From == "capture_id" &&
                fk.To == "id" &&
                fk.OnDelete == "CASCADE");

            var migratedPins = await ReadPinIdsAsync(factory);
            migratedPins.Should().ContainSingle().Which.Should().Be(validPinId.ToString());

            await DeleteCaptureAsync(factory, captureId);

            (await ReadPinIdsAsync(factory)).Should().BeEmpty("the migrated pin FK should cascade on hard delete");
        }
        finally
        {
            database.Dispose();
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public async Task Initialize_migrates_v4_database_to_clipboard_clips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"octadock-migration-{Guid.NewGuid():N}.db");
        var connectionString = SqliteConnectionFactory.BuildFileConnectionString(path);
        var factory = new SqliteConnectionFactory(connectionString, NullLogger<SqliteConnectionFactory>.Instance);
        var database = new OctadockDatabase(factory, NullLogger<OctadockDatabase>.Instance);

        try
        {
            await CreateVersion4DatabaseAsync(factory);

            await database.InitializeAsync();

            (await ReadUserVersionAsync(factory)).Should().Be(SchemaMigrations.LatestVersion);
            (await ReadObjectsAsync(factory, "table")).Should().Contain("clipboard_clips");
            (await ReadObjectsAsync(factory, "index")).Should().Contain("ix_clipboard_clips_last_seen_at");
        }
        finally
        {
            database.Dispose();
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public async Task Initialize_migrates_existing_context_packages_to_persisted_notes()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        ContextPackage package = await db.Context.CreatePackageAsync(
            "Existing Context",
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero));
        await using (SqliteConnection connection = await db.ConnectionFactory.OpenConnectionAsync())
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "ALTER TABLE context_packages DROP COLUMN notes; PRAGMA user_version = 8;";
            await command.ExecuteNonQueryAsync();
        }

        await db.Database.InitializeAsync();

        ContextPackage migrated = (await db.Context.GetPackageAsync(package.Id))!;
        migrated.Notes.Should().BeEmpty();
        await db.Context.UpdatePackageNotesAsync(
            package.Id,
            "Recovered after restart",
            DateTimeOffset.UtcNow);
        (await db.Context.GetPackageAsync(package.Id))!.Notes.Should().Be("Recovered after restart");
    }

    [Fact]
    public async Task Salvage_keeps_readable_context_rows_when_capture_or_context_parents_are_missing()
    {
        await using TestDatabase damaged = await TestDatabase.CreateAsync();
        Guid packageId = Guid.NewGuid();
        Guid missingPackageId = Guid.NewGuid();
        Guid itemWithMissingCaptureId = Guid.NewGuid();
        Guid independentItemId = Guid.NewGuid();
        Guid orphanItemId = Guid.NewGuid();
        Guid missingCaptureId = Guid.NewGuid();
        Guid derivativeId = Guid.NewGuid();
        Guid orphanDerivativeId = Guid.NewGuid();

        await using (SqliteConnection connection = await damaged.ConnectionFactory.OpenConnectionAsync())
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                $$"""
                PRAGMA foreign_keys = OFF;

                INSERT INTO context_packages (id, name, created_at, updated_at, notes)
                VALUES ('{{packageId}}', 'Recovered Context', '2026-07-19T10:00:00.0000000Z',
                        '2026-07-19T10:00:00.0000000Z', 'salvage me');

                INSERT INTO context_items (
                    id, package_id, display_name, ownership, storage_path, reference_source,
                    reference_sha256, size_bytes, source_capture_id, added_at, sort_order)
                VALUES (
                    '{{itemWithMissingCaptureId}}', '{{packageId}}', 'missing-parent.txt', 'snapshot',
                    'Context/missing-parent.txt', NULL, NULL, 12, '{{missingCaptureId}}',
                    '2026-07-19T10:00:00.0000000Z', 0),
                    ('{{independentItemId}}', '{{packageId}}', 'independent.txt', 'snapshot',
                    'Context/independent.txt', NULL, NULL, 11, NULL,
                    '2026-07-19T10:01:00.0000000Z', 1),
                    ('{{orphanItemId}}', '{{missingPackageId}}', 'orphan.txt', 'snapshot',
                    'Context/orphan.txt', NULL, NULL, 6, NULL,
                    '2026-07-19T10:02:00.0000000Z', 2);

                INSERT INTO context_item_derivatives (id, item_id, kind, storage_path)
                VALUES ('{{derivativeId}}', '{{itemWithMissingCaptureId}}', 'ocr', 'Context/ocr.txt'),
                       ('{{orphanDerivativeId}}', '{{orphanItemId}}', 'ocr', 'Context/orphan-ocr.txt');
                """;
            await command.ExecuteNonQueryAsync();
        }

        await damaged.Database.CheckpointAsync();
        await using TestDatabase recovered = await TestDatabase.CreateAsync();

        await recovered.Database.TrySalvageAsync(damaged.DatabasePath);

        ContextPackage package = (await recovered.Context.GetPackageAsync(packageId))!;
        package.Notes.Should().Be("salvage me");
        package.Items.Should().HaveCount(2);
        package.Items.Single(item => item.Id == itemWithMissingCaptureId).SourceCaptureId.Should().BeNull();
        package.Items.Single(item => item.Id == itemWithMissingCaptureId).Derivatives
            .Should().ContainSingle(derivative => derivative.Kind == ContextDerivativeKind.Ocr);
        package.Items.Should().Contain(item => item.Id == independentItemId);
        (await recovered.Context.GetPackageAsync(missingPackageId)).Should().BeNull();
    }

    [Fact]
    public async Task Journal_mode_is_wal_on_connections()
    {
        await using var db = await TestDatabase.CreateAsync();

        await using var connection = db.OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = (string)(await command.ExecuteScalarAsync())!;

        mode.Should().BeEquivalentTo("wal");
    }

    private static async Task<IReadOnlyList<string>> ReadObjectsAsync(TestDatabase db, string type)
        => await ReadObjectsAsync(db.ConnectionFactory, type);

    private static async Task<IReadOnlyList<string>> ReadObjectsAsync(SqliteConnectionFactory factory, string type)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$type";
        parameter.Value = type;
        command.Parameters.Add(parameter);

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<long> ReadUserVersionAsync(TestDatabase db)
        => await ReadUserVersionAsync(db.ConnectionFactory);

    private static async Task<long> ReadUserVersionAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<ForeignKeyRow>> ReadForeignKeysAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_list(pins);";

        var rows = new List<ForeignKeyRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ForeignKeyRow(
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(6)));
        }

        return rows;
    }

    private static async Task CreateVersion1DatabaseWithPinsAsync(
        SqliteConnectionFactory factory,
        Guid captureId,
        Guid validPinId,
        Guid orphanPinId)
    {
        string capture = captureId.ToString();
        string validPin = validPinId.ToString();
        string orphanPin = orphanPinId.ToString();
        string orphanCapture = Guid.Parse("99999999-8888-7777-6666-555555555555").ToString();

        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            {{SchemaMigrations.All[0].Sql}}

            PRAGMA user_version = 1;

            INSERT INTO captures (
                id, type, created_at, monitor_id, pixel_width, pixel_height, dpi_scale, original_path)
            VALUES (
                '{{capture}}', 'Area', '2026-06-01T00:00:00.0000000Z',
                '\\.\DISPLAY1', 100, 80, 1.0, 'Captures/2026/06/01/capture.png');

            INSERT INTO pins (
                id, capture_id, x, y, width, height, opacity, click_through, monitor_id, last_visible_at)
            VALUES (
                '{{validPin}}', '{{capture}}', 10, 20, 300, 200, 0.9, 0, '\\.\DISPLAY1', NULL);

            INSERT INTO pins (
                id, capture_id, x, y, width, height, opacity, click_through, monitor_id, last_visible_at)
            VALUES (
                '{{orphanPin}}', '{{orphanCapture}}', 40, 50, 300, 200, 0.9, 0, '\\.\DISPLAY1', NULL);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateVersion4DatabaseAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            {{SchemaMigrations.All[0].Sql}}
            {{SchemaMigrations.All[1].Sql}}
            {{SchemaMigrations.All[2].Sql}}
            {{SchemaMigrations.All[3].Sql}}

            PRAGMA user_version = 4;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadPinIdsAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM pins ORDER BY id ASC;";

        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task DeleteCaptureAsync(SqliteConnectionFactory factory, Guid captureId)
    {
        await using var connection = await factory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM captures WHERE id = $id;";
        command.Parameters.AddWithValue("$id", captureId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (string suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            string file = path + suffix;
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed record ForeignKeyRow(string Table, string From, string To, string OnDelete);
}

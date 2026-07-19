using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Persistence;

namespace Octadock.Data.Sqlite;

/// <summary>
/// SQLite implementation of <see cref="IOctadockDatabase"/>. Ensures the database
/// file exists and brings its schema up to <see cref="SchemaMigrations.LatestVersion"/>
/// by running any outstanding migrations. Idempotent: calling
/// <see cref="InitializeAsync"/> repeatedly is a no-op once the schema is current.
/// The current version is tracked with SQLite's <c>PRAGMA user_version</c>.
///
/// Self-healing: startup runs <c>PRAGMA quick_check</c>. When the file is
/// corrupted (e.g. a force-killed process mid-write), the damaged database is
/// quarantined beside itself as <c>octadock.db.corrupt-&lt;stamp&gt;</c>, a fresh
/// schema is created, and the readable user tables (settings, captures,
/// actions, pins, clipboard clips, and Context packages) are salvaged best-effort — so the app never
/// stays broken behind a corrupt store.
/// </summary>
public sealed partial class OctadockDatabase : IOctadockDatabase, IDisposable
{
    private static readonly string[] SalvageTables =
    [
        "settings",
        "captures",
        "actions",
        "pins",
        "clipboard_clips",
        "context_packages",
        "context_items",
        "context_item_derivatives",
    ];

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<OctadockDatabase> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    /// <summary>Creates the database initializer over the given connection factory.</summary>
    public OctadockDatabase(ISqliteConnectionFactory connectionFactory, ILogger<OctadockDatabase> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Releases the initialization gate.</summary>
    public void Dispose() => _initializationGate.Dispose();

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? databasePath = null;
            try
            {
                databasePath = await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SqliteException ex) when (IsCorruption(ex))
            {
                LogDatabaseCorrupt(ex);
            }
            catch (DatabaseCorruptException ex)
            {
                LogDatabaseCorruptCheck(ex.Detail);
                databasePath = ex.DatabasePath;
            }

            // Recovery path: quarantine the damaged file, rebuild, salvage.
            string? backupPath = TryQuarantine(databasePath);
            if (backupPath is null)
            {
                throw new InvalidOperationException(
                    "The Octadock database is corrupted and could not be quarantined for rebuild.");
            }

            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
            await TrySalvageAsync(backupPath, cancellationToken).ConfigureAwait(false);
            LogDatabaseRebuilt(backupPath);
        }
        finally
        {
            _initializationGate.Release();
        }

        // Fold whatever initialization wrote into the base file immediately, so
        // even a force-killed process cannot roll the store back to genesis.
        await CheckpointAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            // PRAGMA wal_checkpoint reports contention through its result row
            // (busy, log frames, checkpointed frames) instead of throwing. A
            // busy result silently left every row in the WAL forever, so the
            // main file stayed at genesis and any WAL/SHM mishap lost all data.
            // Retry a few times and downgrade to PASSIVE before giving up loud.
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = attempt < 3
                    ? "PRAGMA wal_checkpoint(TRUNCATE);"
                    : "PRAGMA wal_checkpoint(PASSIVE);";
                await using var reader = await checkpoint.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                long busy = 0;
                long checkpointed = -1;
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    busy = reader.GetInt64(0);
                    checkpointed = reader.GetInt64(2);
                }

                if (busy == 0)
                {
                    LogCheckpointed();
                    return;
                }

                LogCheckpointBusy(attempt, checkpointed);
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            LogCheckpointFailed(ex);
        }
    }

    /// <summary>Runs the health check and migrations; returns the on-disk path (or null for in-memory).</summary>
    private async Task<string?> InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        string? databasePath = ResolveFilePath(connection);

        // Detect torn writes early instead of failing on some later query.
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "PRAGMA quick_check(1);";
            var verdict = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(verdict, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new DatabaseCorruptException(databasePath, verdict ?? "unknown");
            }
        }

        var currentVersion = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        LogOpenedDatabase(databasePath ?? ":memory:", currentVersion);
        if (currentVersion >= SchemaMigrations.LatestVersion)
        {
            LogSchemaAlreadyCurrent(currentVersion);
            return databasePath;
        }

        LogMigratingSchema(currentVersion, SchemaMigrations.LatestVersion);

        foreach (var migration in SchemaMigrations.All)
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
            LogAppliedSchemaMigration(migration.Version);
        }

        return databasePath;
    }

    /// <summary>Moves the damaged database (and WAL/SHM) beside itself; null when impossible.</summary>
    private string? TryQuarantine(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            // Pooled handles must be released before the file can be renamed.
            SqliteConnection.ClearAllPools();

            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string backupPath = $"{databasePath}.corrupt-{stamp}";
            File.Move(databasePath, backupPath);
            foreach (string suffix in (string[])["-wal", "-shm"])
            {
                string sidecar = databasePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Move(sidecar, backupPath + suffix, overwrite: true);
                }
            }

            return backupPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogQuarantineFailed(ex);
            return null;
        }
    }

    /// <summary>Copies whatever user rows are still readable out of the quarantined file.</summary>
    internal async Task TrySalvageAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (var attach = connection.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE $path AS damaged;";
                attach.Parameters.AddWithValue("$path", backupPath);
                await attach.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (string table in SalvageTables)
            {
                try
                {
                    IReadOnlyList<string> destinationColumns =
                        await ReadTableColumnsAsync(connection, "main", table, cancellationToken).ConfigureAwait(false);
                    HashSet<string> sourceColumns = (await ReadTableColumnsAsync(
                            connection,
                            "damaged",
                            table,
                            cancellationToken)
                        .ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    string[] commonColumns = destinationColumns
                        .Where(sourceColumns.Contains)
                        .ToArray();
                    if (commonColumns.Length == 0)
                    {
                        throw new InvalidOperationException($"No readable columns were found for table '{table}'.");
                    }

                    string columnList = string.Join(", ", commonColumns.Select(QuoteIdentifier));
                    string projection = string.Join(", ", commonColumns.Select(column =>
                        BuildSalvageProjection(table, column)));
                    string predicate = BuildSalvagePredicate(table, sourceColumns);
                    await using var copy = connection.CreateCommand();
                    copy.CommandText =
                        $"INSERT OR IGNORE INTO main.{QuoteIdentifier(table)} ({columnList}) " +
                        $"SELECT {projection} FROM damaged.{QuoteIdentifier(table)} AS source{predicate};";
                    int rows = await copy.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    LogSalvagedTable(table, rows);
                }
                catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
                {
                    LogSalvageTableFailed(table, ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        {
            LogSalvageFailed(ex);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadTableColumnsAsync(
        SqliteConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {QuoteIdentifier(schema)}.table_info({QuoteIdentifier(table)});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var columns = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static string QuoteIdentifier(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string BuildSalvageProjection(string table, string column)
    {
        string source = $"source.{QuoteIdentifier(column)}";
        return (table, column) switch
        {
            ("pins", "capture_id") or ("context_items", "source_capture_id") =>
                $"CASE WHEN {source} IS NULL OR EXISTS (" +
                $"SELECT 1 FROM main.{QuoteIdentifier("captures")} AS parent " +
                $"WHERE parent.{QuoteIdentifier("id")} = {source}) " +
                $"THEN {source} ELSE NULL END",
            _ => source,
        };
    }

    private static string BuildSalvagePredicate(string table, HashSet<string> sourceColumns)
        => table switch
        {
            "actions" when sourceColumns.Contains("capture_id") =>
                $" WHERE EXISTS (SELECT 1 FROM main.{QuoteIdentifier("captures")} AS parent " +
                $"WHERE parent.{QuoteIdentifier("id")} = source.{QuoteIdentifier("capture_id")})",
            "context_items" when sourceColumns.Contains("package_id") =>
                $" WHERE EXISTS (SELECT 1 FROM main.{QuoteIdentifier("context_packages")} AS parent " +
                $"WHERE parent.{QuoteIdentifier("id")} = source.{QuoteIdentifier("package_id")})",
            "context_item_derivatives" when sourceColumns.Contains("item_id") =>
                $" WHERE EXISTS (SELECT 1 FROM main.{QuoteIdentifier("context_items")} AS parent " +
                $"WHERE parent.{QuoteIdentifier("id")} = source.{QuoteIdentifier("item_id")})",
            _ => string.Empty,
        };

    private static string? ResolveFilePath(SqliteConnection connection)
    {
        string dataSource = connection.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(dataSource);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsCorruption(SqliteException ex)
        => ex.SqliteErrorCode is 11 /* SQLITE_CORRUPT */ or 26 /* SQLITE_NOTADB */;

    private static async Task ApplyMigrationAsync(
        SqliteConnection connection,
        SchemaMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;

            // PRAGMA user_version does not accept a bound parameter, so the value is
            // formatted from a validated compile-time int (never user input).
            versionCommand.CommandText =
                "PRAGMA user_version = " + migration.Version.ToString(CultureInfo.InvariantCulture) + ";";
            await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> GetUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>Raised internally when the startup health check finds damage.</summary>
    private sealed class DatabaseCorruptException : Exception
    {
        public DatabaseCorruptException(string? databasePath, string detail)
            : base($"SQLite quick_check failed: {detail}")
        {
            DatabasePath = databasePath;
            Detail = detail;
        }

        public string? DatabasePath { get; }

        public string Detail { get; }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Database schema already at version {Version}; no migration needed.")]
    private partial void LogSchemaAlreadyCurrent(int version);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Migrating database schema from version {From} to {To}.")]
    private partial void LogMigratingSchema(int from, int to);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Applied schema migration {Version}.")]
    private partial void LogAppliedSchemaMigration(int version);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "The database is corrupted; attempting quarantine and rebuild.")]
    private partial void LogDatabaseCorrupt(Exception exception);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Database quick_check failed ({Detail}); attempting quarantine and rebuild.")]
    private partial void LogDatabaseCorruptCheck(string detail);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Could not quarantine the corrupted database file.")]
    private partial void LogQuarantineFailed(Exception exception);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information,
        Message = "Rebuilt a fresh database; the damaged file is kept at {BackupPath}.")]
    private partial void LogDatabaseRebuilt(string backupPath);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information,
        Message = "Salvaged {Table}: {Rows} rows recovered from the damaged database.")]
    private partial void LogSalvagedTable(string table, int rows);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Could not salvage table {Table}: {Reason}.")]
    private partial void LogSalvageTableFailed(string table, string reason);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Salvage from the damaged database failed.")]
    private partial void LogSalvageFailed(Exception exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "Opened database '{DatabasePath}' at schema version {Version}.")]
    private partial void LogOpenedDatabase(string databasePath, int version);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug,
        Message = "WAL checkpoint (TRUNCATE) completed.")]
    private partial void LogCheckpointed();

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "WAL checkpoint failed; data stays in the WAL until the next successful checkpoint.")]
    private partial void LogCheckpointFailed(Exception exception);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning,
        Message = "WAL checkpoint was blocked by concurrent readers (attempt {Attempt}, frames checkpointed: {Frames}).")]
    private partial void LogCheckpointBusy(int attempt, long frames);
}

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
/// actions, pins, clipboard clips) are salvaged best-effort — so the app never
/// stays broken behind a corrupt store.
/// </summary>
public sealed partial class OctadockDatabase : IOctadockDatabase, IDisposable
{
    private static readonly string[] SalvageTables =
        ["settings", "captures", "actions", "pins", "clipboard_clips"];

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
            await using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            LogCheckpointed();
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
    private async Task TrySalvageAsync(string backupPath, CancellationToken cancellationToken)
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
                    await using var copy = connection.CreateCommand();
                    copy.CommandText = $"INSERT OR IGNORE INTO main.{table} SELECT * FROM damaged.{table};";
                    int rows = await copy.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    LogSalvagedTable(table, rows);
                }
                catch (SqliteException ex)
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

    [LoggerMessage(EventId = 13, Level = LogLevel.Debug,
        Message = "WAL checkpoint failed; will retry on the next cycle.")]
    private partial void LogCheckpointFailed(Exception exception);
}

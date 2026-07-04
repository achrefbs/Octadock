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
/// </summary>
public sealed partial class OctadockDatabase : IOctadockDatabase, IDisposable
{
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
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            var currentVersion = await GetUserVersionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (currentVersion >= SchemaMigrations.LatestVersion)
            {
                LogSchemaAlreadyCurrent(currentVersion);
                return;
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
        }
        finally
        {
            _initializationGate.Release();
        }
    }

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

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Database schema already at version {Version}; no migration needed.")]
    private partial void LogSchemaAlreadyCurrent(int version);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Migrating database schema from version {From} to {To}.")]
    private partial void LogMigratingSchema(int from, int to);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Applied schema migration {Version}.")]
    private partial void LogAppliedSchemaMigration(int version);
}

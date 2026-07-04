using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Common;
using Octadock.Core.Persistence;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="ISettingsStore"/> over the <c>settings</c> key/value
/// table. Each write records an <c>updated_at</c> timestamp; multi-key writes are
/// applied atomically inside a single transaction.
/// </summary>
public sealed partial class SettingsStore : ISettingsStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IClock _clock;
    private readonly ILogger<SettingsStore> _logger;

    /// <summary>Creates the store over the given connection factory and clock.</summary>
    public SettingsStore(ISqliteConnectionFactory connectionFactory, IClock clock, ILogger<SettingsStore> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value_json FROM settings ORDER BY key ASC;";

        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results[reader.GetString(0)] = reader.GetString(1);
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value_json FROM settings WHERE key = $key;";
        SqliteValues.AddParameter(command, "$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        PrepareUpsert(command, key, value, _clock.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return;
        }

        var timestamp = _clock.UtcNow;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in values)
        {
            Guard.NotNullOrWhiteSpace(key, nameof(key));
            ArgumentNullException.ThrowIfNull(value);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            PrepareUpsert(command, key, value, timestamp);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogPersistedSettings(values.Count);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(key);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM settings WHERE key = $key;";
        SqliteValues.AddParameter(command, "$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void PrepareUpsert(SqliteCommand command, string key, string value, DateTimeOffset timestamp)
    {
        command.CommandText =
            """
            INSERT INTO settings (key, value_json, updated_at)
            VALUES ($key, $value_json, $updated_at)
            ON CONFLICT(key) DO UPDATE SET
                value_json = excluded.value_json,
                updated_at = excluded.updated_at;
            """;
        SqliteValues.AddParameter(command, "$key", key);
        SqliteValues.AddParameter(command, "$value_json", value);
        SqliteValues.AddParameter(command, "$updated_at", SqliteValues.ToStorage(timestamp));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Persisted {Count} settings in one transaction.")]
    private partial void LogPersistedSettings(int count);
}

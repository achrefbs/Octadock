using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IActionRepository"/> over the <c>actions</c> table.
/// Rows are removed automatically when their parent capture is hard-deleted via
/// the <c>ON DELETE CASCADE</c> foreign key (foreign keys are enabled per
/// connection by the connection factory).
/// </summary>
public sealed partial class ActionRepository : IActionRepository
{
    private const string Columns = "id, capture_id, action_type, created_at, destination, metadata_json";

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<ActionRepository> _logger;

    /// <summary>Creates the repository over the given connection factory.</summary>
    public ActionRepository(ISqliteConnectionFactory connectionFactory, ILogger<ActionRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AddAsync(ActionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO actions (id, capture_id, action_type, created_at, destination, metadata_json)
            VALUES ($id, $capture_id, $action_type, $created_at, $destination, $metadata_json);
            """;
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$capture_id", record.CaptureId.ToString());
        SqliteValues.AddParameter(command, "$action_type", record.ActionType.ToString());
        SqliteValues.AddParameter(command, "$created_at", SqliteValues.ToStorage(record.CreatedAt));
        SqliteValues.AddParameter(command, "$destination", record.Destination);
        SqliteValues.AddParameter(command, "$metadata_json", record.MetadataJson);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogRecordedAction(record.ActionType, record.CaptureId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActionRecord>> GetForCaptureAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM actions WHERE capture_id = $capture_id ORDER BY created_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$capture_id", captureId.ToString());

        var results = new List<ActionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    private static ActionRecord Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        CaptureId = Guid.Parse(SqliteValues.GetString(reader, 1)),
        ActionType = SqliteValues.GetEnum<ActionType>(reader, 2),
        CreatedAt = SqliteValues.GetTimestamp(reader, 3),
        Destination = SqliteValues.GetNullableString(reader, 4),
        MetadataJson = SqliteValues.GetNullableString(reader, 5),
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Recorded action {ActionType} on capture {CaptureId}.")]
    private partial void LogRecordedAction(ActionType actionType, Guid captureId);
}

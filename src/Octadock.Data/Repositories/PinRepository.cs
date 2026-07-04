using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IPinRepository"/> over the <c>pins</c> table. Uses an
/// <c>INSERT ... ON CONFLICT(id) DO UPDATE</c> upsert so a pin's persisted state
/// can be written the same way whether or not it already exists.
/// </summary>
public sealed partial class PinRepository : IPinRepository
{
    private const string Columns =
        "id, capture_id, image_path, x, y, width, height, opacity, click_through, monitor_id, last_visible_at";

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<PinRepository> _logger;

    /// <summary>Creates the repository over the given connection factory.</summary>
    public PinRepository(ISqliteConnectionFactory connectionFactory, ILogger<PinRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(PinRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO pins (id, capture_id, image_path, x, y, width, height, opacity, click_through, monitor_id, last_visible_at)
            VALUES ($id, $capture_id, $image_path, $x, $y, $width, $height, $opacity, $click_through, $monitor_id, $last_visible_at)
            ON CONFLICT(id) DO UPDATE SET
                capture_id = excluded.capture_id,
                image_path = excluded.image_path,
                x = excluded.x,
                y = excluded.y,
                width = excluded.width,
                height = excluded.height,
                opacity = excluded.opacity,
                click_through = excluded.click_through,
                monitor_id = excluded.monitor_id,
                last_visible_at = excluded.last_visible_at;
            """;
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$capture_id", record.CaptureId?.ToString());
        SqliteValues.AddParameter(command, "$image_path", record.ImagePath);
        SqliteValues.AddParameter(command, "$x", record.X);
        SqliteValues.AddParameter(command, "$y", record.Y);
        SqliteValues.AddParameter(command, "$width", record.Width);
        SqliteValues.AddParameter(command, "$height", record.Height);
        SqliteValues.AddParameter(command, "$opacity", record.Opacity);
        SqliteValues.AddParameter(command, "$click_through", record.ClickThrough ? 1 : 0);
        SqliteValues.AddParameter(command, "$monitor_id", record.MonitorId.Value);
        SqliteValues.AddTimestamp(command, "$last_visible_at", record.LastVisibleAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogUpsertedPin(record.Id);
    }

    /// <inheritdoc />
    public async Task<PinRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM pins WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PinRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM pins ORDER BY last_visible_at DESC, id ASC;";

        var results = new List<PinRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pins WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PinRecord Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        CaptureId = SqliteValues.GetNullableGuid(reader, 1),
        ImagePath = SqliteValues.GetNullableString(reader, 2),
        X = SqliteValues.GetInt32(reader, 3),
        Y = SqliteValues.GetInt32(reader, 4),
        Width = SqliteValues.GetInt32(reader, 5),
        Height = SqliteValues.GetInt32(reader, 6),
        Opacity = SqliteValues.GetDouble(reader, 7),
        ClickThrough = SqliteValues.GetBoolean(reader, 8),
        MonitorId = new MonitorId(SqliteValues.GetString(reader, 9)),
        LastVisibleAt = SqliteValues.GetNullableTimestamp(reader, 10),
    };

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Upserted pin {PinId}.")]
    private partial void LogUpsertedPin(Guid pinId);
}

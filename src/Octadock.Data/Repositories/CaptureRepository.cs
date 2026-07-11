using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="ICaptureRepository"/> over the <c>captures</c> table.
/// All user-supplied values are bound as parameters; only compile-time-safe
/// fragments (sort direction, generated <c>IN</c> placeholders) are concatenated.
/// </summary>
public sealed partial class CaptureRepository : ICaptureRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<CaptureRepository> _logger;

    /// <summary>Creates the repository over the given connection factory.</summary>
    public CaptureRepository(ISqliteConnectionFactory connectionFactory, ILogger<CaptureRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AddAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO captures (
                id, type, created_at, source_process, source_window, hwnd_hash, monitor_id,
                pixel_width, pixel_height, dpi_scale, original_path, thumbnail_path, project_path,
                duration_ms, deleted_at, approved_mockup_path)
            VALUES (
                $id, $type, $created_at, $source_process, $source_window, $hwnd_hash, $monitor_id,
                $pixel_width, $pixel_height, $dpi_scale, $original_path, $thumbnail_path, $project_path,
                $duration_ms, $deleted_at, $approved_mockup_path);
            """;
        CaptureMapper.BindParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogInsertedCapture(record.Id);
    }

    /// <inheritdoc />
    public async Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {CaptureMapper.Columns} FROM captures WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return CaptureMapper.Map(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CaptureRecord>> QueryAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder($"SELECT {CaptureMapper.Columns} FROM captures");
        AppendWhereClause(command, sql, filter);

        sql.Append(filter.SortOrder == CaptureSortOrder.OldestFirst
            ? " ORDER BY created_at ASC, id ASC"
            : " ORDER BY created_at DESC, id DESC");

        sql.Append(" LIMIT $limit OFFSET $offset;");
        SqliteValues.AddParameter(command, "$limit", NormalizeLimit(filter.Limit));
        SqliteValues.AddParameter(command, "$offset", Math.Max(0, filter.Offset));

        command.CommandText = sql.ToString();
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder("SELECT COUNT(*) FROM captures");
        AppendWhereClause(command, sql, filter);
        sql.Append(';');
        command.CommandText = sql.ToString();

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {CaptureMapper.Columns} FROM captures " +
            "WHERE deleted_at IS NULL ORDER BY created_at DESC, id DESC LIMIT $limit;";
        SqliteValues.AddParameter(command, "$limit", NormalizeLimit(count));
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE captures SET
                type = $type,
                created_at = $created_at,
                source_process = $source_process,
                source_window = $source_window,
                hwnd_hash = $hwnd_hash,
                monitor_id = $monitor_id,
                pixel_width = $pixel_width,
                pixel_height = $pixel_height,
                dpi_scale = $dpi_scale,
                original_path = $original_path,
                thumbnail_path = $thumbnail_path,
                project_path = $project_path,
                duration_ms = $duration_ms,
                deleted_at = $deleted_at,
                approved_mockup_path = $approved_mockup_path
            WHERE id = $id;
            """;
        CaptureMapper.BindParameters(command, record);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            LogUpdateMissingCapture(record.Id);
        }
    }

    /// <inheritdoc />
    public async Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE captures SET deleted_at = $deleted_at WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        SqliteValues.AddParameter(command, "$deleted_at", SqliteValues.ToStorage(deletedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE captures SET deleted_at = NULL WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM captures WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CaptureRecord>> GetOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {CaptureMapper.Columns} FROM captures " +
            "WHERE deleted_at IS NULL AND created_at < $cutoff ORDER BY created_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$cutoff", SqliteValues.ToStorage(cutoff));
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CaptureRecord>> GetSoftDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {CaptureMapper.Columns} FROM captures " +
            "WHERE deleted_at IS NOT NULL AND deleted_at <= $cutoff ORDER BY deleted_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$cutoff", SqliteValues.ToStorage(cutoff));
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends a parameterised <c>WHERE</c> clause translating <paramref name="filter"/>.
    /// Placeholder names are generated (never sourced from user input); every value is bound.
    /// </summary>
    private static void AppendWhereClause(SqliteCommand command, StringBuilder sql, CaptureFilter filter)
    {
        var conditions = new List<string>();

        if (!filter.IncludeDeleted)
        {
            conditions.Add("deleted_at IS NULL");
        }

        if (filter.Types is { Count: > 0 } types)
        {
            var placeholders = new List<string>(types.Count);
            var index = 0;
            foreach (var type in types)
            {
                var name = $"$type{index++}";
                placeholders.Add(name);
                SqliteValues.AddParameter(command, name, type.ToString());
            }

            conditions.Add($"type IN ({string.Join(", ", placeholders)})");
        }

        if (filter.CreatedAfter is { } after)
        {
            conditions.Add("created_at >= $created_after");
            SqliteValues.AddParameter(command, "$created_after", SqliteValues.ToStorage(after));
        }

        if (filter.CreatedBefore is { } before)
        {
            conditions.Add("created_at <= $created_before");
            SqliteValues.AddParameter(command, "$created_before", SqliteValues.ToStorage(before));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            // ESCAPE '\' so literal % and _ in the search text are matched, not treated as wildcards.
            conditions.Add(
                "(source_process LIKE $search ESCAPE '\\' OR source_window LIKE $search ESCAPE '\\')");
            SqliteValues.AddParameter(command, "$search", $"%{EscapeLike(filter.SearchText.Trim())}%");
        }

        if (filter.HasProject is { } hasProject)
        {
            conditions.Add(hasProject ? "project_path IS NOT NULL" : "project_path IS NULL");
        }

        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }
    }

    /// <summary>Escapes LIKE wildcards (<c>%</c>, <c>_</c>) and the escape char itself.</summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static int NormalizeLimit(int limit) => limit < 0 ? -1 : limit;

    private static async Task<IReadOnlyList<CaptureRecord>> ReadListAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<CaptureRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(CaptureMapper.Map(reader));
        }

        return results;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Inserted capture {CaptureId}.")]
    private partial void LogInsertedCapture(Guid captureId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Update affected no rows; capture {CaptureId} does not exist.")]
    private partial void LogUpdateMissingCapture(Guid captureId);
}

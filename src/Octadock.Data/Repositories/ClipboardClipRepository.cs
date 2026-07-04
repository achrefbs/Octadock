using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IClipboardClipRepository"/> over the
/// <c>clipboard_clips</c> table. The repository stores metadata and text inline;
/// image payload files are managed by higher layers and referenced by path.
/// </summary>
public sealed partial class ClipboardClipRepository : IClipboardClipRepository
{
    private const string Columns =
        "id, kind, created_at, last_seen_at, seen_count, source_process, source_window, format_name, " +
        "text, image_path, thumbnail_path, content_hash, size_bytes, is_favorite, deleted_at, metadata_json";

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<ClipboardClipRepository> _logger;

    /// <summary>Creates the repository over the given connection factory.</summary>
    public ClipboardClipRepository(
        ISqliteConnectionFactory connectionFactory,
        ILogger<ClipboardClipRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AddAsync(ClipboardClipRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO clipboard_clips (
                id, kind, created_at, last_seen_at, seen_count, source_process, source_window, format_name,
                text, image_path, thumbnail_path, content_hash, size_bytes, is_favorite, deleted_at, metadata_json)
            VALUES (
                $id, $kind, $created_at, $last_seen_at, $seen_count, $source_process, $source_window, $format_name,
                $text, $image_path, $thumbnail_path, $content_hash, $size_bytes, $is_favorite, $deleted_at, $metadata_json);
            """;
        BindParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogInsertedClip(record.Id, record.Kind);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ClipboardClipRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE clipboard_clips SET
                kind = $kind,
                created_at = $created_at,
                last_seen_at = $last_seen_at,
                seen_count = $seen_count,
                source_process = $source_process,
                source_window = $source_window,
                format_name = $format_name,
                text = $text,
                image_path = $image_path,
                thumbnail_path = $thumbnail_path,
                content_hash = $content_hash,
                size_bytes = $size_bytes,
                is_favorite = $is_favorite,
                deleted_at = $deleted_at,
                metadata_json = $metadata_json
            WHERE id = $id;
            """;
        BindParameters(command, record);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            LogUpdateMissingClip(record.Id);
        }
    }

    /// <inheritdoc />
    public async Task<ClipboardClipRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM clipboard_clips WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClipboardClipRecord>> QueryAsync(
        ClipboardClipFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder($"SELECT {Columns} FROM clipboard_clips");
        AppendWhereClause(command, sql, filter);
        sql.Append(filter.SortOrder switch
        {
            ClipboardClipSortOrder.CreatedNewestFirst => " ORDER BY created_at DESC, id DESC",
            ClipboardClipSortOrder.OldestFirst => " ORDER BY created_at ASC, id ASC",
            _ => " ORDER BY last_seen_at DESC, created_at DESC, id DESC",
        });
        sql.Append(" LIMIT $limit OFFSET $offset;");

        command.CommandText = sql.ToString();
        SqliteValues.AddParameter(command, "$limit", NormalizeLimit(filter.Limit));
        SqliteValues.AddParameter(command, "$offset", Math.Max(0, filter.Offset));

        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(ClipboardClipFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder("SELECT COUNT(*) FROM clipboard_clips");
        AppendWhereClause(command, sql, filter);
        sql.Append(';');
        command.CommandText = sql.ToString();

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null ? 0 : Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClipboardClipRecord>> GetRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM clipboard_clips " +
            "WHERE deleted_at IS NULL ORDER BY last_seen_at DESC, created_at DESC, id DESC LIMIT $limit;";
        SqliteValues.AddParameter(command, "$limit", NormalizeLimit(count));
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ClipboardClipRecord?> GetLatestByContentHashAsync(
        ClipboardClipKind kind,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM clipboard_clips " +
            "WHERE kind = $kind AND content_hash = $content_hash AND deleted_at IS NULL " +
            "ORDER BY last_seen_at DESC, created_at DESC, id DESC LIMIT 1;";
        SqliteValues.AddParameter(command, "$kind", kind.ToString());
        SqliteValues.AddParameter(command, "$content_hash", contentHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return Map(reader);
    }

    /// <inheritdoc />
    public async Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_clips SET deleted_at = $deleted_at WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        SqliteValues.AddParameter(command, "$deleted_at", SqliteValues.ToStorage(deletedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_clips SET deleted_at = NULL WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_clips WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClipboardClipRecord>> GetOlderThanAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM clipboard_clips " +
            "WHERE deleted_at IS NULL AND last_seen_at < $cutoff ORDER BY last_seen_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$cutoff", SqliteValues.ToStorage(cutoff));
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClipboardClipRecord>> GetSoftDeletedBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {Columns} FROM clipboard_clips " +
            "WHERE deleted_at IS NOT NULL AND deleted_at <= $cutoff ORDER BY deleted_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$cutoff", SqliteValues.ToStorage(cutoff));
        return await ReadListAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static void AppendWhereClause(SqliteCommand command, StringBuilder sql, ClipboardClipFilter filter)
    {
        var conditions = new List<string>();

        if (!filter.IncludeDeleted)
        {
            conditions.Add("deleted_at IS NULL");
        }

        if (filter.Kinds is { Count: > 0 } kinds)
        {
            var placeholders = new List<string>(kinds.Count);
            var index = 0;
            foreach (var kind in kinds)
            {
                var name = $"$kind{index++}";
                placeholders.Add(name);
                SqliteValues.AddParameter(command, name, kind.ToString());
            }

            conditions.Add($"kind IN ({string.Join(", ", placeholders)})");
        }

        if (filter.CreatedAfter is { } createdAfter)
        {
            conditions.Add("created_at >= $created_after");
            SqliteValues.AddParameter(command, "$created_after", SqliteValues.ToStorage(createdAfter));
        }

        if (filter.CreatedBefore is { } createdBefore)
        {
            conditions.Add("created_at <= $created_before");
            SqliteValues.AddParameter(command, "$created_before", SqliteValues.ToStorage(createdBefore));
        }

        if (filter.LastSeenAfter is { } lastSeenAfter)
        {
            conditions.Add("last_seen_at >= $last_seen_after");
            SqliteValues.AddParameter(command, "$last_seen_after", SqliteValues.ToStorage(lastSeenAfter));
        }

        if (filter.LastSeenBefore is { } lastSeenBefore)
        {
            conditions.Add("last_seen_at <= $last_seen_before");
            SqliteValues.AddParameter(command, "$last_seen_before", SqliteValues.ToStorage(lastSeenBefore));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            conditions.Add(
                "(text LIKE $search ESCAPE '\\' OR source_process LIKE $search ESCAPE '\\' OR " +
                "source_window LIKE $search ESCAPE '\\' OR format_name LIKE $search ESCAPE '\\')");
            SqliteValues.AddParameter(command, "$search", $"%{EscapeLike(filter.SearchText.Trim())}%");
        }

        if (filter.IsFavorite is { } isFavorite)
        {
            conditions.Add(isFavorite ? "is_favorite = 1" : "is_favorite = 0");
        }

        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private static int NormalizeLimit(int limit) => limit < 0 ? -1 : limit;

    private static void BindParameters(SqliteCommand command, ClipboardClipRecord record)
    {
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$kind", record.Kind.ToString());
        SqliteValues.AddParameter(command, "$created_at", SqliteValues.ToStorage(record.CreatedAt));
        SqliteValues.AddParameter(command, "$last_seen_at", SqliteValues.ToStorage(record.LastSeenAt));
        SqliteValues.AddParameter(command, "$seen_count", record.SeenCount);
        SqliteValues.AddParameter(command, "$source_process", record.SourceProcess);
        SqliteValues.AddParameter(command, "$source_window", record.SourceWindow);
        SqliteValues.AddParameter(command, "$format_name", record.FormatName);
        SqliteValues.AddParameter(command, "$text", record.Text);
        SqliteValues.AddParameter(command, "$image_path", record.ImagePath);
        SqliteValues.AddParameter(command, "$thumbnail_path", record.ThumbnailPath);
        SqliteValues.AddParameter(command, "$content_hash", record.ContentHash);
        SqliteValues.AddParameter(command, "$size_bytes", record.SizeBytes);
        SqliteValues.AddParameter(command, "$is_favorite", record.IsFavorite ? 1 : 0);
        SqliteValues.AddTimestamp(command, "$deleted_at", record.DeletedAt);
        SqliteValues.AddParameter(command, "$metadata_json", record.MetadataJson);
    }

    private static ClipboardClipRecord Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        Kind = SqliteValues.GetEnum<ClipboardClipKind>(reader, 1),
        CreatedAt = SqliteValues.GetTimestamp(reader, 2),
        LastSeenAt = SqliteValues.GetTimestamp(reader, 3),
        SeenCount = SqliteValues.GetInt32(reader, 4),
        SourceProcess = SqliteValues.GetNullableString(reader, 5),
        SourceWindow = SqliteValues.GetNullableString(reader, 6),
        FormatName = SqliteValues.GetNullableString(reader, 7),
        Text = SqliteValues.GetNullableString(reader, 8),
        ImagePath = SqliteValues.GetNullableString(reader, 9),
        ThumbnailPath = SqliteValues.GetNullableString(reader, 10),
        ContentHash = SqliteValues.GetNullableString(reader, 11),
        SizeBytes = SqliteValues.GetNullableInt64(reader, 12),
        IsFavorite = SqliteValues.GetBoolean(reader, 13),
        DeletedAt = SqliteValues.GetNullableTimestamp(reader, 14),
        MetadataJson = SqliteValues.GetNullableString(reader, 15),
    };

    private static async Task<IReadOnlyList<ClipboardClipRecord>> ReadListAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var results = new List<ClipboardClipRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Inserted clipboard clip {ClipId} of kind {Kind}.")]
    private partial void LogInsertedClip(Guid clipId, ClipboardClipKind kind);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Update affected no rows; clipboard clip {ClipId} does not exist.")]
    private partial void LogUpdateMissingClip(Guid clipId);
}

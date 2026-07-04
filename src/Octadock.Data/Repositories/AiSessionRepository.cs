using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IAiSessionRepository"/> for provider-neutral AI/tool
/// runs, timeline events, and linked artifacts.
/// </summary>
public sealed partial class AiSessionRepository : IAiSessionRepository
{
    private const string SessionColumns =
        "id, provider, title, cwd, git_branch, command, pid, status, started_at, ended_at, exit_code, last_event_at, notification_mode, metadata_json";

    private const string EventColumns = "id, session_id, event_type, created_at, message, metadata_json";

    private const string ArtifactColumns =
        "id, session_id, artifact_type, created_at, capture_id, external_id, path, uri, title, metadata_json";

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<AiSessionRepository> _logger;

    /// <summary>Creates the repository over the given connection factory.</summary>
    public AiSessionRepository(ISqliteConnectionFactory connectionFactory, ILogger<AiSessionRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AddAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ai_sessions (
                id, provider, title, cwd, git_branch, command, pid, status, started_at, ended_at,
                exit_code, last_event_at, notification_mode, metadata_json)
            VALUES (
                $id, $provider, $title, $cwd, $git_branch, $command, $pid, $status, $started_at, $ended_at,
                $exit_code, $last_event_at, $notification_mode, $metadata_json);
            """;
        BindSession(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogInsertedSession(record.Id, record.Provider);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ai_sessions SET
                provider = $provider,
                title = $title,
                cwd = $cwd,
                git_branch = $git_branch,
                command = $command,
                pid = $pid,
                status = $status,
                started_at = $started_at,
                ended_at = $ended_at,
                exit_code = $exit_code,
                last_event_at = $last_event_at,
                notification_mode = $notification_mode,
                metadata_json = $metadata_json
            WHERE id = $id;
            """;
        BindSession(command, record);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected == 0)
        {
            LogUpdateMissingSession(record.Id);
        }
    }

    /// <inheritdoc />
    public async Task<AiSessionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SessionColumns} FROM ai_sessions WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return MapSession(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiSessionRecord>> ListAsync(
        AiSessionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder($"SELECT {SessionColumns} FROM ai_sessions");
        AppendWhereClause(command, sql, filter);
        sql.Append(filter.SortOrder == AiSessionSortOrder.OldestStartedFirst
            ? " ORDER BY started_at ASC, id ASC"
            : " ORDER BY COALESCE(last_event_at, started_at) DESC, started_at DESC, id DESC");
        sql.Append(" LIMIT $limit OFFSET $offset;");

        command.CommandText = sql.ToString();
        SqliteValues.AddParameter(command, "$limit", NormalizeLimit(filter.Limit));
        SqliteValues.AddParameter(command, "$offset", Math.Max(0, filter.Offset));

        var results = new List<AiSessionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapSession(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task AddEventAsync(AiSessionEventRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO ai_session_events (id, session_id, event_type, created_at, message, metadata_json)
                VALUES ($id, $session_id, $event_type, $created_at, $message, $metadata_json);
                """;
            BindEvent(command, record);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await TouchLastEventAsync(connection, transaction, record.SessionId, record.CreatedAt, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogInsertedEvent(record.EventType, record.SessionId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiSessionEventRecord>> GetEventsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {EventColumns} FROM ai_session_events WHERE session_id = $session_id ORDER BY created_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$session_id", sessionId.ToString());

        var results = new List<AiSessionEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapEvent(reader));
        }

        return results;
    }

    /// <inheritdoc />
    public async Task AddArtifactAsync(AiSessionArtifactRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO ai_session_artifacts (
                    id, session_id, artifact_type, created_at, capture_id, external_id, path, uri, title, metadata_json)
                VALUES (
                    $id, $session_id, $artifact_type, $created_at, $capture_id, $external_id, $path, $uri, $title, $metadata_json);
                """;
            BindArtifact(command, record);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await TouchLastEventAsync(connection, transaction, record.SessionId, record.CreatedAt, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogInsertedArtifact(record.ArtifactKind, record.SessionId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiSessionArtifactRecord>> GetArtifactsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {ArtifactColumns} FROM ai_session_artifacts WHERE session_id = $session_id ORDER BY created_at ASC, id ASC;";
        SqliteValues.AddParameter(command, "$session_id", sessionId.ToString());

        var results = new List<AiSessionArtifactRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapArtifact(reader));
        }

        return results;
    }

    private static void AppendWhereClause(SqliteCommand command, StringBuilder sql, AiSessionFilter filter)
    {
        var conditions = new List<string>();

        if (filter.Providers is { Count: > 0 } providers)
        {
            var placeholders = new List<string>(providers.Count);
            var index = 0;
            foreach (var provider in providers)
            {
                var name = $"$provider{index++}";
                placeholders.Add(name);
                SqliteValues.AddParameter(command, name, provider.ToString());
            }

            conditions.Add($"provider IN ({string.Join(", ", placeholders)})");
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            var placeholders = new List<string>(statuses.Count);
            var index = 0;
            foreach (var status in statuses)
            {
                var name = $"$status{index++}";
                placeholders.Add(name);
                SqliteValues.AddParameter(command, name, status.ToString());
            }

            conditions.Add($"status IN ({string.Join(", ", placeholders)})");
        }

        if (filter.StartedAfter is { } after)
        {
            conditions.Add("started_at >= $started_after");
            SqliteValues.AddParameter(command, "$started_after", SqliteValues.ToStorage(after));
        }

        if (filter.StartedBefore is { } before)
        {
            conditions.Add("started_at <= $started_before");
            SqliteValues.AddParameter(command, "$started_before", SqliteValues.ToStorage(before));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchText))
        {
            conditions.Add(
                "(title LIKE $search ESCAPE '\\' OR cwd LIKE $search ESCAPE '\\' OR git_branch LIKE $search ESCAPE '\\' OR command LIKE $search ESCAPE '\\')");
            SqliteValues.AddParameter(command, "$search", $"%{EscapeLike(filter.SearchText.Trim())}%");
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

    private static void BindSession(SqliteCommand command, AiSessionRecord record)
    {
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$provider", record.Provider.ToString());
        SqliteValues.AddParameter(command, "$title", record.Title);
        SqliteValues.AddParameter(command, "$cwd", record.Cwd);
        SqliteValues.AddParameter(command, "$git_branch", record.GitBranch);
        SqliteValues.AddParameter(command, "$command", record.Command);
        SqliteValues.AddParameter(command, "$pid", record.Pid);
        SqliteValues.AddParameter(command, "$status", record.Status.ToString());
        SqliteValues.AddParameter(command, "$started_at", SqliteValues.ToStorage(record.StartedAt));
        SqliteValues.AddTimestamp(command, "$ended_at", record.EndedAt);
        SqliteValues.AddParameter(command, "$exit_code", record.ExitCode);
        SqliteValues.AddTimestamp(command, "$last_event_at", record.LastEventAt);
        SqliteValues.AddParameter(command, "$notification_mode", record.NotificationMode.ToString());
        SqliteValues.AddParameter(command, "$metadata_json", record.MetadataJson);
    }

    private static void BindEvent(SqliteCommand command, AiSessionEventRecord record)
    {
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$session_id", record.SessionId.ToString());
        SqliteValues.AddParameter(command, "$event_type", record.EventType.ToString());
        SqliteValues.AddParameter(command, "$created_at", SqliteValues.ToStorage(record.CreatedAt));
        SqliteValues.AddParameter(command, "$message", record.Message);
        SqliteValues.AddParameter(command, "$metadata_json", record.MetadataJson);
    }

    private static void BindArtifact(SqliteCommand command, AiSessionArtifactRecord record)
    {
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$session_id", record.SessionId.ToString());
        SqliteValues.AddParameter(command, "$artifact_type", record.ArtifactKind.ToString());
        SqliteValues.AddParameter(command, "$created_at", SqliteValues.ToStorage(record.CreatedAt));
        SqliteValues.AddParameter(command, "$capture_id", record.CaptureId?.ToString());
        SqliteValues.AddParameter(command, "$external_id", record.ExternalId);
        SqliteValues.AddParameter(command, "$path", record.Path);
        SqliteValues.AddParameter(command, "$uri", record.Uri);
        SqliteValues.AddParameter(command, "$title", record.Title);
        SqliteValues.AddParameter(command, "$metadata_json", record.MetadataJson);
    }

    private static AiSessionRecord MapSession(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        Provider = SqliteValues.GetEnum<AiSessionProvider>(reader, 1),
        Title = SqliteValues.GetString(reader, 2),
        Cwd = SqliteValues.GetNullableString(reader, 3),
        GitBranch = SqliteValues.GetNullableString(reader, 4),
        Command = SqliteValues.GetNullableString(reader, 5),
        Pid = GetNullableInt32(reader, 6),
        Status = SqliteValues.GetEnum<AiSessionStatus>(reader, 7),
        StartedAt = SqliteValues.GetTimestamp(reader, 8),
        EndedAt = SqliteValues.GetNullableTimestamp(reader, 9),
        ExitCode = GetNullableInt32(reader, 10),
        LastEventAt = SqliteValues.GetNullableTimestamp(reader, 11),
        NotificationMode = SqliteValues.GetEnum<AiSessionNotificationMode>(reader, 12),
        MetadataJson = SqliteValues.GetNullableString(reader, 13),
    };

    private static AiSessionEventRecord MapEvent(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        SessionId = Guid.Parse(SqliteValues.GetString(reader, 1)),
        EventType = SqliteValues.GetEnum<AiSessionEventType>(reader, 2),
        CreatedAt = SqliteValues.GetTimestamp(reader, 3),
        Message = SqliteValues.GetNullableString(reader, 4),
        MetadataJson = SqliteValues.GetNullableString(reader, 5),
    };

    private static AiSessionArtifactRecord MapArtifact(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        SessionId = Guid.Parse(SqliteValues.GetString(reader, 1)),
        ArtifactKind = SqliteValues.GetEnum<AiSessionArtifactKind>(reader, 2),
        CreatedAt = SqliteValues.GetTimestamp(reader, 3),
        CaptureId = SqliteValues.GetNullableGuid(reader, 4),
        ExternalId = SqliteValues.GetNullableString(reader, 5),
        Path = SqliteValues.GetNullableString(reader, 6),
        Uri = SqliteValues.GetNullableString(reader, 7),
        Title = SqliteValues.GetNullableString(reader, 8),
        MetadataJson = SqliteValues.GetNullableString(reader, 9),
    };

    private static int? GetNullableInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static async Task TouchLastEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        DateTimeOffset eventTime,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE ai_sessions
            SET last_event_at =
                CASE
                    WHEN last_event_at IS NULL OR last_event_at < $event_time THEN $event_time
                    ELSE last_event_at
                END
            WHERE id = $session_id;
            """;
        SqliteValues.AddParameter(command, "$session_id", sessionId.ToString());
        SqliteValues.AddParameter(command, "$event_time", SqliteValues.ToStorage(eventTime));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Inserted AI session {SessionId} from {Provider}.")]
    private partial void LogInsertedSession(Guid sessionId, AiSessionProvider provider);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Update affected no rows; AI session {SessionId} does not exist.")]
    private partial void LogUpdateMissingSession(Guid sessionId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Inserted AI session event {EventType} for {SessionId}.")]
    private partial void LogInsertedEvent(AiSessionEventType eventType, Guid sessionId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug,
        Message = "Inserted AI session artifact {ArtifactKind} for {SessionId}.")]
    private partial void LogInsertedArtifact(AiSessionArtifactKind artifactKind, Guid sessionId);
}

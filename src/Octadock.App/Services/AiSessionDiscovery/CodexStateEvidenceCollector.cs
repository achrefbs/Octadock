using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Octadock.Core.Models;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>Best-effort state inferred from Codex desktop rollout logs.</summary>
public enum AiSessionCodexThreadRunState
{
    Unknown,
    Active,
    Completed,
}

/// <summary>Run state plus the timestamp of the rollout line that proved it.</summary>
public sealed record AiSessionCodexThreadRunSnapshot(
    AiSessionCodexThreadRunState State,
    DateTimeOffset? ObservedAt)
{
    public static AiSessionCodexThreadRunSnapshot Unknown { get; } =
        new(AiSessionCodexThreadRunState.Unknown, null);
}

/// <summary>Minimal Codex desktop thread metadata used by state-db discovery.</summary>
public sealed record AiSessionCodexThreadSnapshot(
    string ThreadId,
    string? RawTitle,
    string? Cwd,
    long CreatedAtUnixSeconds,
    long UpdatedAtUnixSeconds,
    bool Archived,
    AiSessionCodexThreadRunState RunState,
    DateTimeOffset? RunStateObservedAt,
    string? SpawnStatus,
    string? ParentThreadId,
    long? ParentUpdatedAtUnixSeconds,
    string? Source,
    string? Model,
    string? ModelProvider,
    string? StateDatabasePath);

/// <summary>
/// Reads Codex desktop/CLI state: the threads table in ~/.codex/state_*.sqlite
/// plus the tail of each thread's rollout log. Emits Running evidence for
/// threads with fresh rollout activity and Completed evidence for recent
/// threads whose rollout finished or went quiet, so the coordinator can close
/// their rows (and any merged runtime-worker rows) deterministically.
/// </summary>
public sealed class CodexStateEvidenceCollector : IAiSessionEvidenceCollector
{
    public const string SourceId = "codex-state";

    /// <summary>Threads updated within this window are considered at all.</summary>
    public static readonly TimeSpan ThreadRecencyWindow = TimeSpan.FromMinutes(20);

    /// <summary>Rollout activity older than this means the run is no longer live.</summary>
    public static readonly TimeSpan ActiveRolloutWindow = TimeSpan.FromSeconds(45);

    private const int MaxThreads = 30;

    private readonly Func<string?> _databasePathResolver;

    public CodexStateEvidenceCollector(Func<string?>? databasePathResolver = null)
    {
        _databasePathResolver = databasePathResolver ?? FindLatestCodexStateDatabase;
    }

    public string Source => SourceId;

    public Task<AiSessionEvidenceBatch> CollectAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                string? databasePath = _databasePathResolver();
                if (string.IsNullOrWhiteSpace(databasePath))
                {
                    // No ~/.codex at all means Codex is absent: that is an
                    // authoritative "no sessions", so rows may complete. A
                    // present directory without a readable db is a failure,
                    // which blocks completions instead of faking emptiness.
                    return CodexDirectoryExists()
                        ? AiSessionEvidenceBatch.Failed(SourceId, "Codex state database not found or unreadable.")
                        : new AiSessionEvidenceBatch(SourceId, Succeeded: true, []);
                }

                try
                {
                    return new AiSessionEvidenceBatch(
                        SourceId,
                        Succeeded: true,
                        ReadThreadEvidence(databasePath, observedAt));
                }
                catch (Exception ex) when (ex is SqliteException
                    or IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException)
                {
                    return AiSessionEvidenceBatch.Failed(SourceId, $"Codex state read failed: {ex.Message}");
                }
            },
            cancellationToken);

    /// <summary>Maps one Codex thread row to evidence, or null when it should be invisible.</summary>
    public static AiSessionEvidence? ClassifyThread(
        AiSessionCodexThreadSnapshot snapshot,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? threadId = AiSessionTextSanitizer.Clean(snapshot.ThreadId);
        if (threadId is null || snapshot.Archived)
        {
            return null;
        }

        if (string.Equals(snapshot.SpawnStatus, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (snapshot.RunState == AiSessionCodexThreadRunState.Unknown)
        {
            return null;
        }

        DateTimeOffset updatedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, snapshot.UpdatedAtUnixSeconds));
        DateTimeOffset cutoff = observedAt.Subtract(ThreadRecencyWindow);
        bool isOpenSubagent = string.Equals(snapshot.SpawnStatus, "open", StringComparison.OrdinalIgnoreCase);
        bool parentIsRecent = snapshot.ParentUpdatedAtUnixSeconds is long parentUpdated &&
            DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, parentUpdated)) >= cutoff;
        if (updatedAt < cutoff && (!isOpenSubagent || !parentIsRecent))
        {
            return null;
        }

        bool rolloutIsLive =
            snapshot.RunState == AiSessionCodexThreadRunState.Active &&
            snapshot.RunStateObservedAt is { } liveAt &&
            liveAt >= observedAt.Subtract(ActiveRolloutWindow);

        string? workspace = AiSessionTextSanitizer.NormalizePath(snapshot.Cwd);
        DateTimeOffset startedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, snapshot.CreatedAtUnixSeconds));
        DateTimeOffset lastActivity = Max(updatedAt, snapshot.RunStateObservedAt);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["codexThreadId"] = threadId,
            ["codexThreadUpdatedAt"] = updatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            ["codexThreadRunState"] = snapshot.RunState.ToString(),
        };
        // Thread titles are auto-generated from the user's first prompt, so
        // they are conversation content: redact before persisting.
        AddIfPresent(metadata, "codexThreadTitle", AiSessionTextSanitizer.RedactSecrets(snapshot.RawTitle));
        AddIfPresent(metadata, "spawnStatus", snapshot.SpawnStatus);
        AddIfPresent(metadata, "parentThreadId", snapshot.ParentThreadId);
        AddIfPresent(metadata, "model", snapshot.Model);
        AddIfPresent(metadata, "modelProvider", snapshot.ModelProvider);

        (AiSessionStatus statusHint, double confidence, string reason) = rolloutIsLive
            ? (AiSessionStatus.Running, AiSessionConfidence.Certain,
                "Codex thread with fresh rollout activity.")
            : snapshot.RunState == AiSessionCodexThreadRunState.Completed
                ? (AiSessionStatus.Completed, AiSessionConfidence.Certain,
                    "Codex thread rollout recorded task completion.")
                : (AiSessionStatus.Completed, AiSessionConfidence.Strong,
                    "Codex thread rollout activity went quiet.");

        return new AiSessionEvidence
        {
            Source = SourceId,
            Detector = "codex-state-thread",
            Provider = AiSessionProvider.Codex,
            Confidence = confidence,
            Reason = reason,
            Title = BuildThreadTitle(snapshot, workspace),
            Command = $"Codex thread {threadId}",
            WorkspacePath = workspace,
            ProviderSessionId = threadId,
            StateFilePath = snapshot.StateDatabasePath,
            StartedAt = startedAt,
            LastActivityAt = lastActivity,
            StatusHint = statusHint,
            Metadata = metadata,
        };
    }

    private static IReadOnlyList<AiSessionEvidence> ReadThreadEvidence(
        string databasePath,
        DateTimeOffset observedAt)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                t.id,
                t.title,
                t.cwd,
                t.created_at,
                t.updated_at,
                t.archived,
                t.rollout_path,
                t.source,
                t.model,
                t.model_provider,
                e.status AS spawn_status,
                e.parent_thread_id,
                p.updated_at AS parent_updated_at
            FROM threads t
            LEFT JOIN thread_spawn_edges e ON e.child_thread_id = t.id
            LEFT JOIN threads p ON p.id = e.parent_thread_id
            WHERE t.archived = 0
              AND COALESCE(e.status, '') <> 'closed'
              AND (
                  t.updated_at >= $cutoff
                  OR (
                      COALESCE(e.status, '') = 'open'
                      AND COALESCE(p.updated_at, 0) >= $cutoff
                  )
              )
            ORDER BY t.updated_at DESC, t.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$cutoff",
            observedAt.Subtract(ThreadRecencyWindow).ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$limit", MaxThreads);

        var evidence = new List<AiSessionEvidence>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            AiSessionCodexThreadRunSnapshot runState = ReadThreadRunState(
                ReadSqliteString(reader, "rollout_path"));
            var snapshot = new AiSessionCodexThreadSnapshot(
                ReadSqliteString(reader, "id") ?? string.Empty,
                ReadSqliteString(reader, "title"),
                ReadSqliteString(reader, "cwd"),
                ReadSqliteInt64(reader, "created_at") ?? observedAt.ToUnixTimeSeconds(),
                ReadSqliteInt64(reader, "updated_at") ?? observedAt.ToUnixTimeSeconds(),
                ReadSqliteInt64(reader, "archived") is > 0,
                runState.State,
                runState.ObservedAt,
                ReadSqliteString(reader, "spawn_status"),
                ReadSqliteString(reader, "parent_thread_id"),
                ReadSqliteInt64(reader, "parent_updated_at"),
                ReadSqliteString(reader, "source"),
                ReadSqliteString(reader, "model"),
                ReadSqliteString(reader, "model_provider"),
                databasePath);

            AiSessionEvidence? item = ClassifyThread(snapshot, observedAt);
            if (item is not null)
            {
                evidence.Add(item);
            }
        }

        return evidence;
    }

    private static bool CodexDirectoryExists()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrWhiteSpace(userProfile) &&
            Directory.Exists(Path.Combine(userProfile, ".codex"));
    }

    /// <summary>Newest ~/.codex/state_*.sqlite database, or null when Codex is absent.</summary>
    public static string? FindLatestCodexStateDatabase()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        string codexDirectory = Path.Combine(userProfile, ".codex");
        if (!Directory.Exists(codexDirectory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(codexDirectory, "state_*.sqlite", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Tails a rollout jsonl file and returns the newest recognizable run state.
    /// Only event/item types and timestamps are inspected; payload text stays
    /// unread beyond JSON structure to keep conversation content out of Octadock.
    /// </summary>
    internal static AiSessionCodexThreadRunSnapshot ReadThreadRunState(string? rolloutPath)
    {
        string? path = AiSessionTextSanitizer.NormalizePath(rolloutPath);
        if (path is null || !File.Exists(path))
        {
            return AiSessionCodexThreadRunSnapshot.Unknown;
        }

        try
        {
            const int tailByteLimit = 128 * 1024;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long offset = Math.Max(0, stream.Length - tailByteLimit);
            stream.Seek(offset, SeekOrigin.Begin);

            int bytesToRead = (int)(stream.Length - offset);
            byte[] buffer = new byte[bytesToRead];
            int read = stream.Read(buffer, 0, buffer.Length);
            string text = Encoding.UTF8.GetString(buffer, 0, read);
            string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            for (int index = lines.Length - 1; index >= 0; index--)
            {
                AiSessionCodexThreadRunSnapshot state = ClassifyRolloutLine(lines[index]);
                if (state.State != AiSessionCodexThreadRunState.Unknown)
                {
                    return state;
                }
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return AiSessionCodexThreadRunSnapshot.Unknown;
        }

        return AiSessionCodexThreadRunSnapshot.Unknown;
    }

    private static AiSessionCodexThreadRunSnapshot ClassifyRolloutLine(string line)
    {
        AiSessionCodexThreadRunSnapshot result;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            string? type = ReadJsonString(root, "type");
            if (!root.TryGetProperty("payload", out JsonElement payload))
            {
                return AiSessionCodexThreadRunSnapshot.Unknown;
            }

            string? payloadType = ReadJsonString(payload, "type");
            DateTimeOffset? timestamp = ReadJsonTimestamp(root, "timestamp");
            if (string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(payloadType, "task_complete", StringComparison.OrdinalIgnoreCase))
            {
                result = new AiSessionCodexThreadRunSnapshot(AiSessionCodexThreadRunState.Completed, timestamp);
            }
            else if (string.Equals(type, "response_item", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(payloadType, "function_call", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(payloadType, "function_call_output", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(payloadType, "message", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(payloadType, "reasoning", StringComparison.OrdinalIgnoreCase)))
            {
                result = new AiSessionCodexThreadRunSnapshot(AiSessionCodexThreadRunState.Active, timestamp);
            }
            else if (string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(payloadType, "agent_message", StringComparison.OrdinalIgnoreCase))
            {
                result = new AiSessionCodexThreadRunSnapshot(AiSessionCodexThreadRunState.Active, timestamp);
            }
            else
            {
                result = AiSessionCodexThreadRunSnapshot.Unknown;
            }
        }
        catch (JsonException)
        {
            return AiSessionCodexThreadRunSnapshot.Unknown;
        }

        return result;
    }

    private static string BuildThreadTitle(AiSessionCodexThreadSnapshot snapshot, string? workspace)
    {
        string? nickname = TryReadSubagentNickname(snapshot.Source);
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            return $"Codex subagent - {nickname}";
        }

        string folder = AiSessionTextSanitizer.WorkspaceLabel(workspace);
        return string.IsNullOrEmpty(folder) ? "Codex session" : $"Codex - {folder}";
    }

    private static string? TryReadSubagentNickname(string? sourceJson)
    {
        if (string.IsNullOrWhiteSpace(sourceJson) || !sourceJson.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(sourceJson);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("subagent", out JsonElement subagent) &&
                subagent.TryGetProperty("thread_spawn", out JsonElement spawn) &&
                spawn.TryGetProperty("agent_nickname", out JsonElement nickname) &&
                nickname.ValueKind == JsonValueKind.String)
            {
                return AiSessionTextSanitizer.Clean(nickname.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static void AddIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset? right)
        => right is { } value && value > left ? value : left;

    private static string? ReadSqliteString(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : AiSessionTextSanitizer.Clean(reader.GetString(ordinal));
    }

    private static long? ReadSqliteInt64(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? ReadJsonTimestamp(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset timestamp)
            ? timestamp
            : null;
}

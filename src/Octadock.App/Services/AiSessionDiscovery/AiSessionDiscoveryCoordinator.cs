using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>Counters and follow-up state from one repository sync pass.</summary>
public sealed record AiSessionDiscoverySyncResult(
    int DetectedCount,
    int AddedCount,
    int CompletedCount,
    IReadOnlyList<int> ActivePids);

/// <summary>
/// Reconciles resolved observations with the durable AI-session repository:
/// creates rows for new sessions, refreshes matched rows, completes rows whose
/// backing evidence disappeared, and collapses duplicates. Only rows created by
/// discovery (metadata source = "process-discovery") are ever touched, so
/// octadock run/watch/hook sessions stay untouched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AiSessionDiscoveryCoordinator
{
    /// <summary>Metadata marker identifying rows owned by the discovery pipeline.</summary>
    public const string DiscoverySource = "process-discovery";

    /// <summary>
    /// How long a live-but-declassified process survives before its row
    /// completes. Time-based (not scan-counted) so overlay-driven fast scan
    /// cadences do not shrink the grace window.
    /// </summary>
    internal static readonly TimeSpan DeclassifiedProcessGrace = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Safety cap: an unmatched row this stale completes even when its backing
    /// evidence source keeps failing (e.g. the provider was uninstalled).
    /// </summary>
    internal static readonly TimeSpan StaleRowCutoff = TimeSpan.FromHours(6);

    private static readonly AiSessionStatus[] ActiveStatuses =
    [
        AiSessionStatus.Queued,
        AiSessionStatus.Running,
        AiSessionStatus.WaitingForInput,
        AiSessionStatus.Paused,
    ];

    private readonly IAiSessionRepository _sessions;
    private readonly Func<int, long?, bool> _isProcessAlive;
    private readonly Dictionary<Guid, DateTimeOffset> _firstMissedAt = [];

    public AiSessionDiscoveryCoordinator(
        IAiSessionRepository sessions,
        Func<int, long?, bool>? isProcessAlive = null)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _isProcessAlive = isProcessAlive ?? DefaultIsProcessAlive;
    }

    /// <summary>Applies one resolution pass to the repository.</summary>
    public async Task<AiSessionDiscoverySyncResult> SyncAsync(
        AiSessionResolution resolution,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var filter = new AiSessionFilter
        {
            Statuses = ActiveStatuses,
            Limit = 1000,
        };
        IReadOnlyList<AiSessionRecord> activeSessions =
            await _sessions.ListAsync(filter, cancellationToken).ConfigureAwait(false);
        List<RowState> rows = activeSessions
            .Where(IsDiscoverySession)
            .Select(RowState.Parse)
            .ToList();

        var added = 0;
        var completed = 0;
        var activePids = new List<int>();

        foreach (AiSessionObservation observation in resolution.Observations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<(RowState Row, int Tier)> matches = rows
                .Where(row => !row.Consumed)
                .Select(row => (Row: row, Tier: MatchTier(row, observation)))
                .Where(m => m.Tier > 0)
                .OrderBy(m => m.Tier)
                .ThenByDescending(m => m.Row.Record.LastEventAt ?? m.Row.Record.StartedAt)
                .ToList();

            RowState? canonical = matches.Count > 0 ? matches[0].Row : null;

            // Only strong-identity duplicates (key/pid/session id) are merged
            // away. A weaker workspace-only (tier 4) co-match may belong to a
            // different session in the same folder, so it is left alone and
            // handled by the normal unmatched-row rules.
            foreach ((RowState duplicate, _) in matches.Skip(1).Where(m => m.Tier <= 3))
            {
                duplicate.Consumed = true;
                _firstMissedAt.Remove(duplicate.Record.Id);
                await CompleteRowAsync(
                    duplicate.Record,
                    observedAt,
                    "Merged into another discovered session record.",
                    cancellationToken).ConfigureAwait(false);
                completed++;
            }

            if (canonical is not null)
            {
                canonical.Consumed = true;
                _firstMissedAt.Remove(canonical.Record.Id);
                if (observation.Status == AiSessionStatus.Completed)
                {
                    await CompleteRowAsync(canonical.Record, observedAt, observation.Reason, cancellationToken)
                        .ConfigureAwait(false);
                    completed++;
                }
                else
                {
                    AiSessionRecord updated = await UpdateRowAsync(
                        canonical.Record,
                        observation,
                        observedAt,
                        cancellationToken).ConfigureAwait(false);
                    if (updated.Pid is int updatedPid)
                    {
                        activePids.Add(updatedPid);
                    }
                }

                continue;
            }

            if (observation.Status == AiSessionStatus.Completed)
            {
                // Nothing to complete: never create rows for already-finished runs.
                continue;
            }

            if (ConflictsWithExplicitSession(observation, activeSessions))
            {
                // An explicit run/watch/hook session already tracks this
                // process (or one of its ancestors, e.g. the cmd.exe wrapper
                // that octadock run spawned).
                continue;
            }

            AiSessionRecord record = CreateRecord(observation, observedAt);
            await _sessions.AddAsync(record, cancellationToken).ConfigureAwait(false);
            await AddEventAsync(
                record.Id,
                AiSessionEventType.Created,
                observedAt,
                observation.Pid is int pid
                    ? $"Discovered running {record.Title} (PID {pid})."
                    : $"Discovered running {record.Title}.",
                cancellationToken).ConfigureAwait(false);
            await AddEventAsync(
                record.Id,
                AiSessionEventType.Started,
                observedAt,
                $"Tracking started from local discovery ({observation.Detector}). {observation.Reason}",
                cancellationToken).ConfigureAwait(false);
            added++;
            if (record.Pid is int newPid)
            {
                activePids.Add(newPid);
            }
        }

        completed += await CompleteUnmatchedRowsAsync(
            rows,
            resolution.SucceededSources,
            observedAt,
            activePids,
            cancellationToken).ConfigureAwait(false);

        PruneMissedScanTracking(rows);
        return new AiSessionDiscoverySyncResult(
            resolution.Observations.Count,
            added,
            completed,
            activePids);
    }

    private static bool ConflictsWithExplicitSession(
        AiSessionObservation observation,
        IReadOnlyList<AiSessionRecord> activeSessions)
    {
        var protectedPids = new HashSet<int>();
        foreach (AiSessionRecord session in activeSessions)
        {
            if (!IsDiscoverySession(session) && session.IsActive && session.Pid is int pid)
            {
                protectedPids.Add(pid);
            }
        }

        if (protectedPids.Count == 0)
        {
            return false;
        }

        if (observation.Pid is int observationPid && protectedPids.Contains(observationPid))
        {
            return true;
        }

        // run/watch rows often track a wrapper (cmd.exe, npm shim) while the
        // classified AI process is a descendant; the snapshot collector records
        // the ancestor chain for exactly this check.
        string? ancestors = observation.Metadata.GetValueOrDefault("ancestorPids");
        if (string.IsNullOrWhiteSpace(ancestors))
        {
            return false;
        }

        foreach (string token in ancestors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ancestorPid) &&
                protectedPids.Contains(ancestorPid))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<int> CompleteUnmatchedRowsAsync(
        List<RowState> rows,
        IReadOnlySet<string> succeededSources,
        DateTimeOffset observedAt,
        List<int> activePids,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        bool processSourceOk = succeededSources.Contains(ProcessSnapshotEvidenceCollector.SourceId);
        foreach (RowState row in rows.Where(r => !r.Consumed))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A verifiably dead process completes its row no matter what other
            // sources did this pass — otherwise a permanently failing provider
            // source (e.g. uninstalled Codex) would pin the row forever.
            if (row.Record.Pid is int deadCandidate &&
                processSourceOk &&
                !_isProcessAlive(deadCandidate, row.StartTicks))
            {
                _firstMissedAt.Remove(row.Record.Id);
                await CompleteRowAsync(
                    row.Record,
                    observedAt,
                    "Discovered process is no longer running.",
                    cancellationToken).ConfigureAwait(false);
                completed++;
                continue;
            }

            DateTimeOffset lastSeen = row.Record.LastEventAt ?? row.Record.StartedAt;
            if (observedAt - lastSeen >= StaleRowCutoff)
            {
                _firstMissedAt.Remove(row.Record.Id);
                await CompleteRowAsync(
                    row.Record,
                    observedAt,
                    "Session went stale with no fresh evidence and was marked completed.",
                    cancellationToken).ConfigureAwait(false);
                completed++;
                continue;
            }

            if (!RequiredSourcesForCompletion(row).All(succeededSources.Contains))
            {
                // The evidence source backing this row failed this pass; a
                // missing session there proves nothing, so keep the row.
                KeepRowActive(row, activePids);
                continue;
            }

            string message;
            if (row.Record.Pid is not null)
            {
                // The process is still alive (the dead case completed above)
                // but no longer classifies as a session, or one scan flickered.
                // Give it a time-based grace window before completing.
                if (!_firstMissedAt.TryGetValue(row.Record.Id, out DateTimeOffset firstMissedAt))
                {
                    firstMissedAt = observedAt;
                    _firstMissedAt[row.Record.Id] = firstMissedAt;
                }

                if (observedAt - firstMissedAt < DeclassifiedProcessGrace)
                {
                    KeepRowActive(row, activePids);
                    continue;
                }

                message = "Session is no longer detected by discovery.";
            }
            else
            {
                message = "Provider state no longer reports this session as active.";
            }

            _firstMissedAt.Remove(row.Record.Id);
            await CompleteRowAsync(row.Record, observedAt, message, cancellationToken).ConfigureAwait(false);
            completed++;
        }

        return completed;
    }

    private static void KeepRowActive(RowState row, List<int> activePids)
    {
        if (row.Record.Pid is int pid)
        {
            activePids.Add(pid);
        }
    }

    private void PruneMissedScanTracking(List<RowState> rows)
    {
        if (_firstMissedAt.Count == 0)
        {
            return;
        }

        var known = rows.Select(r => r.Record.Id).ToHashSet();
        List<Guid> stale = _firstMissedAt.Keys.Where(id => !known.Contains(id)).ToList();
        foreach (Guid id in stale)
        {
            _firstMissedAt.Remove(id);
        }
    }

    /// <summary>Higher tiers are weaker matches; 0 means no match.</summary>
    internal static int MatchTier(RowState row, AiSessionObservation observation)
    {
        if (row.DiscoveryKey is not null &&
            string.Equals(row.DiscoveryKey, observation.DiscoveryKey, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (row.Record.Provider != observation.Provider)
        {
            return 0;
        }

        if (row.Record.Pid is int rowPid &&
            observation.Pid == rowPid &&
            StartTicksCompatible(row.StartTicks, observation.ProcessStartTicks))
        {
            return 2;
        }

        if (row.SessionId is not null &&
            string.Equals(row.SessionId, observation.ProviderSessionId, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        // Workspace adoption: only when at most one side carries a native
        // session id and the pids do not contradict, so two distinct id'd
        // sessions in the same folder never merge.
        if ((row.SessionId is null || observation.ProviderSessionId is null) &&
            (row.Record.Pid is null || observation.Pid is null || row.Record.Pid == observation.Pid) &&
            AiSessionIdentityResolver.WorkspacesMatch(row.Workspace, observation.WorkspacePath))
        {
            return 4;
        }

        return 0;
    }

    private static bool StartTicksCompatible(long? rowTicks, long? observationTicks)
        => rowTicks is not long row ||
            observationTicks is not long observation ||
            Math.Abs(row - observation) <= TimeSpan.TicksPerSecond;

    private static IReadOnlyList<string> RequiredSourcesForCompletion(RowState row)
    {
        var required = new List<string>(3);
        if (row.Record.Pid is not null)
        {
            required.Add(ProcessSnapshotEvidenceCollector.SourceId);
        }

        if (row.HasMetadata("codexThreadId") ||
            string.Equals(row.Detector, "codex-state-thread", StringComparison.OrdinalIgnoreCase))
        {
            required.Add(CodexStateEvidenceCollector.SourceId);
        }

        if (row.HasMetadata("claudeSessionId") ||
            string.Equals(row.Detector, "claude-project-transcript", StringComparison.OrdinalIgnoreCase))
        {
            required.Add(ClaudeCodeStateEvidenceCollector.SourceId);
        }

        if (required.Count == 0)
        {
            required.Add(ProcessSnapshotEvidenceCollector.SourceId);
        }

        return required;
    }

    private async Task<AiSessionRecord> UpdateRowAsync(
        AiSessionRecord existing,
        AiSessionObservation observation,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        string metadataJson = BuildMetadataJson(observation, existing.MetadataJson);
        DateTimeOffset? nextLastEventAt = MaxNullable(existing.LastEventAt, observation.LastActivityAt);
        AiSessionStatus nextStatus =
            observation.Status is AiSessionStatus.Running or AiSessionStatus.WaitingForInput
                ? observation.Status
                : existing.Status;

        AiSessionRecord next = existing with
        {
            Provider = observation.Provider,
            Title = observation.Title,
            Cwd = observation.WorkspacePath ?? existing.Cwd,
            Command = observation.Command ?? existing.Command,
            Pid = observation.Pid ?? existing.Pid,
            Status = nextStatus,
            LastEventAt = nextLastEventAt,
            MetadataJson = metadataJson,
        };

        if (RecordsEquivalent(existing, next))
        {
            return existing;
        }

        await _sessions.UpdateAsync(next, cancellationToken).ConfigureAwait(false);
        if (next.Status != existing.Status)
        {
            await AddEventAsync(
                existing.Id,
                AiSessionEventType.StatusChanged,
                observedAt,
                $"Status changed to {next.Status}.",
                cancellationToken).ConfigureAwait(false);
        }

        return next;
    }

    private static bool RecordsEquivalent(AiSessionRecord left, AiSessionRecord right)
        => left.Provider == right.Provider &&
            string.Equals(left.Title, right.Title, StringComparison.Ordinal) &&
            string.Equals(left.Cwd, right.Cwd, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(left.Command, right.Command, StringComparison.Ordinal) &&
            left.Pid == right.Pid &&
            left.Status == right.Status &&
            string.Equals(left.MetadataJson, right.MetadataJson, StringComparison.Ordinal) &&
            Nullable.Equals(left.LastEventAt, right.LastEventAt);

    private async Task CompleteRowAsync(
        AiSessionRecord record,
        DateTimeOffset observedAt,
        string message,
        CancellationToken cancellationToken)
    {
        await _sessions.UpdateAsync(record with
        {
            Status = AiSessionStatus.Completed,
            EndedAt = observedAt,
            LastEventAt = observedAt,
        }, cancellationToken).ConfigureAwait(false);
        await AddEventAsync(
            record.Id,
            AiSessionEventType.Completed,
            observedAt,
            message,
            cancellationToken).ConfigureAwait(false);
    }

    private AiSessionRecord CreateRecord(AiSessionObservation observation, DateTimeOffset observedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Provider = observation.Provider,
            Title = observation.Title,
            Cwd = observation.WorkspacePath,
            Command = observation.Command,
            Pid = observation.Pid,
            Status = observation.Status == AiSessionStatus.WaitingForInput
                ? AiSessionStatus.WaitingForInput
                : AiSessionStatus.Running,
            StartedAt = observation.StartedAt,
            LastEventAt = observation.LastActivityAt ?? observedAt,
            NotificationMode = AiSessionNotificationMode.Silent,
            MetadataJson = BuildMetadataJson(observation),
        };

    private Task AddEventAsync(
        Guid sessionId,
        AiSessionEventType eventType,
        DateTimeOffset createdAt,
        string message,
        CancellationToken cancellationToken)
        => _sessions.AddEventAsync(new AiSessionEventRecord
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            EventType = eventType,
            CreatedAt = createdAt,
            Message = message,
        }, cancellationToken);

    /// <summary>Identity fields kept across updates when a weaker observation lacks them.</summary>
    private static readonly string[] PreservedIdentityKeys =
    [
        "sessionId",
        "codexThreadId",
        "claudeSessionId",
        "processStartTicks",
    ];

    /// <summary>Serializes the diagnostic metadata explaining how a session was detected.</summary>
    internal static string BuildMetadataJson(AiSessionObservation observation, string? existingMetadataJson = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = DiscoverySource,
            ["discoveryKey"] = observation.DiscoveryKey,
            ["detector"] = observation.Detector,
            ["confidence"] = observation.Confidence.ToString("0.00", CultureInfo.InvariantCulture),
            ["reason"] = AiSessionTextSanitizer.Truncate(observation.Reason, AiSessionTextSanitizer.MaxMetadataValueLength)!,
            ["evidenceSources"] = string.Join("+", observation.Sources),
        };

        foreach (KeyValuePair<string, string> entry in observation.Metadata)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            {
                metadata.TryAdd(entry.Key, entry.Value);
            }
        }

        // A process-only observation adopting a state-identified row must not
        // erase the row's session identity, or tier-1/3 matching (and resume
        // continuity) breaks the next time the provider state wakes up.
        if (existingMetadataJson is not null)
        {
            foreach (string key in PreservedIdentityKeys)
            {
                if (!metadata.ContainsKey(key) &&
                    ReadMetadataString(existingMetadataJson, key) is { } prior)
                {
                    metadata[key] = prior;
                }
            }
        }

        return JsonSerializer.Serialize(metadata);
    }

    /// <summary>True when this row is owned by the discovery pipeline.</summary>
    public static bool IsDiscoverySession(AiSessionRecord record)
        => string.Equals(
            ReadMetadataString(record.MetadataJson, "source"),
            DiscoverySource,
            StringComparison.OrdinalIgnoreCase);

    internal static string? ReadMetadataString(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? MaxNullable(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        return right is { } value && value > left.Value ? value : left;
    }

    private static bool DefaultIsProcessAlive(int pid, long? expectedStartTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }

            if (expectedStartTicks is long ticks)
            {
                DateTimeOffset? liveStart = ProcessSnapshotEvidenceCollector.TryGetStartTime(process);
                if (liveStart is not null &&
                    Math.Abs(liveStart.Value.UtcTicks - ticks) > TimeSpan.TicksPerSecond)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Parsed identity fields of one existing discovery-owned row.</summary>
    internal sealed class RowState
    {
        private RowState(AiSessionRecord record)
        {
            Record = record;
        }

        public AiSessionRecord Record { get; }

        public string? DiscoveryKey { get; private init; }

        public string? SessionId { get; private init; }

        public long? StartTicks { get; private init; }

        public string? Workspace { get; private init; }

        public string? Detector { get; private init; }

        public bool Consumed { get; set; }

        public bool HasMetadata(string key)
            => ReadMetadataString(Record.MetadataJson, key) is not null;

        public static RowState Parse(AiSessionRecord record)
        {
            string? discoveryKey = ReadMetadataString(record.MetadataJson, "discoveryKey") ??
                ReadMetadataString(record.MetadataJson, "processKey");
            string? startTicksText = ReadMetadataString(record.MetadataJson, "processStartTicks") ??
                ReadStartTicksFromKey(discoveryKey);
            long? startTicks = long.TryParse(
                startTicksText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsedTicks)
                ? parsedTicks
                : null;
            string? sessionId = ReadMetadataString(record.MetadataJson, "sessionId") ??
                ReadMetadataString(record.MetadataJson, "codexThreadId") ??
                ReadMetadataString(record.MetadataJson, "claudeSessionId");

            return new RowState(record)
            {
                DiscoveryKey = discoveryKey,
                SessionId = sessionId,
                StartTicks = startTicks,
                Workspace = record.Cwd ?? ReadMetadataString(record.MetadataJson, "workingDirectory"),
                Detector = ReadMetadataString(record.MetadataJson, "detector"),
            };
        }

        private static string? ReadStartTicksFromKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            int index = key.LastIndexOf(':');
            return index >= 0 && index < key.Length - 1 ? key[(index + 1)..] : null;
        }
    }
}

using System.Globalization;
using Octadock.Core.Models;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>
/// Merges raw evidence from all collectors into canonical observations.
/// Identity precedence: provider-native session id, then workspace correlation
/// within the same provider, then process identity (pid + start time). The same
/// logical session seen through a process snapshot and provider state therefore
/// produces one observation, not two rows.
/// </summary>
public sealed class AiSessionIdentityResolver
{
    /// <summary>Groups evidence, applies corroboration and confidence rules, returns observations.</summary>
    public AiSessionResolution Resolve(
        IReadOnlyList<AiSessionEvidenceBatch> batches,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(batches);

        var succeededSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evidence = new List<AiSessionEvidence>();
        foreach (AiSessionEvidenceBatch batch in batches)
        {
            if (!batch.Succeeded)
            {
                continue;
            }

            succeededSources.Add(batch.Source);
            evidence.AddRange(batch.Evidence);
        }

        List<EvidenceGroup> groups = BuildGroups(evidence, observedAt);

        var observations = new List<AiSessionObservation>();
        var dropped = new List<AiSessionResolutionDrop>();
        foreach (EvidenceGroup group in groups)
        {
            if (TryBuildObservation(group, succeededSources, observedAt, out AiSessionObservation? observation, out string? dropReason))
            {
                observations.Add(observation!);
            }
            else
            {
                foreach (AiSessionEvidence member in group.Members)
                {
                    dropped.Add(new AiSessionResolutionDrop(member, dropReason!));
                }
            }
        }

        return new AiSessionResolution(observations, dropped, succeededSources);
    }

    /// <summary>How stale a state group may be and still claim a workspace-less process.</summary>
    private static readonly TimeSpan FallbackPairingWindow = TimeSpan.FromMinutes(2);

    private static List<EvidenceGroup> BuildGroups(
        IReadOnlyList<AiSessionEvidence> evidence,
        DateTimeOffset observedAt)
    {
        var groups = new List<EvidenceGroup>();
        var groupsById = new Dictionary<(AiSessionProvider Provider, string SessionId), EvidenceGroup>();

        EvidenceGroup CreateGroup(AiSessionEvidence item, string? anchorId)
        {
            var group = new EvidenceGroup(item.Provider);
            group.Members.Add(item);
            groups.Add(group);
            if (anchorId is not null)
            {
                groupsById.TryAdd((item.Provider, anchorId), group);
            }

            return group;
        }

        // Stage A: provider-state evidence anchors groups by native session id.
        foreach (AiSessionEvidence item in evidence)
        {
            if (IsProcessEvidence(item))
            {
                continue;
            }

            string anchorId = item.ProviderSessionId ??
                "state:" + AiSessionTextSanitizer.ClaudeProjectToken(item.StateFilePath ?? item.LogFilePath ?? item.Title);
            if (groupsById.TryGetValue((item.Provider, anchorId), out EvidenceGroup? existing))
            {
                existing.Members.Add(item);
            }
            else
            {
                CreateGroup(item, anchorId);
            }
        }

        // Stage B pass 1: exact joins (session id, then workspace token). Exact
        // matches always run before any fallback so a workspace-matched process
        // can never lose its group to a workspace-less latecomer.
        IReadOnlyList<AiSessionEvidence> processEvidence = evidence
            .Where(IsProcessEvidence)
            .OrderByDescending(e => e.StartedAt ?? DateTimeOffset.MinValue)
            .ToArray();
        var unmatched = new List<AiSessionEvidence>();
        foreach (AiSessionEvidence item in processEvidence)
        {
            if (item.ProviderSessionId is not null &&
                groupsById.TryGetValue((item.Provider, item.ProviderSessionId), out EvidenceGroup? byId))
            {
                byId.Members.Add(item);
                byId.ClaimedByProcess = true;
                continue;
            }

            EvidenceGroup? byWorkspace = groups
                .Where(g => g.Provider == item.Provider &&
                    !g.ClaimedByProcess &&
                    g.HasStateEvidence &&
                    WorkspacesMatch(g.WorkspacePath, item.WorkspacePath))
                .OrderByDescending(g => g.LastActivityAt)
                .FirstOrDefault();
            if (byWorkspace is not null)
            {
                byWorkspace.Members.Add(item);
                byWorkspace.ClaimedByProcess = true;
                continue;
            }

            unmatched.Add(item);
        }

        // Stage B pass 2: command lines often omit the cwd (e.g. the Claude
        // Code CLI), so a workspace-less process still pairs with the freshest
        // unclaimed state group of its provider — but only a group that looks
        // ALIVE right now; pairing a live process with a completed or quiet
        // state group would wrongly complete the live session.
        foreach (AiSessionEvidence item in unmatched)
        {
            if (item.ProviderSessionId is not null &&
                groupsById.TryGetValue((item.Provider, item.ProviderSessionId), out EvidenceGroup? byId))
            {
                // A sibling process with the same session id registered a
                // standalone group in this pass; join it instead of splitting.
                byId.Members.Add(item);
                continue;
            }

            if (item.WorkspacePath is null)
            {
                EvidenceGroup? freshest = groups
                    .Where(g => g.Provider == item.Provider &&
                        !g.ClaimedByProcess &&
                        g.HasStateEvidence &&
                        ResolveStatus(g.Members) != AiSessionStatus.Completed &&
                        g.LastActivityAt >= observedAt.Subtract(FallbackPairingWindow))
                    .OrderByDescending(g => g.LastActivityAt)
                    .FirstOrDefault();
                if (freshest is not null)
                {
                    freshest.Members.Add(item);
                    freshest.ClaimedByProcess = true;
                    continue;
                }
            }

            CreateGroup(item, item.ProviderSessionId);
        }

        return groups;
    }

    private static bool TryBuildObservation(
        EvidenceGroup group,
        IReadOnlySet<string> succeededSources,
        DateTimeOffset observedAt,
        out AiSessionObservation? observation,
        out string? dropReason)
    {
        observation = null;
        dropReason = null;
        List<AiSessionEvidence> members = group.Members;

        // Corroboration: evidence like idle-prone Codex runtime workers only
        // stands when its corroborating state source is unavailable, or when
        // matching state evidence actually joined the group.
        if (members.All(m => m.RequiresCorroboration))
        {
            string? availableCorroborator = members
                .Select(m => m.CorroborationSource)
                .FirstOrDefault(s => s is not null && succeededSources.Contains(s));
            if (availableCorroborator is not null)
            {
                dropReason =
                    $"Requires corroboration from '{availableCorroborator}', which ran but had no matching session state.";
                return false;
            }
        }

        double confidence = members.Max(m => m.Confidence);
        int distinctSources = members.Select(m => m.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        confidence = Math.Min(0.99, confidence + (0.05 * (distinctSources - 1)));
        if (confidence < AiSessionConfidence.MinimumToShow)
        {
            dropReason = string.Create(
                CultureInfo.InvariantCulture,
                $"Confidence {confidence:0.00} is below the minimum {AiSessionConfidence.MinimumToShow:0.00} to show a session.");
            return false;
        }

        AiSessionEvidence primary = members
            .OrderByDescending(m => m.ProviderSessionId is not null ? 1 : 0)
            .ThenByDescending(m => m.Confidence)
            .First();
        AiSessionEvidence? processMember = members
            .Where(m => m.Pid is not null)
            .OrderByDescending(m => m.Confidence)
            .FirstOrDefault();

        AiSessionStatus status = ResolveStatus(members);
        string discoveryKey = BuildDiscoveryKey(group.Provider, members, primary, processMember);
        string? workspace = processMember?.WorkspacePath ??
            members.OrderByDescending(m => m.Confidence).Select(m => m.WorkspacePath).FirstOrDefault(w => w is not null);
        DateTimeOffset startedAt = members
            .Select(m => m.StartedAt)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .DefaultIfEmpty(observedAt)
            .Min();
        DateTimeOffset? lastActivity = members
            .Select(m => m.LastActivityAt)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .DefaultIfEmpty()
            .Max();
        string[] sources = members
            .Select(m => m.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string reason = distinctSources > 1
            ? $"{primary.Reason} Corroborated by {distinctSources} evidence sources."
            : primary.Reason;

        observation = new AiSessionObservation
        {
            DiscoveryKey = discoveryKey,
            Provider = group.Provider,
            Title = AiSessionTextSanitizer.SanitizeTitle(primary.Title, group.Provider.ToString()),
            Confidence = confidence,
            Status = status,
            Detector = primary.Detector,
            Reason = reason,
            Command = processMember?.Command ?? primary.Command,
            Pid = processMember?.Pid,
            ProcessStartTicks = processMember?.ProcessStartTicks,
            WorkspacePath = workspace,
            ProviderSessionId = members.Select(m => m.ProviderSessionId).FirstOrDefault(s => s is not null),
            StartedAt = startedAt,
            LastActivityAt = lastActivity == default ? null : lastActivity,
            Sources = sources,
            Metadata = MergeMetadata(members, processMember, workspace),
        };
        return true;
    }

    private static AiSessionStatus ResolveStatus(IReadOnlyList<AiSessionEvidence> members)
    {
        AiSessionEvidence? bestHint = null;
        foreach (AiSessionEvidence member in members)
        {
            if (member.StatusHint is null)
            {
                continue;
            }

            if (bestHint is null || CompareHints(member, bestHint) > 0)
            {
                bestHint = member;
            }
        }

        return bestHint?.StatusHint ?? AiSessionStatus.Running;
    }

    private static int CompareHints(AiSessionEvidence left, AiSessionEvidence right)
    {
        DateTimeOffset leftAt = left.LastActivityAt ?? DateTimeOffset.MinValue;
        DateTimeOffset rightAt = right.LastActivityAt ?? DateTimeOffset.MinValue;
        int byTime = leftAt.CompareTo(rightAt);
        if (byTime != 0)
        {
            return byTime;
        }

        // Same timestamp: prefer the active hint over a terminal one.
        static int ActiveRank(AiSessionStatus? status)
            => status is AiSessionStatus.Running or AiSessionStatus.WaitingForInput ? 1 : 0;
        return ActiveRank(left.StatusHint).CompareTo(ActiveRank(right.StatusHint));
    }

    private static string BuildDiscoveryKey(
        AiSessionProvider provider,
        IReadOnlyList<AiSessionEvidence> members,
        AiSessionEvidence primary,
        AiSessionEvidence? processMember)
    {
        AiSessionEvidence? idMember = members
            .Where(m => m.ProviderSessionId is not null)
            .OrderByDescending(m => IsProcessEvidence(m) ? 0 : 1)
            .ThenByDescending(m => m.Confidence)
            .FirstOrDefault();
        if (idMember is not null)
        {
            return $"{provider}:session:{idMember.ProviderSessionId}";
        }

        if (processMember?.Pid is int pid)
        {
            string ticks = processMember.ProcessStartTicks?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            return $"{provider}:pid:{pid}:{ticks}";
        }

        string anchor = AiSessionTextSanitizer.ClaudeProjectToken(
            primary.StateFilePath ?? primary.LogFilePath ?? primary.Title);
        return $"{provider}:state:{anchor}";
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyList<AiSessionEvidence> members,
        AiSessionEvidence? processMember,
        string? workspace)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Lower-confidence first so stronger evidence overwrites shared keys.
        foreach (AiSessionEvidence member in members.OrderBy(m => m.Confidence))
        {
            if (member.Metadata is null)
            {
                continue;
            }

            foreach (KeyValuePair<string, string> entry in member.Metadata)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
                {
                    continue;
                }

                if (metadata.Count >= AiSessionTextSanitizer.MaxMetadataEntries - 12 &&
                    !metadata.ContainsKey(entry.Key))
                {
                    continue;
                }

                // Values can carry conversation-derived text (e.g. Codex thread
                // titles), so every persisted value is redacted and bounded.
                string? sanitized = AiSessionTextSanitizer.Truncate(
                    AiSessionTextSanitizer.RedactSecrets(entry.Value),
                    AiSessionTextSanitizer.MaxMetadataValueLength);
                if (sanitized is not null)
                {
                    metadata[entry.Key] = sanitized;
                }
            }
        }

        SetIfPresent(metadata, "processName", processMember?.ProcessName);
        SetIfPresent(metadata, "executablePath", processMember?.ExecutablePath);
        SetIfPresent(metadata, "mainWindowTitle", processMember?.MainWindowTitle);
        SetIfPresent(
            metadata,
            "parentPid",
            processMember?.ParentPid?.ToString(CultureInfo.InvariantCulture));
        SetIfPresent(
            metadata,
            "processStartTicks",
            processMember?.ProcessStartTicks?.ToString(CultureInfo.InvariantCulture));
        SetIfPresent(metadata, "workingDirectory", workspace);
        SetIfPresent(metadata, "sessionId", members.Select(m => m.ProviderSessionId).FirstOrDefault(s => s is not null));
        SetIfPresent(metadata, "stateFilePath", members.Select(m => m.StateFilePath).FirstOrDefault(s => s is not null));
        SetIfPresent(metadata, "logFilePath", members.Select(m => m.LogFilePath).FirstOrDefault(s => s is not null));
        return metadata;
    }

    private static void SetIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = AiSessionTextSanitizer.Truncate(value, AiSessionTextSanitizer.MaxMetadataValueLength)!;
        }
    }

    private static bool IsProcessEvidence(AiSessionEvidence evidence)
        => evidence.Pid is not null;

    /// <summary>
    /// Loose workspace equality: paths are compared through the same collapsed
    /// token Claude Code uses for project folders, which also absorbs case and
    /// separator differences between provider state and process command lines.
    /// </summary>
    internal static bool WorkspacesMatch(string? left, string? right)
    {
        string leftToken = AiSessionTextSanitizer.ClaudeProjectToken(left);
        string rightToken = AiSessionTextSanitizer.ClaudeProjectToken(right);
        return leftToken.Length > 0 && string.Equals(leftToken, rightToken, StringComparison.Ordinal);
    }

    private sealed class EvidenceGroup(AiSessionProvider provider)
    {
        public AiSessionProvider Provider { get; } = provider;

        public List<AiSessionEvidence> Members { get; } = [];

        public bool ClaimedByProcess { get; set; }

        public bool HasStateEvidence => Members.Any(m => !IsProcessEvidence(m));

        public string? WorkspacePath => Members
            .Select(m => m.WorkspacePath)
            .FirstOrDefault(w => w is not null) ??
            Members
                .Select(m => m.Metadata?.GetValueOrDefault("claudeProjectDir"))
                .FirstOrDefault(d => d is not null);

        public DateTimeOffset LastActivityAt => Members
            .Select(m => m.LastActivityAt ?? DateTimeOffset.MinValue)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
    }
}

using Octadock.Core.Models;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>
/// One normalized observation of a possible AI session from a single evidence
/// source (a process snapshot, a provider state file, a log tail, ...).
/// Collectors emit these; the <see cref="AiSessionIdentityResolver"/> merges
/// them into canonical observations. All fields are best-effort and sensitive
/// values (command lines, paths) must already be sanitized by the collector.
/// </summary>
public sealed record AiSessionEvidence
{
    /// <summary>Collector id that produced this evidence (e.g. "process-snapshot").</summary>
    public required string Source { get; init; }

    /// <summary>Classifier/rule id that recognized the session (e.g. "codex-runtime-session").</summary>
    public required string Detector { get; init; }

    public required AiSessionProvider Provider { get; init; }

    /// <summary>How sure this evidence is that a real user-facing session exists (0..1).</summary>
    public required double Confidence { get; init; }

    /// <summary>Human-readable diagnostic explaining why this was detected.</summary>
    public required string Reason { get; init; }

    public string? Title { get; init; }

    public string? Command { get; init; }

    public int? Pid { get; init; }

    public int? ParentPid { get; init; }

    /// <summary>UTC ticks of the process start time, used to disambiguate PID reuse.</summary>
    public long? ProcessStartTicks { get; init; }

    public string? ProcessName { get; init; }

    public string? ExecutablePath { get; init; }

    public string? MainWindowTitle { get; init; }

    /// <summary>Normalized workspace/cwd path, when known.</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>Provider-native session/thread id, the strongest identity signal.</summary>
    public string? ProviderSessionId { get; init; }

    /// <summary>Provider state file (SQLite db, config) backing this evidence.</summary>
    public string? StateFilePath { get; init; }

    /// <summary>Provider log/transcript file backing this evidence.</summary>
    public string? LogFilePath { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? LastActivityAt { get; init; }

    /// <summary>
    /// Lifecycle hint from provider state (Running/WaitingForInput/Completed).
    /// Null means the evidence only proves existence, not lifecycle.
    /// </summary>
    public AiSessionStatus? StatusHint { get; init; }

    /// <summary>
    /// When true this evidence alone is not enough: if the corroborating source
    /// ran successfully and produced no matching evidence, the resolver drops
    /// the observation (e.g. idle Codex runtime workers without a live thread).
    /// </summary>
    public bool RequiresCorroboration { get; init; }

    /// <summary>Source id whose evidence can corroborate this one.</summary>
    public string? CorroborationSource { get; init; }

    /// <summary>Extra provider-specific metadata (already sanitized and bounded).</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Everything one collector produced in one scan pass.</summary>
public sealed record AiSessionEvidenceBatch(
    string Source,
    bool Succeeded,
    IReadOnlyList<AiSessionEvidence> Evidence,
    string? FailureReason = null)
{
    public static AiSessionEvidenceBatch Failed(string source, string reason)
        => new(source, Succeeded: false, [], reason);
}

/// <summary>A collector that gathers raw session evidence from one local source.</summary>
public interface IAiSessionEvidenceCollector
{
    /// <summary>Stable id used for corroboration and diagnostics.</summary>
    string Source { get; }

    /// <summary>
    /// Collects evidence for the current scan. Implementations must not throw:
    /// failures are reported through <see cref="AiSessionEvidenceBatch.Succeeded"/>
    /// so the coordinator knows not to complete rows backed by this source.
    /// </summary>
    Task<AiSessionEvidenceBatch> CollectAsync(DateTimeOffset observedAt, CancellationToken cancellationToken);
}

/// <summary>One canonical session after identity resolution, ready to sync.</summary>
public sealed record AiSessionObservation
{
    /// <summary>Stable identity key persisted in row metadata (survives re-scans).</summary>
    public required string DiscoveryKey { get; init; }

    public required AiSessionProvider Provider { get; init; }

    public required string Title { get; init; }

    public required double Confidence { get; init; }

    /// <summary>Resolved lifecycle: Running/WaitingForInput keep or create rows, Completed only completes existing rows.</summary>
    public required AiSessionStatus Status { get; init; }

    /// <summary>Primary detector id (from the strongest evidence).</summary>
    public required string Detector { get; init; }

    /// <summary>Diagnostic explanation of why this session was detected.</summary>
    public required string Reason { get; init; }

    public string? Command { get; init; }

    public int? Pid { get; init; }

    public long? ProcessStartTicks { get; init; }

    public string? WorkspacePath { get; init; }

    public string? ProviderSessionId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? LastActivityAt { get; init; }

    /// <summary>Distinct collector sources that contributed evidence.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>Merged, sanitized, bounded metadata for the repository row.</summary>
    public required IReadOnlyDictionary<string, string> Metadata { get; init; }
}

/// <summary>Evidence the resolver refused to surface, with the reason kept for diagnostics.</summary>
public sealed record AiSessionResolutionDrop(AiSessionEvidence Evidence, string Reason);

/// <summary>Output of one identity-resolution pass.</summary>
public sealed record AiSessionResolution(
    IReadOnlyList<AiSessionObservation> Observations,
    IReadOnlyList<AiSessionResolutionDrop> Dropped,
    IReadOnlySet<string> SucceededSources);

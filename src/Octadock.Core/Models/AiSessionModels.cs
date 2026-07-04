namespace Octadock.Core.Models;

/// <summary>Known sources for tracked AI or automation runs.</summary>
public enum AiSessionProvider
{
    Unknown = 0,
    Generic,
    ClaudeCode,
    Codex,
    Cursor,
    GitHubCopilot,
    Gemini,
    Jules,
    Devin,
    Vercel,
    Shell,
    Other,
}

/// <summary>High-level lifecycle state for a tracked AI session.</summary>
public enum AiSessionStatus
{
    Unknown = 0,
    Queued,
    Running,
    WaitingForInput,
    Paused,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>How Octadock should notify the user about this session.</summary>
public enum AiSessionNotificationMode
{
    Default = 0,
    Silent,
    Toast,
    ToastAndSound,
    ToastAndTts,
}

/// <summary>Append-only event kinds captured while a session runs.</summary>
public enum AiSessionEventType
{
    Unknown = 0,
    Created,
    Started,
    StatusChanged,
    Output,
    Error,
    WaitingForInput,
    Completed,
    ArtifactLinked,
    Notification,
    Metadata,
    Heartbeat,
}

/// <summary>Artifacts that can be associated with an AI session.</summary>
public enum AiSessionArtifactKind
{
    Unknown = 0,
    File,
    Capture,
    Recording,
    OcrText,
    Url,
    PullRequest,
    Commit,
    Branch,
    Log,
    ContextBundle,
    Other,
}

/// <summary>
/// Durable metadata for one tracked AI/tool run. This is provider-neutral so the
/// same table can represent wrapped commands, watched PIDs, cloud agents, and
/// future MCP-backed sessions.
/// </summary>
public sealed record AiSessionRecord
{
    public required Guid Id { get; init; }

    public required AiSessionProvider Provider { get; init; }

    public required string Title { get; init; }

    public string? Cwd { get; init; }

    public string? GitBranch { get; init; }

    public string? Command { get; init; }

    public int? Pid { get; init; }

    public required AiSessionStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public int? ExitCode { get; init; }

    public DateTimeOffset? LastEventAt { get; init; }

    public AiSessionNotificationMode NotificationMode { get; init; } = AiSessionNotificationMode.Default;

    public string? MetadataJson { get; init; }

    public bool IsActive =>
        Status is AiSessionStatus.Queued or AiSessionStatus.Running or AiSessionStatus.WaitingForInput or AiSessionStatus.Paused;
}

/// <summary>One timestamped event inside an AI session timeline.</summary>
public sealed record AiSessionEventRecord
{
    public required Guid Id { get; init; }

    public required Guid SessionId { get; init; }

    public required AiSessionEventType EventType { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? Message { get; init; }

    public string? MetadataJson { get; init; }
}

/// <summary>A file, capture, URL, PR, log, or other artifact linked to an AI session.</summary>
public sealed record AiSessionArtifactRecord
{
    public required Guid Id { get; init; }

    public required Guid SessionId { get; init; }

    public required AiSessionArtifactKind ArtifactKind { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public Guid? CaptureId { get; init; }

    public string? ExternalId { get; init; }

    public string? Path { get; init; }

    public string? Uri { get; init; }

    public string? Title { get; init; }

    public string? MetadataJson { get; init; }
}

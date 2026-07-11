namespace Octadock.WorkflowIntelligence.Internal;

internal enum ProviderKind
{
    Claude = 0,
    Codex,
}

internal sealed record CorpusInventory(
    ProviderKind Provider,
    string Root,
    int JsonlFiles,
    int CompressedFiles,
    long TotalBytes,
    long LargestFileBytes,
    DateTimeOffset? NewestWriteAt);

internal sealed record ProviderInspection(
    ProviderKind Provider,
    bool Healthy,
    int FilesInspected,
    long CharactersInspected,
    int RecordsParsed,
    int ParseErrors,
    int UserRecords,
    int AssistantRecords,
    int StartBoundaries,
    int CompletionBoundaries,
    IReadOnlyList<string> RecordTypes,
    IReadOnlyList<string> Warnings);

internal sealed record CorpusSample(
    ProviderKind Provider,
    string RelativePath,
    long Bytes,
    DateTimeOffset LastWriteAt,
    string SelectionReason);

internal sealed record CorpusSampleManifest(
    DateTimeOffset CreatedAt,
    int RequestedCount,
    IReadOnlyList<CorpusSample> Samples,
    string Notice);

internal sealed record TailInspection(
    int RecordsParsed,
    int ParseErrors,
    int UserRecords,
    int AssistantRecords,
    int StartBoundaries,
    int CompletionBoundaries,
    IReadOnlyList<string> RecordTypes);

internal sealed record HookPayload(
    ProviderKind Provider,
    string EventName,
    string? SessionId,
    string? TurnId,
    string? PromptId,
    string? TranscriptPath,
    string? WorkingDirectory,
    string? StopReason,
    bool StopHookActive,
    string? LastAssistantMessage);

namespace Octadock.Core.Context;

/// <summary>
/// Decides whether a new Context item is snapshotted (copied into managed storage) or
/// referenced (WS10, Architecture-Now). Small items are copied so they survive the
/// source being discarded; large items are referenced to avoid blowing up disk, and can
/// be materialized on demand.
/// </summary>
public static class ContextIngestPolicy
{
    /// <summary>Items at or below this size are snapshotted; larger ones are referenced.</summary>
    public const long DefaultSnapshotThresholdBytes = 25L * 1024 * 1024;

    /// <summary>Chooses snapshot vs reference for an item of the given size.</summary>
    public static ContextOwnership Decide(long sizeBytes, long thresholdBytes = DefaultSnapshotThresholdBytes)
        => sizeBytes <= thresholdBytes ? ContextOwnership.Snapshot : ContextOwnership.Reference;
}

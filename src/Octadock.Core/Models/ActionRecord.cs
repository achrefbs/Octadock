namespace Octadock.Core.Models;

/// <summary>A single action performed on a capture, for the history timeline.</summary>
public sealed record ActionRecord
{
    public required Guid Id { get; init; }

    public required Guid CaptureId { get; init; }

    public required ActionType ActionType { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Optional destination (folder, app, upload provider, clipboard).</summary>
    public string? Destination { get; init; }

    /// <summary>Optional JSON blob with action-specific detail.</summary>
    public string? MetadataJson { get; init; }
}

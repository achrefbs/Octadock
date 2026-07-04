using Octadock.Core.Models;

namespace Octadock.Core.Persistence;

/// <summary>Sort orders for history queries.</summary>
public enum CaptureSortOrder
{
    NewestFirst = 0,
    OldestFirst,
}

/// <summary>
/// A filter for querying the capture history. All members are optional; an empty
/// filter returns the most recent, non-deleted captures.
/// </summary>
public sealed record CaptureFilter
{
    /// <summary>Restrict to these capture types (null/empty = all types).</summary>
    public IReadOnlyCollection<CaptureType>? Types { get; init; }

    /// <summary>Only captures created at or after this instant.</summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>Only captures created at or before this instant.</summary>
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>Free-text match against source process/window.</summary>
    public string? SearchText { get; init; }

    /// <summary>Include soft-deleted captures in the result.</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Only captures that have an annotation project.</summary>
    public bool? HasProject { get; init; }

    public CaptureSortOrder SortOrder { get; init; } = CaptureSortOrder.NewestFirst;

    /// <summary>Max rows to return (paging).</summary>
    public int Limit { get; init; } = 200;

    /// <summary>Rows to skip (paging).</summary>
    public int Offset { get; init; }

    public static readonly CaptureFilter RecentDefault = new();
}

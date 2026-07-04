using Octadock.Core.Models;

namespace Octadock.Core.Persistence;

/// <summary>Sort orders for clipboard-history queries.</summary>
public enum ClipboardClipSortOrder
{
    LastSeenNewestFirst = 0,
    CreatedNewestFirst,
    OldestFirst,
}

/// <summary>
/// Optional filters for querying persisted clipboard clips. An empty filter
/// returns recent, non-deleted clips ordered by most recent clipboard activity.
/// </summary>
public sealed record ClipboardClipFilter
{
    /// <summary>Restrict to these clip kinds (null/empty = all kinds).</summary>
    public IReadOnlyCollection<ClipboardClipKind>? Kinds { get; init; }

    /// <summary>Only clips first recorded at or after this instant.</summary>
    public DateTimeOffset? CreatedAfter { get; init; }

    /// <summary>Only clips first recorded at or before this instant.</summary>
    public DateTimeOffset? CreatedBefore { get; init; }

    /// <summary>Only clips last observed at or after this instant.</summary>
    public DateTimeOffset? LastSeenAfter { get; init; }

    /// <summary>Only clips last observed at or before this instant.</summary>
    public DateTimeOffset? LastSeenBefore { get; init; }

    /// <summary>Free-text match against clip text, source process/window, or format.</summary>
    public string? SearchText { get; init; }

    /// <summary>Include soft-deleted clips in the result.</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>Restrict to favorited or non-favorited clips when set.</summary>
    public bool? IsFavorite { get; init; }

    public ClipboardClipSortOrder SortOrder { get; init; } = ClipboardClipSortOrder.LastSeenNewestFirst;

    /// <summary>Max rows to return (paging).</summary>
    public int Limit { get; init; } = 200;

    /// <summary>Rows to skip (paging).</summary>
    public int Offset { get; init; }

    public static readonly ClipboardClipFilter RecentDefault = new();
}

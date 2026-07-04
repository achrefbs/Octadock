using Octadock.Core.Models;

namespace Octadock.Core.Persistence;

/// <summary>Sort orders for AI session queries.</summary>
public enum AiSessionSortOrder
{
    MostRecentActivityFirst = 0,
    OldestStartedFirst,
}

/// <summary>Optional filters for listing tracked AI sessions.</summary>
public sealed record AiSessionFilter
{
    /// <summary>Restrict to these providers (null/empty = all providers).</summary>
    public IReadOnlyCollection<AiSessionProvider>? Providers { get; init; }

    /// <summary>Restrict to these lifecycle states (null/empty = all statuses).</summary>
    public IReadOnlyCollection<AiSessionStatus>? Statuses { get; init; }

    /// <summary>Only sessions started at or after this instant.</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>Only sessions started at or before this instant.</summary>
    public DateTimeOffset? StartedBefore { get; init; }

    /// <summary>Free-text match against title, cwd, git branch, or command.</summary>
    public string? SearchText { get; init; }

    public AiSessionSortOrder SortOrder { get; init; } = AiSessionSortOrder.MostRecentActivityFirst;

    /// <summary>Max rows to return (paging).</summary>
    public int Limit { get; init; } = 200;

    /// <summary>Rows to skip (paging).</summary>
    public int Offset { get; init; }

    public static readonly AiSessionFilter RecentDefault = new();
}

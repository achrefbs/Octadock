namespace Octadock.Core.Models;

/// <summary>Persistable clipboard payload categories Octadock can restore later.</summary>
public enum ClipboardClipKind
{
    Unknown = 0,
    Text,
    Image,
}

/// <summary>
/// Durable metadata and content pointer for one clipboard-history item. Text
/// clips can store their plain text inline; image clips point at managed,
/// root-relative files owned by the clipboard-history feature.
/// </summary>
public sealed record ClipboardClipRecord
{
    public required Guid Id { get; init; }

    public required ClipboardClipKind Kind { get; init; }

    /// <summary>The first time this payload was recorded by Octadock.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The most recent time this payload was observed on the clipboard.</summary>
    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>Number of times this payload has been observed. Starts at 1.</summary>
    public int SeenCount { get; init; } = 1;

    /// <summary>Optional foreground process provenance captured with the clip.</summary>
    public string? SourceProcess { get; init; }

    /// <summary>Optional foreground window title captured with the clip.</summary>
    public string? SourceWindow { get; init; }

    /// <summary>Primary clipboard format name, such as text/plain or image/png.</summary>
    public string? FormatName { get; init; }

    /// <summary>Plain-text content for text clips.</summary>
    public string? Text { get; init; }

    /// <summary>Root-relative managed image path for image clips.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Root-relative thumbnail path for image clips.</summary>
    public string? ThumbnailPath { get; init; }

    /// <summary>Stable content fingerprint for future de-duplication.</summary>
    public string? ContentHash { get; init; }

    /// <summary>Approximate payload byte size, when known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>User-marked favorite/starred state for future history UI.</summary>
    public bool IsFavorite { get; init; }

    /// <summary>When set, the clip is hidden from normal history and pending cleanup.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>Optional JSON for non-primary formats or capture diagnostics.</summary>
    public string? MetadataJson { get; init; }

    /// <summary>True when this record has been soft-deleted.</summary>
    public bool IsDeleted => DeletedAt is not null;
}

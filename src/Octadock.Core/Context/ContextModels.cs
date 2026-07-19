namespace Octadock.Core.Context;

/// <summary>
/// How a Context item's bytes are owned (WS10, Architecture-Now). A snapshot is copied
/// into Octadock's managed storage so it survives the source capture being discarded or
/// aged out by retention; a reference points at the original file (used above the
/// snapshot size threshold) and can be materialized on demand.
/// </summary>
public enum ContextOwnership
{
    /// <summary>The bytes were copied into managed context storage.</summary>
    Snapshot,

    /// <summary>The item references an external file, verified by hash, materialized on demand.</summary>
    Reference,
}

/// <summary>The kind of a derivative attached to a Context item.</summary>
public enum ContextDerivativeKind
{
    /// <summary>Recognized text (the stealth-leak surface — excluded with its item).</summary>
    Ocr,

    /// <summary>A generated preview thumbnail.</summary>
    Thumbnail,

    /// <summary>An annotation project (<c>.octadock</c>) layered over the item.</summary>
    Annotation,
}

/// <summary>A derivative (OCR text, thumbnail, annotation) attached to a Context item.</summary>
/// <param name="Kind">The derivative kind.</param>
/// <param name="StorageRelativePath">Where the derivative lives, relative to the context storage root.</param>
public sealed record ContextDerivative(ContextDerivativeKind Kind, string StorageRelativePath);

/// <summary>
/// A single member of a <see cref="ContextPackage"/>. Context is a persistent, privacy-safe
/// packaging surface, kept intentionally separate from the Capture Shelf, and is NOT AI.
/// </summary>
public sealed record ContextItem
{
    /// <summary>Stable id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Human-facing name (the original filename; never an absolute path).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Snapshot (copied) or Reference (external, verified).</summary>
    public required ContextOwnership Ownership { get; init; }

    /// <summary>For a snapshot: the primary file path relative to the context storage root.</summary>
    public string? StorageRelativePath { get; init; }

    /// <summary>
    /// Original absolute source path retained as local provenance for every item.
    /// It is the authoritative read source only when <see cref="Ownership"/> is
    /// <see cref="ContextOwnership.Reference"/> and is never exported as a path.
    /// </summary>
    public string? ReferenceSourcePath { get; init; }

    /// <summary>For a reference: the SHA-256 used to verify the external file before materializing.</summary>
    public string? ReferenceSha256 { get; init; }

    /// <summary>Size in bytes of the primary content.</summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Provenance only: the capture this item came from, if any. The item's bytes live in
    /// managed storage, so the item survives this capture being discarded (the link is set
    /// to null, never cascaded).
    /// </summary>
    public Guid? SourceCaptureId { get; init; }

    /// <summary>When the item was added to the package.</summary>
    public DateTimeOffset AddedAt { get; init; }

    /// <summary>OCR / thumbnail / annotation derivatives. Excluding the item excludes ALL of these.</summary>
    public IReadOnlyList<ContextDerivative> Derivatives { get; init; } = Array.Empty<ContextDerivative>();
}

/// <summary>
/// A named, persistent Context package: a curated set of items (with their derivatives) a
/// user prepares to hand off. Distinct from the just-captured Capture Shelf (WS10).
/// </summary>
public sealed record ContextPackage
{
    /// <summary>Stable id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The package name (titles the Context surface and the exported bundle).</summary>
    public required string Name { get; init; }

    /// <summary>User-authored local notes that travel with an explicit Context export.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>When the package was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The package's items, in the user's persisted order.</summary>
    public IReadOnlyList<ContextItem> Items { get; init; } = Array.Empty<ContextItem>();
}

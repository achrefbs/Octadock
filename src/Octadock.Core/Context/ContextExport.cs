using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Core.Context;

/// <summary>
/// The include/exclude choices for a Context export (WS10). Everything is included by
/// default; the user can exclude whole items or individual derivatives. The
/// <b>hard invariant</b> — excluding an item excludes ALL of its derivatives (OCR text is
/// the stealth leak) — is enforced here in <see cref="IncludesDerivative"/>, so no export
/// path can accidentally ship a derivative of an item the user removed.
/// </summary>
public sealed class ContextExportSelection
{
    private readonly HashSet<Guid> _excludedItems = new();
    private readonly HashSet<(Guid ItemId, ContextDerivativeKind Kind)> _excludedDerivatives = new();
    private readonly HashSet<Guid>? _reviewedItems;
    private readonly HashSet<Guid>? _includedItems;

    /// <summary>
    /// Creates a planner-only selection whose historical default is to include
    /// every item in the supplied package. Outbound App services require the
    /// explicit reviewed snapshot created by <see cref="FromReviewedItems"/>.
    /// </summary>
    public ContextExportSelection()
    {
    }

    private ContextExportSelection(HashSet<Guid> reviewedItems, HashSet<Guid> includedItems)
    {
        _reviewedItems = reviewedItems;
        _includedItems = includedItems;
    }

    /// <summary>
    /// Captures the exact set the user reviewed and the positive subset they
    /// chose to export. A later package mutation therefore fails closed instead
    /// of silently exporting an item that never appeared in the review.
    /// </summary>
    public static ContextExportSelection FromReviewedItems(
        IEnumerable<Guid> reviewedItemIds,
        IEnumerable<Guid> includedItemIds)
    {
        ArgumentNullException.ThrowIfNull(reviewedItemIds);
        ArgumentNullException.ThrowIfNull(includedItemIds);

        Guid[] reviewed = reviewedItemIds.ToArray();
        Guid[] included = includedItemIds.ToArray();
        var reviewedSet = reviewed.ToHashSet();
        var includedSet = included.ToHashSet();
        if (reviewedSet.Count != reviewed.Length)
        {
            throw new ArgumentException("The reviewed Context item set contains duplicate ids.", nameof(reviewedItemIds));
        }

        if (includedSet.Count != included.Length)
        {
            throw new ArgumentException("The included Context item set contains duplicate ids.", nameof(includedItemIds));
        }

        if (!includedSet.IsSubsetOf(reviewedSet))
        {
            throw new ArgumentException("Included Context items must be part of the reviewed item set.", nameof(includedItemIds));
        }

        return new ContextExportSelection(reviewedSet, includedSet);
    }

    /// <summary>Excludes a whole item (and, by invariant, every derivative of it).</summary>
    public ContextExportSelection ExcludeItem(Guid itemId)
    {
        if (_includedItems is not null)
        {
            _includedItems.Remove(itemId);
        }
        else
        {
            _excludedItems.Add(itemId);
        }

        return this;
    }

    /// <summary>Excludes a single derivative of an item.</summary>
    public ContextExportSelection ExcludeDerivative(Guid itemId, ContextDerivativeKind kind)
    {
        _excludedDerivatives.Add((itemId, kind));
        return this;
    }

    /// <summary>True when the item is part of the export.</summary>
    public bool IncludesItem(Guid itemId)
        => _includedItems?.Contains(itemId) ?? !_excludedItems.Contains(itemId);

    /// <summary>
    /// Verifies that the persisted package still has exactly the item ids shown
    /// during review. This is required at the outbound service boundary.
    /// </summary>
    public void ValidateReviewedSnapshot(IEnumerable<Guid> currentItemIds)
    {
        ArgumentNullException.ThrowIfNull(currentItemIds);
        if (_reviewedItems is null)
        {
            throw new InvalidOperationException(
                "Context export requires an explicit reviewed item selection. Reload Context and review the export again.");
        }

        Guid[] current = currentItemIds.ToArray();
        if (current.Length != current.ToHashSet().Count || !_reviewedItems.SetEquals(current))
        {
            throw new InvalidOperationException(
                "Context changed after the export was reviewed. Reload Context and review the exact item selection again.");
        }
    }

    /// <summary>
    /// True only when the item is included AND the derivative isn't individually excluded —
    /// so excluding an item automatically excludes every derivative of it.
    /// </summary>
    public bool IncludesDerivative(Guid itemId, ContextDerivativeKind kind)
        => IncludesItem(itemId) && !_excludedDerivatives.Contains((itemId, kind));
}

/// <summary>Where the bytes for an export entry come from.</summary>
public enum ContextExportSource
{
    /// <summary>A file already copied into managed context storage (relative path).</summary>
    ManagedStorage,

    /// <summary>An external referenced original (absolute path), materialized during export.</summary>
    ReferenceOriginal,
}

/// <summary>One file to write into the exported package.</summary>
/// <param name="PackagePath">Relative path inside the exported package (forward slashes, never absolute).</param>
/// <param name="Source">Where to read the bytes from.</param>
/// <param name="SourceLocation">Managed-relative path or the referenced original's absolute path.</param>
/// <param name="ItemId">The owning item.</param>
/// <param name="Derivative">The derivative kind, or null for the item's primary content.</param>
public sealed record ContextExportEntry(
    string PackagePath,
    ContextExportSource Source,
    string SourceLocation,
    Guid ItemId,
    ContextDerivativeKind? Derivative)
{
    /// <summary>The captured source size used to validate a referenced original.</summary>
    public long? ExpectedSizeBytes { get; init; }

    /// <summary>The captured source SHA-256 used to validate a referenced original.</summary>
    public string? ExpectedSha256 { get; init; }
}

/// <summary>The full plan for an export: the physical entries plus the manifest to write at the root.</summary>
public sealed record ContextExportPlan(
    IReadOnlyList<ContextExportEntry> Entries,
    string ManifestJson,
    string ManifestPackagePath);

/// <summary>
/// Turns a <see cref="ContextPackage"/> + a <see cref="ContextExportSelection"/> into a
/// pure, IO-free export plan (WS10): the relative-pathed entries to copy and a manifest
/// that records provenance without leaking absolute source paths. The App layer does the
/// physical copy/zip (through SafeFileWriter). Relative paths are the default; excluding an
/// item excludes all its derivatives.
/// </summary>
public static class ContextExporter
{
    /// <summary>The manifest file name at the package root.</summary>
    public const string ManifestFileName = "context-manifest.json";

    private const int Schema = 1;
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Builds the export plan, honoring the selection and the derivative invariant.</summary>
    public static ContextExportPlan BuildPlan(ContextPackage package, ContextExportSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        selection ??= new ContextExportSelection();

        var entries = new List<ContextExportEntry>();
        var manifestItems = new List<ManifestItem>();

        foreach (ContextItem item in package.Items)
        {
            if (!selection.IncludesItem(item.Id))
            {
                continue; // item excluded → item + ALL derivatives excluded (the hard invariant)
            }

            string primaryPackagePath = PrimaryPackagePath(item);
            (ContextExportSource source, string location) = PrimarySource(item);
            entries.Add(new ContextExportEntry(
                primaryPackagePath,
                source,
                location,
                item.Id,
                Derivative: null)
            {
                ExpectedSizeBytes = source == ContextExportSource.ReferenceOriginal ? item.SizeBytes : null,
                ExpectedSha256 = source == ContextExportSource.ReferenceOriginal ? item.ReferenceSha256 : null,
            });

            var manifestDerivatives = new List<ManifestDerivative>();
            foreach (ContextDerivative derivative in item.Derivatives)
            {
                if (!selection.IncludesDerivative(item.Id, derivative.Kind))
                {
                    continue;
                }

                string derivativePackagePath = DerivativePackagePath(item, derivative);
                entries.Add(new ContextExportEntry(
                    derivativePackagePath,
                    ContextExportSource.ManagedStorage,
                    derivative.StorageRelativePath,
                    item.Id,
                    derivative.Kind));
                manifestDerivatives.Add(new ManifestDerivative(derivative.Kind.ToString().ToLowerInvariant(), derivativePackagePath));
            }

            manifestItems.Add(new ManifestItem(
                item.Id.ToString(),
                item.DisplayName,
                primaryPackagePath,
                item.Ownership.ToString().ToLowerInvariant(),
                item.SizeBytes,
                item.ReferenceSha256,
                manifestDerivatives));
        }

        var manifest = new Manifest(Schema, package.Name, package.Notes, package.CreatedAt, manifestItems);
        string manifestJson = JsonSerializer.Serialize(manifest, Json);
        return new ContextExportPlan(entries, manifestJson, ManifestFileName);
    }

    private static (ContextExportSource Source, string Location) PrimarySource(ContextItem item)
        => item.Ownership == ContextOwnership.Reference
            ? (ContextExportSource.ReferenceOriginal, item.ReferenceSourcePath ?? string.Empty)
            : (ContextExportSource.ManagedStorage, item.StorageRelativePath ?? string.Empty);

    private static string PrimaryPackagePath(ContextItem item)
    {
        string name = SanitizeFileName(item.DisplayName);
        if (string.IsNullOrEmpty(name))
        {
            name = "content";
        }

        return $"items/{item.Id:N}/{name}";
    }

    private static string DerivativePackagePath(ContextItem item, ContextDerivative derivative)
    {
        string ext = Path.GetExtension(derivative.StorageRelativePath);
        string kind = derivative.Kind.ToString().ToLowerInvariant();
        return $"items/{item.Id:N}/{kind}{ext}";
    }

    /// <summary>Strips any directory components and invalid characters so a name can't escape the package.</summary>
    private static string SanitizeFileName(string name)
    {
        string bare = Path.GetFileName(name.Replace('\\', '/'));
        var sb = new StringBuilder(bare.Length);
        foreach (char c in bare)
        {
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }

        string cleaned = sb.ToString().Trim();
        return cleaned is "" or "." or ".." ? string.Empty : cleaned;
    }

    private sealed record Manifest(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("notes")] string Notes,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("items")] IReadOnlyList<ManifestItem> Items);

    private sealed record ManifestItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("ownership")] string Ownership,
        [property: JsonPropertyName("sizeBytes")] long SizeBytes,
        [property: JsonPropertyName("sourceSha256")] string? SourceSha256,
        [property: JsonPropertyName("derivatives")] IReadOnlyList<ManifestDerivative> Derivatives);

    private sealed record ManifestDerivative(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("path")] string Path);
}

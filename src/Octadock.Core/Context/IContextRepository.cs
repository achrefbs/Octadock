namespace Octadock.Core.Context;

/// <summary>
/// Persistence for Context packages and their items (WS10). Additive and independent of
/// the capture store, so a Context failure can never take captures down; items reference
/// their source capture for provenance only and survive it being discarded.
/// </summary>
public interface IContextRepository
{
    /// <summary>Creates a new, empty package.</summary>
    Task<ContextPackage> CreatePackageAsync(string name, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Renames a package.</summary>
    Task RenamePackageAsync(Guid packageId, string name, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Replaces a package's user-authored notes.</summary>
    Task UpdatePackageNotesAsync(Guid packageId, string notes, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Deletes a package and all of its items/derivatives.</summary>
    Task DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default);

    /// <summary>Loads every package with its items and their derivatives, newest package first.</summary>
    Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads one package with its items and derivatives, or null.</summary>
    Task<ContextPackage?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default);

    /// <summary>Adds an item (and its derivatives) to a package.</summary>
    Task AddItemAsync(Guid packageId, ContextItem item, CancellationToken cancellationToken = default);

    /// <summary>Removes an item (and its derivatives) from its package.</summary>
    Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists an exact order for every item currently in the package. Implementations
    /// fail closed when the supplied ids are missing, duplicated, or stale.
    /// </summary>
    Task ReorderItemsAsync(
        Guid packageId,
        IReadOnlyList<Guid> orderedItemIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

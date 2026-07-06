using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Context;
using Octadock.Core.Io;
using Octadock.Core.Licensing;
using Octadock.Core.Models;

namespace Octadock.App.Services;

/// <summary>
/// The Context surface's application service (WS10): a persistent, privacy-safe packaging
/// facade over <see cref="IContextRepository"/> and managed storage, kept separate from the
/// Capture Shelf and NOT AI. Adding content snapshots small files into managed storage (so
/// items survive the source being discarded) and references large ones; export assembles a
/// relative-pathed zip through <see cref="ContextExporter"/> + <see cref="ContextZipWriter"/>,
/// written atomically via <see cref="ISafeFileWriter"/>. Creating/adding is gated post-trial;
/// viewing and exporting existing packages never is.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ContextService
{
    private const string ContextFolder = "Context";

    private readonly IContextRepository _repository;
    private readonly IStoragePaths _paths;
    private readonly ISafeFileWriter _safeWriter;
    private readonly ILicenseGate _licenseGate;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly ILogger<ContextService> _logger;

    public ContextService(
        IContextRepository repository,
        IStoragePaths paths,
        ISafeFileWriter safeWriter,
        ILicenseGate licenseGate,
        INotificationService notifications,
        IClock clock,
        ILogger<ContextService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _safeWriter = safeWriter ?? throw new ArgumentNullException(nameof(safeWriter));
        _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Loads all packages with their items (view — never gated).</summary>
    public Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken cancellationToken = default)
        => _repository.GetPackagesAsync(cancellationToken);

    /// <summary>Loads one package (view — never gated).</summary>
    public Task<ContextPackage?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        => _repository.GetPackageAsync(packageId, cancellationToken);

    /// <summary>Creates a new, empty package (gated: new activity).</summary>
    public async Task<ContextPackage?> CreatePackageAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_licenseGate.Allow(GatedFeature.Context))
        {
            return null;
        }

        return await _repository.CreatePackageAsync(name, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a package (managing your own data — allowed).</summary>
    public Task DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        => _repository.DeletePackageAsync(packageId, cancellationToken);

    /// <summary>Removes an item (managing your own data — allowed).</summary>
    public Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        => _repository.RemoveItemAsync(itemId, cancellationToken);

    /// <summary>Adds an external file to a package (gated: new activity). Rejects UNC paths.</summary>
    public async Task<bool> AddFileAsync(Guid packageId, string filePath, CancellationToken cancellationToken = default)
    {
        if (PathSafety.IsUncPath(filePath))
        {
            _notifications.Notify("Context", "Network (UNC) paths aren't allowed. Copy the file locally first.", NotificationKind.Warning);
            return false;
        }

        if (!_licenseGate.Allow(GatedFeature.Context))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _notifications.Notify("Context", "The file could not be found.", NotificationKind.Warning);
            return false;
        }

        long size = new FileInfo(filePath).Length;
        Guid itemId = Guid.NewGuid();
        var item = ContextIngestPolicy.Decide(size) == ContextOwnership.Snapshot
            ? await SnapshotAsync(itemId, filePath, size, sourceCaptureId: null, cancellationToken).ConfigureAwait(false)
            : Reference(itemId, filePath, size, sourceCaptureId: null);

        await _repository.AddItemAsync(packageId, item, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Adds a capture (its image + thumbnail derivative) to a package (gated: new activity).</summary>
    public async Task<bool> AddCaptureAsync(Guid packageId, CaptureRecord capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_licenseGate.Allow(GatedFeature.Context))
        {
            return false;
        }

        string sourceImage = _paths.ToAbsolute(capture.OriginalPath);
        if (!File.Exists(sourceImage))
        {
            _notifications.Notify("Context", "That capture's image is no longer on disk.", NotificationKind.Warning);
            return false;
        }

        long size = new FileInfo(sourceImage).Length;
        Guid itemId = Guid.NewGuid();
        ContextItem item = await SnapshotAsync(itemId, sourceImage, size, capture.Id, cancellationToken).ConfigureAwait(false);

        // Copy the existing thumbnail as a snapshot derivative so it survives the capture.
        if (!string.IsNullOrWhiteSpace(capture.ThumbnailPath))
        {
            string thumbAbs = _paths.ToAbsolute(capture.ThumbnailPath);
            if (File.Exists(thumbAbs))
            {
                string thumbRel = ManagedRelative(itemId, "thumbnail" + Path.GetExtension(thumbAbs));
                await _safeWriter.CopyAsync(thumbAbs, _paths.ToAbsolute(thumbRel), cancellationToken).ConfigureAwait(false);
                item = item with
                {
                    Derivatives = new List<ContextDerivative> { new(ContextDerivativeKind.Thumbnail, thumbRel) },
                };
            }
        }

        await _repository.AddItemAsync(packageId, item, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Exports a package to a zip at <paramref name="destinationZipPath"/> honoring the
    /// selection (export existing data — never gated). Missing referenced originals are
    /// written as empty entries rather than failing the whole export.
    /// </summary>
    public async Task<bool> ExportAsync(
        Guid packageId,
        ContextExportSelection selection,
        string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        ContextPackage? package = await _repository.GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return false;
        }

        ContextExportPlan plan = ContextExporter.BuildPlan(package, selection);
        await _safeWriter.WriteAsync(
            destinationZipPath,
            (stream, _) =>
            {
                ContextZipWriter.Write(plan, ReadEntryBytes, stream);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        _notifications.Notify("Context exported", $"Saved '{package.Name}' to {Path.GetFileName(destinationZipPath)}.", NotificationKind.Success);
        return true;
    }

    private byte[] ReadEntryBytes(ContextExportEntry entry)
    {
        try
        {
            string path = entry.Source == ContextExportSource.ManagedStorage
                ? _paths.ToAbsolute(entry.SourceLocation)
                : entry.SourceLocation;
            return File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Context export could not read {Path}; writing an empty entry.", entry.PackagePath);
            return Array.Empty<byte>();
        }
    }

    private async Task<ContextItem> SnapshotAsync(
        Guid itemId, string sourcePath, long size, Guid? sourceCaptureId, CancellationToken cancellationToken)
    {
        string relative = ManagedRelative(itemId, Path.GetFileName(sourcePath));
        await _safeWriter.CopyAsync(sourcePath, _paths.ToAbsolute(relative), cancellationToken).ConfigureAwait(false);
        return new ContextItem
        {
            Id = itemId,
            DisplayName = Path.GetFileName(sourcePath),
            Ownership = ContextOwnership.Snapshot,
            StorageRelativePath = relative,
            SizeBytes = size,
            SourceCaptureId = sourceCaptureId,
            AddedAt = _clock.UtcNow,
        };
    }

    private ContextItem Reference(Guid itemId, string sourcePath, long size, Guid? sourceCaptureId) => new()
    {
        Id = itemId,
        DisplayName = Path.GetFileName(sourcePath),
        Ownership = ContextOwnership.Reference,
        ReferenceSourcePath = sourcePath,
        ReferenceSha256 = ComputeSha256(sourcePath),
        SizeBytes = size,
        SourceCaptureId = sourceCaptureId,
        AddedAt = _clock.UtcNow,
    };

    private static string ManagedRelative(Guid itemId, string fileName)
        => $"{ContextFolder}/{itemId:N}/{fileName}";

    private static string? ComputeSha256(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

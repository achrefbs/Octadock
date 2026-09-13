using System.Buffers;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Context;
using Octadock.Core.Io;
using Octadock.Core.Models;

namespace Octadock.App.Services;

/// <summary>
/// The Context surface's application service (WS10): a persistent, privacy-safe packaging
/// facade over <see cref="IContextRepository"/> and managed storage, kept separate from the
/// Capture Shelf and NOT AI. Adding content snapshots small files into managed storage (so
/// items survive the source being discarded) and references large ones; export assembles a
/// relative-pathed zip through <see cref="ContextExporter"/> + <see cref="ContextZipWriter"/>,
/// written atomically via <see cref="ISafeFileWriter"/>. Creating and adding items is always available;
/// viewing and exporting existing packages never is.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ContextService
{
    private const string ContextFolder = "Context";
    public const int MaxPackageNotesLength = 4000;

    private readonly IContextRepository _repository;
    private readonly IStoragePaths _paths;
    private readonly ISafeFileWriter _safeWriter;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly ILogger<ContextService> _logger;

    public ContextService(
        IContextRepository repository,
        IStoragePaths paths,
        ISafeFileWriter safeWriter,
        INotificationService notifications,
        IClock clock,
        ILogger<ContextService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _safeWriter = safeWriter ?? throw new ArgumentNullException(nameof(safeWriter));
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


        return await _repository.CreatePackageAsync(name, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Renames an existing package (managing local metadata — never gated).</summary>
    public Task RenamePackageAsync(Guid packageId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _repository.RenamePackageAsync(packageId, name.Trim(), _clock.UtcNow, cancellationToken);
    }

    /// <summary>Updates local package notes (managing local metadata — never gated).</summary>
    public Task UpdatePackageNotesAsync(
        Guid packageId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Length > MaxPackageNotesLength)
        {
            throw new ArgumentException(
                $"Context notes are limited to {MaxPackageNotesLength:N0} characters.",
                nameof(notes));
        }

        return _repository.UpdatePackageNotesAsync(packageId, notes.Trim(), _clock.UtcNow, cancellationToken);
    }

    /// <summary>Persists the exact user-selected item order (managing local metadata — never gated).</summary>
    public Task ReorderItemsAsync(
        Guid packageId,
        IReadOnlyList<Guid> orderedItemIds,
        CancellationToken cancellationToken = default)
        => _repository.ReorderItemsAsync(packageId, orderedItemIds, _clock.UtcNow, cancellationToken);

    /// <summary>Deletes a package and its managed snapshots (managing your own data — allowed).</summary>
    public async Task DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        ContextPackage? package = await _repository.GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false);
        await _repository.DeletePackageAsync(packageId, cancellationToken).ConfigureAwait(false);

        if (package is not null)
        {
            foreach (ContextItem item in package.Items)
            {
                TryDeleteManagedItemDirectory(item.Id, "package deletion");
            }
        }
    }

    /// <summary>Removes an item and its managed snapshots (managing your own data — allowed).</summary>
    public async Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await _repository.RemoveItemAsync(itemId, cancellationToken).ConfigureAwait(false);
        TryDeleteManagedItemDirectory(itemId, "item deletion");
    }

    /// <summary>Adds an external file to a package (gated: new activity). Rejects UNC paths.</summary>
    public async Task<bool> AddFileAsync(Guid packageId, string filePath, CancellationToken cancellationToken = default)
    {
        if (PathSafety.IsUncPath(filePath))
        {
            _notifications.Notify("Context", "Network (UNC) paths aren't allowed. Copy the file locally first.", NotificationKind.Warning);
            return false;
        }



        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _notifications.Notify("Context", "The file could not be found.", NotificationKind.Warning);
            return false;
        }

        Guid itemId = Guid.NewGuid();
        try
        {
            long size = new FileInfo(filePath).Length;
            ContextItem item = ContextIngestPolicy.Decide(size) == ContextOwnership.Snapshot
                ? await SnapshotAsync(itemId, filePath, size, sourceCaptureId: null, cancellationToken).ConfigureAwait(false)
                : await ReferenceAsync(itemId, filePath, size, sourceCaptureId: null, cancellationToken).ConfigureAwait(false);

            await _repository.AddItemAsync(packageId, item, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            TryDeleteManagedItemDirectory(itemId, "failed file add");
            throw;
        }
    }

    /// <summary>Adds a capture (its image + thumbnail derivative) to a package (gated: new activity).</summary>
    public async Task<bool> AddCaptureAsync(Guid packageId, CaptureRecord capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);


        string sourceImage = _paths.ToAbsolute(capture.OriginalPath);
        if (!File.Exists(sourceImage))
        {
            _notifications.Notify("Context", "That capture's image is no longer on disk.", NotificationKind.Warning);
            return false;
        }

        Guid itemId = Guid.NewGuid();
        try
        {
            long size = new FileInfo(sourceImage).Length;
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
        catch
        {
            TryDeleteManagedItemDirectory(itemId, "failed capture add");
            throw;
        }
    }

    /// <summary>
    /// Exports a package to a zip at <paramref name="destinationZipPath"/> honoring the
    /// selection (export existing data — never gated). Referenced originals must still
    /// match the size and SHA-256 recorded at ingest; otherwise the export fails without
    /// replacing an existing destination.
    /// </summary>
    public async Task<bool> ExportAsync(
        Guid packageId,
        ContextExportSelection selection,
        string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        ContextPackage? package = await _repository.GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return false;
        }

        selection.ValidateReviewedSnapshot(package.Items.Select(item => item.Id));
        ContextExportPlan plan = ContextExporter.BuildPlan(package, selection);
        await ValidateReferenceSourcesAsync(plan, cancellationToken).ConfigureAwait(false);
        await _safeWriter.WriteAsync(
            destinationZipPath,
            (stream, ct) => ContextZipWriter.WriteAsync(plan, CopyEntryToAsync, stream, ct),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Exports a package to a plain folder under <paramref name="destinationDirectory"/>
    /// (a subfolder named after the package), honoring the selection. Same relative
    /// layout and manifest as the zip export, just unpacked — the current default the
    /// UI offers so the result is a normal, browsable folder rather than an archive.
    /// The complete package is staged, then replaces the prior package directory so a
    /// re-export cannot retain files that the user excluded this time.
    /// </summary>
    public async Task<bool> ExportToFolderAsync(
        Guid packageId,
        ContextExportSelection selection,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        ContextPackage? package = await _repository.GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return false;
        }

        selection.ValidateReviewedSnapshot(package.Items.Select(item => item.Id));
        ContextExportPlan plan = ContextExporter.BuildPlan(package, selection);
        await ValidateReferenceSourcesAsync(plan, cancellationToken).ConfigureAwait(false);

        string exportRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(exportRoot);
        string root = ResolveExportTarget(exportRoot, SafeFolderName(package.Name));
        string stagingRoot = ResolveExportTarget(exportRoot, $".octadock-context-stage-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            foreach (ContextExportEntry entry in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = ResolveExportTarget(stagingRoot, entry.PackagePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(
                    target,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await CopyEntryToAsync(entry, output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string manifestTarget = ResolveExportTarget(stagingRoot, plan.ManifestPackagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestTarget)!);
            await File.WriteAllTextAsync(manifestTarget, plan.ManifestJson, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            ReplacePackageDirectory(stagingRoot, root, package.Name);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot, "unfinished folder export");
        }

        return true;
    }

    /// <summary>Resolves a forward-slashed package-relative path to an absolute path inside <paramref name="root"/>, refusing any that escapes it.</summary>
    private static string ResolveExportTarget(string root, string packagePath)
    {
        if (Path.IsPathRooted(packagePath))
        {
            throw new InvalidOperationException("Context export path must be relative to the package root.");
        }

        string relative = packagePath.Replace('/', Path.DirectorySeparatorChar);
        string rootFull = Path.GetFullPath(root);
        string full = Path.GetFullPath(Path.Combine(rootFull, relative));
        string relativeToRoot = Path.GetRelativePath(rootFull, full);
        if (Path.IsPathRooted(relativeToRoot) ||
            relativeToRoot == ".." ||
            relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Context export path escaped the package root.");
        }

        return full;
    }

    private static string SafeFolderName(string name)
    {
        name ??= string.Empty;
        string normalized = name.Replace('\\', '/');
        int lastSeparator = normalized.LastIndexOf('/');
        string bare = lastSeparator >= 0 ? normalized[(lastSeparator + 1)..] : normalized;

        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(bare.Length);
        foreach (char c in bare)
        {
            sb.Append(char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        if (cleaned is "" or "." or "..")
        {
            return "context";
        }

        if (IsReservedWindowsFileName(cleaned))
        {
            cleaned = "_" + cleaned;
        }

        const int MaxFolderNameLength = 120;
        if (cleaned.Length <= MaxFolderNameLength)
        {
            return cleaned;
        }

        int truncateAt = MaxFolderNameLength;
        if (char.IsHighSurrogate(cleaned[truncateAt - 1]) && char.IsLowSurrogate(cleaned[truncateAt]))
        {
            truncateAt--;
        }

        return cleaned[..truncateAt].TrimEnd('.', ' ');
    }

    private static async Task ValidateReferenceSourcesAsync(
        ContextExportPlan plan,
        CancellationToken cancellationToken)
    {
        foreach (ContextExportEntry entry in plan.Entries)
        {
            if (entry.Source != ContextExportSource.ReferenceOriginal)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = ResolveReferenceSource(entry);
            long expectedSize = GetExpectedReferenceSize(entry);
            byte[] expectedHash = GetExpectedReferenceHash(entry);

            FileStream input;
            try
            {
                input = OpenReadStream(sourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw ReferenceIntegrityFailure(entry, "is missing or cannot be read", ex);
            }

            await using (input.ConfigureAwait(false))
            {
                if (input.Length != expectedSize)
                {
                    throw ReferenceIntegrityFailure(entry, "has changed size since it was added");
                }

                (long bytesRead, byte[] actualHash) = await ReadAndHashAsync(input, destination: null, cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead != expectedSize || !CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                {
                    throw ReferenceIntegrityFailure(entry, "has changed since it was added");
                }
            }
        }
    }

    private async Task CopyEntryToAsync(
        ContextExportEntry entry,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (entry.Source == ContextExportSource.ReferenceOriginal)
        {
            await CopyReferenceToAsync(entry, destination, cancellationToken).ConfigureAwait(false);
            return;
        }

        string sourcePath = ResolveManagedSource(entry);
        FileStream input;
        try
        {
            input = OpenReadStream(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Managed Context item '{entry.PackagePath}' is missing or cannot be read. Remove the item and add it again before exporting.",
                ex);
        }

        await using (input.ConfigureAwait(false))
        {
            await input.CopyToAsync(destination, 128 * 1024, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CopyReferenceToAsync(
        ContextExportEntry entry,
        Stream destination,
        CancellationToken cancellationToken)
    {
        string sourcePath = ResolveReferenceSource(entry);
        long expectedSize = GetExpectedReferenceSize(entry);
        byte[] expectedHash = GetExpectedReferenceHash(entry);

        FileStream input;
        try
        {
            input = OpenReadStream(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw ReferenceIntegrityFailure(entry, "became unavailable during export", ex);
        }

        await using (input.ConfigureAwait(false))
        {
            if (input.Length != expectedSize)
            {
                throw ReferenceIntegrityFailure(entry, "changed size during export");
            }

            (long bytesRead, byte[] actualHash) = await ReadAndHashAsync(input, destination, cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead != expectedSize || !CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw ReferenceIntegrityFailure(entry, "changed during export");
            }
        }
    }

    private string ResolveManagedSource(ContextExportEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceLocation) || Path.IsPathRooted(entry.SourceLocation))
        {
            throw new InvalidOperationException($"Managed Context item '{entry.PackagePath}' has an invalid storage path.");
        }

        string itemRoot = ManagedItemDirectory(entry.ItemId);
        string sourcePath = _paths.ToAbsolute(entry.SourceLocation);
        string relativeToItem = Path.GetRelativePath(itemRoot, sourcePath);
        if (Path.IsPathRooted(relativeToItem) ||
            relativeToItem == "." ||
            relativeToItem == ".." ||
            relativeToItem.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Managed Context item '{entry.PackagePath}' escaped its storage directory.");
        }

        return sourcePath;
    }

    private static string ResolveReferenceSource(ContextExportEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceLocation))
        {
            throw ReferenceIntegrityFailure(entry, "has no recorded source path");
        }

        if (PathSafety.IsUncPath(entry.SourceLocation))
        {
            throw ReferenceIntegrityFailure(entry, "points to a network path, which is not allowed");
        }

        try
        {
            string fullPath = Path.GetFullPath(entry.SourceLocation);
            if (!File.Exists(fullPath))
            {
                throw ReferenceIntegrityFailure(entry, "is missing");
            }

            return fullPath;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            throw ReferenceIntegrityFailure(entry, "has an invalid or unreadable source path", ex);
        }
    }

    private static long GetExpectedReferenceSize(ContextExportEntry entry)
    {
        if (entry.ExpectedSizeBytes is not long expectedSize || expectedSize < 0)
        {
            throw ReferenceIntegrityFailure(entry, "has no valid recorded size");
        }

        return expectedSize;
    }

    private static byte[] GetExpectedReferenceHash(ContextExportEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ExpectedSha256))
        {
            throw ReferenceIntegrityFailure(entry, "has no recorded SHA-256 hash");
        }

        try
        {
            byte[] hash = Convert.FromHexString(entry.ExpectedSha256);
            if (hash.Length != SHA256.HashSizeInBytes)
            {
                throw ReferenceIntegrityFailure(entry, "has an invalid recorded SHA-256 hash");
            }

            return hash;
        }
        catch (FormatException ex)
        {
            throw ReferenceIntegrityFailure(entry, "has an invalid recorded SHA-256 hash", ex);
        }
    }

    private static InvalidOperationException ReferenceIntegrityFailure(
        ContextExportEntry entry,
        string reason,
        Exception? innerException = null)
        => new(
            $"Referenced Context item '{entry.PackagePath}' {reason}. Remove it and add the source again before exporting.",
            innerException);

    private static FileStream OpenReadStream(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task<(long BytesRead, byte[] Hash)> ReadAndHashAsync(
        Stream source,
        Stream? destination,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long bytesRead = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return (bytesRead, hash.GetHashAndReset());
                }

                hash.AppendData(buffer, 0, read);
                bytesRead += read;
                if (destination is not null)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsReservedWindowsFileName(string name)
    {
        string stem = name.Split('.', 2)[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               stem[3] is >= '1' and <= '9';
    }

    private void ReplacePackageDirectory(string stagingRoot, string destinationRoot, string packageName)
    {
        if (File.Exists(destinationRoot))
        {
            throw new IOException($"Context export destination '{destinationRoot}' is an existing file.");
        }

        if (Directory.Exists(destinationRoot))
        {
            EnsureExistingContextExport(destinationRoot, packageName);
        }

        string parent = Path.GetDirectoryName(destinationRoot)
            ?? throw new InvalidOperationException("Context export destination has no parent directory.");
        string backupRoot = ResolveExportTarget(parent, $".octadock-context-backup-{Guid.NewGuid():N}");
        bool movedExisting = false;

        try
        {
            if (Directory.Exists(destinationRoot))
            {
                Directory.Move(destinationRoot, backupRoot);
                movedExisting = true;
            }

            Directory.Move(stagingRoot, destinationRoot);
        }
        catch
        {
            if (movedExisting && !Directory.Exists(destinationRoot) && Directory.Exists(backupRoot))
            {
                try
                {
                    Directory.Move(backupRoot, destinationRoot);
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError(rollbackException, "Context folder export could not restore {Destination} after replacement failed.", destinationRoot);
                }
            }

            throw;
        }

        if (movedExisting)
        {
            TryDeleteDirectory(backupRoot, "completed folder export backup");
        }
    }

    private static void EnsureExistingContextExport(string destinationRoot, string packageName)
    {
        string manifestPath = ResolveExportTarget(destinationRoot, ContextExporter.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"The destination folder '{destinationRoot}' already exists but is not an Octadock Context export. Choose another folder to avoid replacing unrelated files.");
        }

        try
        {
            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length > 16L * 1024 * 1024)
            {
                throw new InvalidOperationException("The existing Context manifest is unexpectedly large.");
            }

            using FileStream manifest = File.OpenRead(manifestPath);
            using JsonDocument document = JsonDocument.Parse(manifest);
            JsonElement root = document.RootElement;
            bool matches = root.TryGetProperty("schema", out JsonElement schema) &&
                           schema.TryGetInt32(out int schemaValue) &&
                           schemaValue == 1 &&
                           root.TryGetProperty("name", out JsonElement name) &&
                           name.ValueKind == JsonValueKind.String &&
                           string.Equals(name.GetString(), packageName, StringComparison.Ordinal);
            if (!matches)
            {
                throw new InvalidOperationException("The existing Context manifest belongs to a different package or schema.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"The destination folder '{destinationRoot}' is not a replaceable export of this Context package. Choose another folder to avoid replacing unrelated files.",
                ex);
        }
    }

    private string ManagedItemDirectory(Guid itemId)
        => Path.Combine(_paths.RootDirectory, ContextFolder, itemId.ToString("N"));

    private void TryDeleteManagedItemDirectory(Guid itemId, string operation)
        => TryDeleteDirectory(ManagedItemDirectory(itemId), operation);

    private void TryDeleteDirectory(string path, string operation)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Context could not clean up {Path} after {Operation}.", path, operation);
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
            ReferenceSourcePath = Path.GetFullPath(sourcePath),
            SizeBytes = size,
            SourceCaptureId = sourceCaptureId,
            AddedAt = _clock.UtcNow,
        };
    }

    private async Task<ContextItem> ReferenceAsync(
        Guid itemId,
        string sourcePath,
        long size,
        Guid? sourceCaptureId,
        CancellationToken cancellationToken)
    {
        await using FileStream input = OpenReadStream(sourcePath);
        if (input.Length != size)
        {
            throw new IOException("The referenced file changed while it was being added to Context.");
        }

        (long bytesRead, byte[] hash) = await ReadAndHashAsync(input, destination: null, cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead != size)
        {
            throw new IOException("The referenced file changed while it was being added to Context.");
        }

        return new ContextItem
        {
            Id = itemId,
            DisplayName = Path.GetFileName(sourcePath),
            Ownership = ContextOwnership.Reference,
            ReferenceSourcePath = Path.GetFullPath(sourcePath),
            ReferenceSha256 = Convert.ToHexString(hash).ToLowerInvariant(),
            SizeBytes = size,
            SourceCaptureId = sourceCaptureId,
            AddedAt = _clock.UtcNow,
        };
    }

    private static string ManagedRelative(Guid itemId, string fileName)
        => $"{ContextFolder}/{itemId:N}/{fileName}";
}

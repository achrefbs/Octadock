using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.App.Tests.Fakes;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Context;
using Octadock.Core.Io;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.App.Tests.Context;

/// <summary>
/// End-to-end Context value chain (WS10): ingesting a file snapshots its bytes into managed
/// storage, and export assembles a relative-pathed zip through SafeFileWriter.
/// </summary>
public sealed class ContextServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"octadock-ctx-{Guid.NewGuid():N}");

    private sealed class InMemoryContextRepository : IContextRepository
    {
        private readonly Dictionary<Guid, ContextPackage> _packages = new();

        public Exception? AddItemFailure { get; set; }

        public Task<ContextPackage> CreatePackageAsync(string name, DateTimeOffset now, CancellationToken ct = default)
        {
            var package = new ContextPackage { Id = Guid.NewGuid(), Name = name, CreatedAt = now };
            _packages[package.Id] = package;
            return Task.FromResult(package);
        }

        public Task RenamePackageAsync(Guid id, string name, DateTimeOffset now, CancellationToken ct = default)
        {
            if (_packages.TryGetValue(id, out ContextPackage? p))
            {
                _packages[id] = p with { Name = name };
            }

            return Task.CompletedTask;
        }

        public Task UpdatePackageNotesAsync(Guid id, string notes, DateTimeOffset now, CancellationToken ct = default)
        {
            if (_packages.TryGetValue(id, out ContextPackage? package))
            {
                _packages[id] = package with { Notes = notes };
            }

            return Task.CompletedTask;
        }

        public Task DeletePackageAsync(Guid id, CancellationToken ct = default)
        {
            _packages.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContextPackage>>(_packages.Values.ToList());

        public Task<ContextPackage?> GetPackageAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_packages.GetValueOrDefault(id));

        public Task AddItemAsync(Guid packageId, ContextItem item, CancellationToken ct = default)
        {
            if (AddItemFailure is not null)
            {
                return Task.FromException(AddItemFailure);
            }

            ContextPackage p = _packages[packageId];
            _packages[packageId] = p with { Items = p.Items.Append(item).ToList() };
            return Task.CompletedTask;
        }

        public Task RemoveItemAsync(Guid itemId, CancellationToken ct = default)
        {
            foreach (Guid key in _packages.Keys.ToList())
            {
                ContextPackage p = _packages[key];
                _packages[key] = p with { Items = p.Items.Where(i => i.Id != itemId).ToList() };
            }

            return Task.CompletedTask;
        }

        public Task ReorderItemsAsync(
            Guid packageId,
            IReadOnlyList<Guid> orderedItemIds,
            DateTimeOffset now,
            CancellationToken ct = default)
        {
            ContextPackage package = _packages[packageId];
            Dictionary<Guid, ContextItem> items = package.Items.ToDictionary(item => item.Id);
            _packages[packageId] = package with
            {
                Items = orderedItemIds.Select(id => items[id]).ToList(),
            };
            return Task.CompletedTask;
        }
    }

    private sealed class NoopNotifications : INotificationService
    {
        public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info, Action? clickAction = null) { }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset LocalNow => UtcNow;
    }

    private ContextService BuildService(InMemoryContextRepository? repository = null)
    {
        var paths = new StoragePaths(_root);
        paths.EnsureDirectories();
        var safeWriter = new SafeFileWriter(new FileRevisionStore(Path.Combine(_root, "revisions")));
        return new ContextService(
            repository ?? new InMemoryContextRepository(),
            paths,
            safeWriter,
            new AllowAllLicenseGate(),
            new NoopNotifications(),
            new FixedClock(),
            NullLogger<ContextService>.Instance);
    }

    private static async Task<ContextExportSelection> ReviewAllAsync(
        ContextService service,
        Guid packageId)
    {
        ContextPackage package = (await service.GetPackageAsync(packageId))!;
        Guid[] itemIds = package.Items.Select(item => item.Id).ToArray();
        return ContextExportSelection.FromReviewedItems(itemIds, itemIds);
    }

    private static ContextItem ReferenceItem(string sourcePath, byte[] originalBytes) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = Path.GetFileName(sourcePath),
        Ownership = ContextOwnership.Reference,
        ReferenceSourcePath = sourcePath,
        ReferenceSha256 = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant(),
        SizeBytes = originalBytes.LongLength,
        AddedAt = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task Ingesting_a_file_snapshots_it_and_export_packages_it_into_a_zip()
    {
        ContextService service = BuildService();
        string source = Path.Combine(_root, "hello.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(source, "hello world");

        ContextPackage package = (await service.CreatePackageAsync("Repro"))!;
        (await service.AddFileAsync(package.Id, source)).Should().BeTrue();
        ContextPackage stored = (await service.GetPackageAsync(package.Id))!;
        stored.Items.Should().ContainSingle().Which.ReferenceSourcePath
            .Should().Be(Path.GetFullPath(source), "snapshot items retain local source provenance");

        string zipPath = Path.Combine(_root, "out.zip");
        (await service.ExportAsync(package.Id, await ReviewAllAsync(service, package.Id), zipPath)).Should().BeTrue();

        File.Exists(zipPath).Should().BeTrue();
        using var archive = ZipFile.OpenRead(zipPath);
        archive.Entries.Should().Contain(e => e.FullName == "context-manifest.json");
        ZipArchiveEntry item = archive.Entries.Should().ContainSingle(e => e.FullName.EndsWith("hello.txt", StringComparison.Ordinal)).Subject;
        using var reader = new StreamReader(item.Open(), Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be("hello world", "the snapshotted bytes are what get exported");
    }

    [Fact]
    public async Task ExportToFolder_writes_a_normal_browsable_folder()
    {
        ContextService service = BuildService();
        string source = Path.Combine(_root, "notes.md");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(source, "# Notes");

        ContextPackage package = (await service.CreatePackageAsync("Launch Review"))!;
        (await service.AddFileAsync(package.Id, source)).Should().BeTrue();

        string exportRoot = Path.Combine(_root, "exports");
        (await service.ExportToFolderAsync(
            package.Id,
            await ReviewAllAsync(service, package.Id),
            exportRoot)).Should().BeTrue();

        string packageRoot = Path.Combine(exportRoot, "Launch Review");
        Directory.Exists(packageRoot).Should().BeTrue();
        File.Exists(Path.Combine(packageRoot, "context-manifest.json")).Should().BeTrue();
        string exported = Directory.GetFiles(packageRoot, "notes.md", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(exported)).Should().Be("# Notes");
    }

    [Fact]
    public async Task Export_fails_closed_when_an_unreviewed_item_is_added_after_the_ui_snapshot()
    {
        ContextService service = BuildService();
        ContextPackage package = (await service.CreatePackageAsync("Stale review"))!;
        string reviewedSource = Path.Combine(_root, "reviewed.txt");
        string unseenSource = Path.Combine(_root, "unseen.txt");
        await File.WriteAllTextAsync(reviewedSource, "reviewed");
        await File.WriteAllTextAsync(unseenSource, "unseen");
        (await service.AddFileAsync(package.Id, reviewedSource)).Should().BeTrue();
        ContextExportSelection reviewedSelection = await ReviewAllAsync(service, package.Id);

        (await service.AddFileAsync(package.Id, unseenSource)).Should().BeTrue();
        string zipPath = Path.Combine(_root, "stale-review.zip");
        Func<Task> export = () => service.ExportAsync(package.Id, reviewedSelection, zipPath);

        InvalidOperationException exception = (await export.Should()
            .ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("changed after the export was reviewed");
        File.Exists(zipPath).Should().BeFalse();
    }

    [Fact]
    public async Task Export_rejects_a_missing_reference_without_creating_the_destination()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Missing reference"))!;
        string missingPath = Path.Combine(_root, "missing.bin");
        await repository.AddItemAsync(package.Id, ReferenceItem(missingPath, Encoding.UTF8.GetBytes("missing")));

        string zipPath = Path.Combine(_root, "missing.zip");
        ContextExportSelection selection = await ReviewAllAsync(service, package.Id);
        Func<Task> export = () => service.ExportAsync(package.Id, selection, zipPath);

        InvalidOperationException exception = (await export.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("missing").And.Contain("add the source again");
        File.Exists(zipPath).Should().BeFalse("an invalid reference must fail before publication");
    }

    [Fact]
    public async Task Export_rejects_a_same_size_changed_reference_and_preserves_an_existing_destination()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Changed reference"))!;
        string sourcePath = Path.Combine(_root, "reference.txt");
        byte[] original = Encoding.UTF8.GetBytes("before");
        await File.WriteAllBytesAsync(sourcePath, original);
        await repository.AddItemAsync(package.Id, ReferenceItem(sourcePath, original));
        await File.WriteAllTextAsync(sourcePath, "after!");

        string zipPath = Path.Combine(_root, "existing.zip");
        await File.WriteAllTextAsync(zipPath, "keep this destination");
        ContextExportSelection selection = await ReviewAllAsync(service, package.Id);
        Func<Task> export = () => service.ExportAsync(package.Id, selection, zipPath);

        InvalidOperationException exception = (await export.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("changed");
        (await File.ReadAllTextAsync(zipPath)).Should().Be("keep this destination");
    }

    [Fact]
    public async Task Export_rejects_a_reference_whose_size_changed()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Resized reference"))!;
        string sourcePath = Path.Combine(_root, "resized.txt");
        byte[] original = Encoding.UTF8.GetBytes("original");
        await File.WriteAllBytesAsync(sourcePath, original);
        await repository.AddItemAsync(package.Id, ReferenceItem(sourcePath, original));
        await File.AppendAllTextAsync(sourcePath, " changed");

        string zipPath = Path.Combine(_root, "resized.zip");
        ContextExportSelection selection = await ReviewAllAsync(service, package.Id);
        Func<Task> export = () => service.ExportAsync(package.Id, selection, zipPath);

        InvalidOperationException exception = (await export.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("changed size");
        File.Exists(zipPath).Should().BeFalse();
    }

    [Fact]
    public async Task Export_streams_a_valid_reference_and_preserves_its_bytes()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Large reference"))!;
        string sourcePath = Path.Combine(_root, "large.bin");
        byte[] payload = RandomNumberGenerator.GetBytes((2 * 1024 * 1024) + 17);
        await File.WriteAllBytesAsync(sourcePath, payload);
        await repository.AddItemAsync(package.Id, ReferenceItem(sourcePath, payload));

        string zipPath = Path.Combine(_root, "reference.zip");
        (await service.ExportAsync(package.Id, await ReviewAllAsync(service, package.Id), zipPath)).Should().BeTrue();

        using var archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry item = archive.Entries.Should().ContainSingle(e => e.FullName.EndsWith("large.bin", StringComparison.Ordinal)).Subject;
        await using Stream input = item.Open();
        using var exported = new MemoryStream();
        await input.CopyToAsync(exported);
        exported.ToArray().Should().Equal(payload);
    }

    [Fact]
    public async Task Folder_reexport_replaces_the_package_and_removes_excluded_or_stale_files()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Release"))!;
        string firstSource = Path.Combine(_root, "first.txt");
        string secondSource = Path.Combine(_root, "second.txt");
        await File.WriteAllTextAsync(firstSource, "first");
        await File.WriteAllTextAsync(secondSource, "second");
        (await service.AddFileAsync(package.Id, firstSource)).Should().BeTrue();
        (await service.AddFileAsync(package.Id, secondSource)).Should().BeTrue();

        ContextPackage loaded = (await service.GetPackageAsync(package.Id))!;
        Guid excludedId = loaded.Items.Single(i => i.DisplayName == "second.txt").Id;
        string exportRoot = Path.Combine(_root, "exports");
        (await service.ExportToFolderAsync(
            package.Id,
            await ReviewAllAsync(service, package.Id),
            exportRoot)).Should().BeTrue();
        string packageRoot = Path.Combine(exportRoot, "Release");
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "stale.txt"), "must disappear");

        ContextExportSelection selection = ContextExportSelection.FromReviewedItems(
            loaded.Items.Select(item => item.Id),
            loaded.Items.Where(item => item.Id != excludedId).Select(item => item.Id));
        (await service.ExportToFolderAsync(package.Id, selection, exportRoot)).Should().BeTrue();

        File.Exists(Path.Combine(packageRoot, "stale.txt")).Should().BeFalse();
        Directory.GetFiles(packageRoot, "second.txt", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetFiles(packageRoot, "first.txt", SearchOption.AllDirectories).Should().ContainSingle();
        Directory.GetDirectories(exportRoot, ".octadock-context-backup-*").Should().BeEmpty();
    }

    [Fact]
    public async Task Folder_export_refuses_to_replace_an_unrelated_existing_directory()
    {
        ContextService service = BuildService();
        ContextPackage package = (await service.CreatePackageAsync("Existing"))!;
        string source = Path.Combine(_root, "source.txt");
        await File.WriteAllTextAsync(source, "source");
        (await service.AddFileAsync(package.Id, source)).Should().BeTrue();
        string exportRoot = Path.Combine(_root, "exports");
        string unrelated = Path.Combine(exportRoot, "Existing");
        Directory.CreateDirectory(unrelated);
        string sentinel = Path.Combine(unrelated, "personal.txt");
        await File.WriteAllTextAsync(sentinel, "do not replace");

        ContextExportSelection selection = await ReviewAllAsync(service, package.Id);
        Func<Task> export = () => service.ExportToFolderAsync(package.Id, selection, exportRoot);

        InvalidOperationException exception = (await export.Should().ThrowAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain("avoid replacing unrelated files");
        (await File.ReadAllTextAsync(sentinel)).Should().Be("do not replace");
        File.Exists(Path.Combine(unrelated, "context-manifest.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Folder_export_sanitizes_a_traversal_package_name_to_a_direct_child()
    {
        ContextService service = BuildService();
        ContextPackage package = (await service.CreatePackageAsync(".."))!;
        string source = Path.Combine(_root, "safe.txt");
        await File.WriteAllTextAsync(source, "safe");
        (await service.AddFileAsync(package.Id, source)).Should().BeTrue();

        string exportRoot = Path.Combine(_root, "exports");
        (await service.ExportToFolderAsync(
            package.Id,
            await ReviewAllAsync(service, package.Id),
            exportRoot)).Should().BeTrue();

        string packageRoot = Path.Combine(exportRoot, "context");
        Directory.Exists(packageRoot).Should().BeTrue();
        File.Exists(Path.Combine(packageRoot, "context-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "context-manifest.json")).Should().BeFalse("the package name cannot escape the export root");
    }

    [Fact]
    public async Task Removing_an_item_deletes_its_managed_snapshot_directory()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Cleanup"))!;
        string source = Path.Combine(_root, "remove.txt");
        await File.WriteAllTextAsync(source, "remove me");
        (await service.AddFileAsync(package.Id, source)).Should().BeTrue();
        ContextItem item = ((await service.GetPackageAsync(package.Id))!).Items.Single();
        string managedPath = new StoragePaths(_root).ToAbsolute(item.StorageRelativePath!);
        File.Exists(managedPath).Should().BeTrue();

        await service.RemoveItemAsync(item.Id);

        Directory.Exists(Path.GetDirectoryName(managedPath)).Should().BeFalse();
        ((await repository.GetPackageAsync(package.Id))!).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_package_deletes_all_of_its_managed_snapshot_directories()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Cleanup all"))!;
        string first = Path.Combine(_root, "one.txt");
        string second = Path.Combine(_root, "two.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");
        (await service.AddFileAsync(package.Id, first)).Should().BeTrue();
        (await service.AddFileAsync(package.Id, second)).Should().BeTrue();
        IReadOnlyList<string> itemDirectories = ((await service.GetPackageAsync(package.Id))!).Items
            .Select(i => Path.GetDirectoryName(new StoragePaths(_root).ToAbsolute(i.StorageRelativePath!))!)
            .ToList();

        await service.DeletePackageAsync(package.Id);

        itemDirectories.Should().OnlyContain(path => !Directory.Exists(path));
        (await repository.GetPackageAsync(package.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Failed_repository_add_compensates_the_new_managed_snapshot()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Failure"))!;
        repository.AddItemFailure = new InvalidOperationException("database write failed");
        string source = Path.Combine(_root, "orphan.txt");
        await File.WriteAllTextAsync(source, "do not orphan this");

        Func<Task> add = async () => _ = await service.AddFileAsync(package.Id, source);
        await add.Should().ThrowAsync<InvalidOperationException>().WithMessage("database write failed");

        string contextRoot = Path.Combine(_root, "Context");
        if (Directory.Exists(contextRoot))
        {
            Directory.GetDirectories(contextRoot).Should().BeEmpty();
            Directory.GetFiles(contextRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Package_notes_are_bounded_and_persisted_as_local_metadata()
    {
        var repository = new InMemoryContextRepository();
        ContextService service = BuildService(repository);
        ContextPackage package = (await service.CreatePackageAsync("Review"))!;

        await service.UpdatePackageNotesAsync(package.Id, "  Reproduce before changing the parser.  ");

        (await service.GetPackageAsync(package.Id))!.Notes
            .Should().Be("Reproduce before changing the parser.");
        Func<Task> tooLong = () => service.UpdatePackageNotesAsync(
            package.Id,
            new string('x', ContextService.MaxPackageNotesLength + 1));
        await tooLong.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*limited*");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort temp cleanup
        }
    }
}

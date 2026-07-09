using System.IO;
using System.IO.Compression;
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

    private ContextService BuildService()
    {
        var paths = new StoragePaths(_root);
        paths.EnsureDirectories();
        var safeWriter = new SafeFileWriter(new FileRevisionStore(Path.Combine(_root, "revisions")));
        return new ContextService(
            new InMemoryContextRepository(),
            paths,
            safeWriter,
            new AllowAllLicenseGate(),
            new NoopNotifications(),
            new FixedClock(),
            NullLogger<ContextService>.Instance);
    }

    [Fact]
    public async Task Ingesting_a_file_snapshots_it_and_export_packages_it_into_a_zip()
    {
        ContextService service = BuildService();
        string source = Path.Combine(_root, "hello.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(source, "hello world");

        ContextPackage package = (await service.CreatePackageAsync("Repro"))!;
        (await service.AddFileAsync(package.Id, source)).Should().BeTrue();

        string zipPath = Path.Combine(_root, "out.zip");
        (await service.ExportAsync(package.Id, new ContextExportSelection(), zipPath)).Should().BeTrue();

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
        (await service.ExportToFolderAsync(package.Id, new ContextExportSelection(), exportRoot)).Should().BeTrue();

        string packageRoot = Path.Combine(exportRoot, "Launch Review");
        Directory.Exists(packageRoot).Should().BeTrue();
        File.Exists(Path.Combine(packageRoot, "context-manifest.json")).Should().BeTrue();
        string exported = Directory.GetFiles(packageRoot, "notes.md", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        (await File.ReadAllTextAsync(exported)).Should().Be("# Notes");
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

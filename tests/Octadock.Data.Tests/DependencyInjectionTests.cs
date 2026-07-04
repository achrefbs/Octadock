using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Persistence;
using Octadock.Data.DependencyInjection;
using Octadock.Data.Sqlite;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddOctadockData_registers_all_persistence_services()
    {
        var root = Path.Combine(Path.GetTempPath(), $"octadock-di-{Guid.NewGuid():N}");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IStoragePaths>(new FakeStoragePaths(root));
            services.AddOctadockData();

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<ISqliteConnectionFactory>().Should().BeOfType<SqliteConnectionFactory>();
            provider.GetRequiredService<IOctadockDatabase>().Should().BeOfType<OctadockDatabase>();
            provider.GetRequiredService<ICaptureRepository>().Should().NotBeNull();
            provider.GetRequiredService<IActionRepository>().Should().NotBeNull();
            provider.GetRequiredService<IPinRepository>().Should().NotBeNull();
            provider.GetRequiredService<IClipboardClipRepository>().Should().NotBeNull();
            provider.GetRequiredService<IAiSessionRepository>().Should().NotBeNull();
            provider.GetRequiredService<ISettingsStore>().Should().NotBeNull();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Resolved_services_operate_against_storage_paths_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"octadock-di-{Guid.NewGuid():N}");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IStoragePaths>(new FakeStoragePaths(root));
            services.AddOctadockData();

            using var provider = services.BuildServiceProvider();
            var database = provider.GetRequiredService<IOctadockDatabase>();
            var captures = provider.GetRequiredService<ICaptureRepository>();

            await database.InitializeAsync();
            var capture = RecordFactory.MinimalCapture();
            await captures.AddAsync(capture);

            var loaded = await captures.GetAsync(capture.Id);
            loaded.Should().NotBeNull();

            File.Exists(Path.Combine(root, "octadock.db")).Should().BeTrue("the DB path came from IStoragePaths");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}

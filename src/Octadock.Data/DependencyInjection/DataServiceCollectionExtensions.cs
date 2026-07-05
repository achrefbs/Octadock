using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Persistence;
using Octadock.Data.Repositories;
using Octadock.Data.Sqlite;

namespace Octadock.Data.DependencyInjection;

/// <summary>Registers the Octadock SQLite persistence layer in a DI container.</summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite connection factory, database initializer, and the core
    /// repositories/store. The database path is resolved at runtime from the
    /// <see cref="IStoragePaths"/> registered by Octadock.Core, so no path argument
    /// is required. All services are singletons, which suits this single-process
    /// desktop application.
    /// </summary>
    public static IServiceCollection AddOctadockData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // A clock is required by the settings store; only register the default if
        // the host has not already supplied one.
        services.TryAddSingleton<IClock>(SystemClock.Instance);

        services.TryAddSingleton<ISqliteConnectionFactory>(static provider =>
        {
            var storagePaths = provider.GetRequiredService<IStoragePaths>();
            var logger = provider.GetRequiredService<ILogger<SqliteConnectionFactory>>();
            storagePaths.EnsureDirectories();
            return new SqliteConnectionFactory(storagePaths, logger);
        });

        services.TryAddSingleton<IOctadockDatabase, OctadockDatabase>();
        services.TryAddSingleton<ICaptureRepository, CaptureRepository>();
        services.TryAddSingleton<IActionRepository, ActionRepository>();
        services.TryAddSingleton<IPinRepository, PinRepository>();
        services.TryAddSingleton<IClipboardClipRepository, ClipboardClipRepository>();
        services.TryAddSingleton<ISettingsStore, SettingsStore>();

        return services;
    }
}

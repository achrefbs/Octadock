using Microsoft.Extensions.DependencyInjection;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Services;

namespace Octadock.Core.DependencyInjection;

/// <summary>
/// Registration helpers for the platform-agnostic Octadock.Core services.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core (platform-agnostic) Octadock services: clock, storage
    /// paths, command parsing/formatting, filename generation, settings, project
    /// serialization and retention. Persistence and platform services must be
    /// registered separately by their owning layers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="dataRoot">
    /// Optional explicit data root for <see cref="IStoragePaths"/>. When null,
    /// <c>%LOCALAPPDATA%\Octadock</c> is used.
    /// </param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddOctadockCore(this IServiceCollection services, string? dataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Stateless, shared services -> singletons.
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IStoragePaths>(_ => new StoragePaths(dataRoot));
        services.AddSingleton<ICommandParser, CommandParser>();
        services.AddSingleton<ICommandFormatter, CommandFormatter>();
        services.AddSingleton<IFilenameGenerator, FilenameGenerator>();
        services.AddSingleton<IProjectSerializer, OctadockProjectSerializer>();

        // Holds a cached settings snapshot -> singleton.
        services.AddSingleton<ISettingsService, SettingsService>();

        // Depends on scoped-ish repositories in the host, but is itself stateless.
        services.AddSingleton<IRetentionService, RetentionService>();

        return services;
    }
}

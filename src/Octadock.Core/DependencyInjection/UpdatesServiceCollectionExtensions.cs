using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.Core.Updates;

namespace Octadock.Core.DependencyInjection;

/// <summary>
/// Registers the WS1 update-check module: a settings/host-gated check that detects a
/// newer signed release without downgrading. The manifest host and the update signing
/// key are founder-gated; until they exist the null source reports "not configured".
/// Full auto-update is Deferred.
/// </summary>
public static class UpdatesServiceCollectionExtensions
{
    /// <summary>Env var that points at the signed update-manifest URL (founder-gated).</summary>
    public const string ManifestUrlEnvVar = "OCTADOCK_UPDATE_MANIFEST_URL";

    /// <summary>Adds the update-check services to the container.</summary>
    public static IServiceCollection AddOctadockUpdates(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IUpdateManifestSource>(_ =>
        {
            string? url = Environment.GetEnvironmentVariable(ManifestUrlEnvVar);
            return string.IsNullOrWhiteSpace(url)
                ? new NullUpdateManifestSource()
                : new HttpUpdateManifestSource(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, url);
        });

        services.AddSingleton(sp => new UpdateCheckService(
            sp.GetRequiredService<IUpdateManifestSource>(),
            CurrentVersion(),
            // The update trust ring is founder-gated (KMS); until then manifests are advisory.
            signatureVerifier: null,
            sp.GetService<ILogger<UpdateCheckService>>()));

        return services;
    }

    private static ReleaseVersion CurrentVersion()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        string? info = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entry?.GetName().Version?.ToString();
        return ReleaseVersion.TryParse(info, out ReleaseVersion version) ? version : new ReleaseVersion(0, 0, 0, null);
    }
}

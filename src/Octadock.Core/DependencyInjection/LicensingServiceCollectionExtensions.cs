using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Licensing;
using Octadock.Core.Trial;

namespace Octadock.Core.DependencyInjection;

/// <summary>
/// Registers the client-side licensing/trial module (WS4/WS5): the signed-state store
/// outside octadock.db, the monotonic trial clock, the entitlement verifier + trust
/// ring, the resolved license state the gate consults, and the activation service that
/// closes the money → key → activate loop. Depends on <see cref="IStoragePaths"/>,
/// <see cref="IClock"/> (Core) and <see cref="IMachineIdentity"/> (the platform layer),
/// which are resolved lazily.
/// </summary>
public static class LicensingServiceCollectionExtensions
{
    /// <summary>Env var that overrides the license-service base URL (local testing).</summary>
    public const string BaseUrlEnvVar = "OCTADOCK_LICENSE_API_BASEURL";

    /// <summary>Adds the Octadock client licensing services to the container.</summary>
    public static IServiceCollection AddOctadockLicensing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Signed state + trial persistence live under %LOCALAPPDATA%\Octadock\license\,
        // OUTSIDE octadock.db (R11): a forged file fails verification, and corrupting
        // the app database cannot drop a licensed app back to trial.
        services.AddSingleton<IEntitlementStore>(sp => new FileEntitlementStore(LicenseDirectory(sp)));
        services.AddSingleton<ITrialClockStore>(sp =>
            new FileTrialClockStore(Path.Combine(LicenseDirectory(sp), "trial-clock.json")));
        services.AddSingleton<TrialClock>();

        // Verify-only Ed25519 with the embedded trust ring (production key founder-gated).
        services.AddSingleton(_ => new EntitlementVerifier(ClientTrustAnchors.Default));
        services.AddSingleton<EntitlementEvaluator>();

        // The single source of truth the gate + UI consult.
        services.AddSingleton(sp => new LicenseStateService(
            sp.GetRequiredService<IEntitlementStore>(),
            sp.GetRequiredService<EntitlementEvaluator>(),
            sp.GetRequiredService<IMachineIdentity>(),
            sp.GetRequiredService<TrialClock>()));

        // The trial/license gate the ~8 service seams consult (WS5, R16).
        services.AddSingleton<LicenseGate>();
        services.AddSingleton<ILicenseGate>(sp => sp.GetRequiredService<LicenseGate>());

        // Activation: HTTP to the (founder-gated) license service, then local verify + store.
        services.AddSingleton(_ => new ActivationOptions
        {
            BaseUrl = Environment.GetEnvironmentVariable(BaseUrlEnvVar) is { Length: > 0 } url
                ? url
                : new ActivationOptions().BaseUrl,
        });
        services.AddSingleton<IActivationClient>(sp =>
        {
            ActivationOptions options = sp.GetRequiredService<ActivationOptions>();
            var http = new HttpClient { Timeout = options.Timeout };
            string baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
            {
                http.BaseAddress = baseUri;
            }

            return new HttpActivationClient(http, sp.GetService<ILogger<HttpActivationClient>>());
        });
        services.AddSingleton<ActivationService>();

        return services;
    }

    private static string LicenseDirectory(IServiceProvider sp)
        => Path.Combine(sp.GetRequiredService<IStoragePaths>().RootDirectory, "license");
}

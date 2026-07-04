using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.Core.Abstractions;

namespace Octadock.App.CaptureUx;

/// <summary>
/// Composition-root module for Octadock's capture UX surfaces: the selection
/// overlays + window picker (<see cref="IRegionSelectionService"/>), the all-in-one
/// HUD (<see cref="IHudService"/>) and the Capture Shelf
/// (<see cref="IShelfService"/>). Registered from <c>Program.Main</c> via
/// <see cref="AddCaptureUx"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class CaptureUxModule
{
    /// <summary>Adds the Capture UX services (shelf, region selection, HUD) to the container.</summary>
    public static IServiceCollection AddCaptureUx(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Capture Shelf: single long-lived window, populated as captures arrive.
        services.AddSingleton<ShelfService>();
        services.AddSingleton<IShelfService>(sp => sp.GetRequiredService<ShelfService>());

        // Selection overlays + window picker.
        services.AddSingleton<RegionSelectionService>();
        services.AddSingleton<IRegionSelectionService>(sp => sp.GetRequiredService<RegionSelectionService>());

        // All-in-one HUD.
        services.AddSingleton<HudService>();
        services.AddSingleton<IHudService>(sp => sp.GetRequiredService<HudService>());

        return services;
    }
}

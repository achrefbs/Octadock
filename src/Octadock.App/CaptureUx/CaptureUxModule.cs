using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.Core.Abstractions;

namespace Octadock.App.CaptureUx;

/// <summary>
/// Composition-root module for Octadock's capture UX surfaces: the selection
/// overlays + window picker (<see cref="IRegionSelectionService"/>) and the
/// Capture Shelf (<see cref="IShelfService"/>). Registered from
/// <c>Program.Main</c> via <see cref="AddCaptureUx"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal static class CaptureUxModule
{
    /// <summary>Adds the Capture UX services (shelf, region selection) to the container.</summary>
    public static IServiceCollection AddCaptureUx(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Capture Shelf: single long-lived window, populated as captures arrive.
        services.AddSingleton<ShelfService>();
        services.AddSingleton<IShelfService>(sp => sp.GetRequiredService<ShelfService>());

        // One command seam shared by the stable Dock rail and the secondary
        // capture tools hosted by the Shelf.
        services.AddSingleton<CaptureActionService>();
        services.AddSingleton<ICaptureActionService>(sp => sp.GetRequiredService<CaptureActionService>());

        // Selection overlays + window picker.
        services.AddSingleton<RegionSelectionService>();
        services.AddSingleton<IRegionSelectionService>(sp => sp.GetRequiredService<RegionSelectionService>());

        return services;
    }
}

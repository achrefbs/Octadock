using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.History;
using Octadock.App.Services;
using Octadock.Core.Abstractions;

namespace Octadock.App.Editing;

/// <summary>
/// Composition for the editing feature area: the annotation editor and the local
/// history window. Registers the orchestration services those surfaces expose so
/// the rest of the app (dispatcher, hotkeys, window presenter) can resolve them.
/// The composition root calls <see cref="AddEditing"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class EditingModule
{
    /// <summary>Registers the annotation and history services.</summary>
    public static IServiceCollection AddEditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Annotation editor.
        services.AddSingleton<AnnotationService>();
        services.AddSingleton<IAnnotationService>(sp => sp.GetRequiredService<AnnotationService>());

        // Local history.
        services.AddSingleton<HistoryPresenter>();
        services.AddSingleton<IHistoryPresenter>(sp => sp.GetRequiredService<HistoryPresenter>());

        // View models / windows are constructed via ActivatorUtilities with their
        // Core dependencies resolved from the container, but registering the view
        // models keeps them available for direct resolution and testing.
        services.AddTransient<HistoryViewModel>();

        return services;
    }
}

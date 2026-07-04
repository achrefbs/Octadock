using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;

namespace Octadock.App.TextTools;

/// <summary>Registers the text-transform toolbox window and presenter.</summary>
public static class TextToolsModule
{
    /// <summary>Adds text-tools services to the container.</summary>
    public static IServiceCollection AddTextTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TextToolsPresenter>();
        services.AddSingleton<ITextToolsPresenter>(sp => sp.GetRequiredService<TextToolsPresenter>());
        services.AddTransient<TextToolsViewModel>();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;

namespace Octadock.App.Clipboard;

/// <summary>
/// Registers the clipboard-history feature: the Win32 clipboard monitor, the
/// snapshot source with privacy filtering, the recording pipeline, and the
/// history window (presenter singleton + transient view models).
/// </summary>
public static class ClipboardModule
{
    /// <summary>Adds clipboard-history services to the container.</summary>
    public static IServiceCollection AddClipboardHistory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClipboardMonitor, ClipboardMonitor>();
        services.AddSingleton<IClipboardSnapshotSource, WpfClipboardSnapshotSource>();
        services.AddSingleton<ClipboardHistoryService>();

        services.AddSingleton<ClipboardHistoryPresenter>();
        services.AddSingleton<IClipboardHistoryPresenter>(sp => sp.GetRequiredService<ClipboardHistoryPresenter>());
        services.AddTransient<ClipboardHistoryViewModel>();

        return services;
    }
}

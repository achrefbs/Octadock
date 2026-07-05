using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Octadock.App.AiSessions;
using Octadock.App.Clipboard;
using Octadock.App.Diagnostics;
using Octadock.App.Imaging;
using Octadock.App.Preview;
using Octadock.App.Services;
using Octadock.App.Settings;
using Octadock.App.Theming;
using Octadock.App.Tray;
using Octadock.Core.Abstractions;
using Octadock.Core.Persistence;
using Octadock.Core.Services;

namespace Octadock.App.DependencyInjection;

/// <summary>
/// Registers the WPF app-layer services that compose Core + Data + Platform.Windows
/// into the running Octadock application: imaging (WPF encoders + the convenience
/// <see cref="IImageLoadService"/>), clipboard, the orchestration implementations
/// (command dispatcher, capture coordinator, OCR, notifications, window presenter),
/// the tray controller, the theme manager, and the settings/first-run view models.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class AppServiceCollectionExtensions
{
    /// <summary>Adds every Octadock.App service to the container.</summary>
    public static IServiceCollection AddOctadockApp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ---- Imaging (WPF) ----
        // The platform layer intentionally does NOT register these; the WPF app owns
        // image encoding, thumbnails and clipboard via WPF imaging.
        services.AddSingleton<IImageEncoder, WpfImageEncoder>();
        services.AddSingleton<IThumbnailGenerator, WpfThumbnailGenerator>();
        services.AddSingleton<IImageLoadService, WpfImageLoadService>();

        // ---- Clipboard (WPF STA) ----
        services.AddSingleton<IClipboardService, WpfClipboardService>();

        // ---- Notifications (tray balloons via the tray controller sink) ----
        services.AddSingleton<NotificationService>();
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationService>());

        // ---- Orchestration ----
        services.AddSingleton<CaptureGate>();
        services.AddSingleton<CaptureCoordinator>();
        services.AddSingleton<ICaptureCoordinator>(sp => sp.GetRequiredService<CaptureCoordinator>());
        services.AddSingleton<OcrHistoryRecorder>();
        services.AddSingleton<IOcrService, OcrService>();
        services.AddSingleton<RecordingController>();
        services.AddSingleton<DictationController>();
        services.AddSingleton<ITextExplanationProvider, CliTextExplanationProvider>();
        services.AddSingleton<ReadAloudService>();
        services.AddSingleton<AiSessionCommandService>();
        services.AddSingleton<AiSessionDiscoveryService>();
        services.AddSingleton<AiSessionOverlayService>();
        services.AddSingleton<AiSessionProcessExitWatcher>();

        // Every session write publishes on the change bus so the overlay,
        // windows, and the exit watcher update instantly instead of polling.
        services.Replace(ServiceDescriptor.Singleton<IAiSessionRepository>(sp =>
            new NotifyingAiSessionRepository(
                ActivatorUtilities.CreateInstance<Octadock.Data.Repositories.AiSessionRepository>(sp),
                sp.GetRequiredService<IAiSessionChangeBus>())));
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<CrashReportService>();

        // ---- File preview (Quick Look-style cards) ----
        // Selection is by descending Priority, then registration order. The
        // JSON/log/markdown providers outrank the generic text provider for
        // their extensions. ImagePreviewProvider lives in the App project (not
        // Core) because WPF owns image decoding.
        services.AddSingleton<IFilePreviewProvider, CsvPreviewProvider>();
        services.AddSingleton<IFilePreviewProvider, JsonPreviewProvider>();
        services.AddSingleton<IFilePreviewProvider, LogPreviewProvider>();
        services.AddSingleton<IFilePreviewProvider, MarkdownPreviewProvider>();
        services.AddSingleton<IFilePreviewProvider, TextPreviewProvider>();
        services.AddSingleton<IFilePreviewProvider, ImagePreviewProvider>();
        services.AddSingleton<FilePreviewService>();

        services.AddSingleton<WindowPresenter>();
        services.AddSingleton<IWindowPresenter>(sp => sp.GetRequiredService<WindowPresenter>());

        // ---- Tray + theme ----
        services.AddSingleton<TrayIconController>();
        services.AddSingleton<ThemeManager>();

        // ---- View models (transient: each window gets a fresh working copy) ----
        services.AddTransient<AiSessionsViewModel>();
        services.AddTransient<AiSessionsWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<Octadock.App.FirstRun.FirstRunViewModel>();
        services.AddTransient<Octadock.App.FirstRun.FirstRunWindow>();
        services.AddTransient<Octadock.App.About.AboutWindow>();

        return services;
    }
}

using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Ai;
using Octadock.App.Clipboard;
using Octadock.App.Context;
using Octadock.App.Diagnostics;
using Octadock.App.Imaging;
using Octadock.App.Services;
using Octadock.App.Settings;
using Octadock.App.Theming;
using Octadock.App.Tray;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Io;
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

        // ---- Safe file writes (WS9, R23): atomic + reversible user-file saves ----
        services.AddSingleton<IFileRevisionStore>(sp =>
            new FileRevisionStore(Path.Combine(
                sp.GetRequiredService<IStoragePaths>().RootDirectory, "revisions")));
        services.AddSingleton<ISafeFileWriter, SafeFileWriter>();

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
        // Model-download consent gate (WS7, R6): dictation must not fetch a large
        // model without explicit, one-time, sized consent.
        services.AddSingleton<IModelDownloadConsentPrompt, MessageBoxModelDownloadConsentPrompt>();
        services.AddSingleton<IModelDownloadConsent, ModelDownloadConsentService>();
        services.AddSingleton<DictationController>();
        services.AddSingleton<DictationPushToTalk>();
        services.AddSingleton<ITextSecretDetector, TextSecretDetector>();
        services.AddSingleton<IAgentPacketBuilder, AgentPacketBuilder>();
        services.AddSingleton<IAiCliRunner, CliAiRunner>();
        services.AddSingleton<IAgentCliRunner, AgentCliRunner>();
        services.AddSingleton<IAiTextActionService, CliAiTextActionService>();
        services.AddSingleton<IAiSendConfirmation, WpfAiSendConfirmation>();
        services.AddSingleton<IAiTextFileLoader, AiTextFileLoader>();
        services.AddSingleton<IAiTextFilePicker, WpfAiTextFilePicker>();
        services.AddSingleton<AgentEvidenceFactory>();
        services.AddSingleton<IAgentTemporaryLeaseStore, AgentTemporaryLeaseStore>();
        services.AddSingleton<IVisualComparisonService, SkiaVisualComparisonService>();
        services.AddSingleton<IImageEditProvider, CodexCliImageEditProvider>();
        services.AddSingleton<IImageMockupService, ImageMockupService>();
        services.AddSingleton<IAgentWorkspacePicker, WpfAgentWorkspacePicker>();
        services.AddSingleton<IAgentPacketExportService, AgentPacketExportService>();
        services.AddSingleton<IAgentHandoffConfirmation, WpfAgentHandoffConfirmation>();
        services.AddSingleton<ReadAloudService>();
        services.AddSingleton<IActivationReplacementConfirmation, WpfActivationReplacementConfirmation>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<ActiveContextState>();
        services.AddSingleton<ContextService>();
        services.AddSingleton<CrashReportService>();

        services.AddSingleton<WindowPresenter>();
        services.AddSingleton<IWindowPresenter>(sp => sp.GetRequiredService<WindowPresenter>());

        // ---- Tray + theme ----
        services.AddSingleton<TrayIconController>();
        services.AddSingleton<ThemeManager>();

        // ---- View models (transient: each window gets a fresh working copy) ----
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<Octadock.App.FirstRun.FirstRunViewModel>();
        services.AddTransient<Octadock.App.FirstRun.FirstRunWindow>();
        services.AddTransient<Octadock.App.About.AboutWindow>();
        services.AddTransient<Octadock.App.Context.ContextViewModel>();
        services.AddTransient<Octadock.App.Context.ContextWindow>();
        services.AddTransient<AiActionsViewModel>();
        services.AddTransient<AiActionsWindow>();
        services.AddTransient<AgentWorkspaceViewModel>();
        services.AddTransient<AgentWorkspaceWindow>();

        return services;
    }
}

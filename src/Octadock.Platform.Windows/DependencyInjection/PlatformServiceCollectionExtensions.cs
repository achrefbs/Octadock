using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Octadock.Core.Abstractions;
using Octadock.Platform.Windows.Audio;
using Octadock.Platform.Windows.Capture;
using Octadock.Platform.Windows.Hotkeys;
using Octadock.Platform.Windows.Monitors;
using Octadock.Platform.Windows.Ocr;
using Octadock.Platform.Windows.Recording;
using Octadock.Platform.Windows.Stt;
using Octadock.Platform.Windows.System;
using Octadock.Platform.Windows.Tts;

namespace Octadock.Platform.Windows.DependencyInjection;

/// <summary>
/// Registers the Windows platform implementations of the Octadock Core
/// abstractions (monitors, hotkeys, capture, OCR, recording, system integration).
/// Clipboard, image encoding and thumbnail generation are intentionally NOT
/// registered here; the WPF app layer owns those via WPF imaging.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class PlatformServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Windows platform services to the container. Most are singletons
    /// because they own long-lived native resources (hotkey window, D3D device,
    /// single-instance mutex/pipe).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection for chaining.</returns>
    public static IServiceCollection AddOctadockPlatformWindows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Monitors / DPI.
        services.AddSingleton<MonitorService>();
        services.AddSingleton<IMonitorService>(sp => sp.GetRequiredService<MonitorService>());

        // Global hotkeys (owns a message-only window + pump thread).
        services.AddSingleton<IHotkeyService, HotkeyService>();

        // Still capture, window picker, capture exclusion.
        services.AddSingleton<ICaptureEngine, CaptureEngine>();
        services.AddSingleton<IWindowPicker, WindowPicker>();
        services.AddSingleton<ICaptureExclusion, CaptureExclusion>();

        // OCR: Windows.Media.Ocr provider + factory.
        services.AddSingleton<WindowsMediaOcrProvider>();
        services.AddSingleton<IOcrProvider>(sp => sp.GetRequiredService<WindowsMediaOcrProvider>());
        services.AddSingleton<IOcrProviderFactory, OcrProviderFactory>();

        // Recording + scrolling capture.
        services.AddSingleton<IRecordingEngine, MediaFoundationRecordingEngine>();
        services.AddSingleton<IScrollingCaptureEngine, ScrollingCaptureEngine>();

        // Speech to text: WASAPI mic capture + selectable local/cloud providers.
        services.AddSingleton<AudioCaptureService>();
        services.AddSingleton<IDictationAudioSource>(sp => sp.GetRequiredService<AudioCaptureService>());
        services.AddSingleton<ParakeetModelStore>();
        services.AddSingleton<ParakeetSttProvider>();
        services.AddSingleton<WhisperSttProvider>();
        services.AddSingleton<OpenAiSttProvider>();
        services.AddSingleton<ISpeechToTextProvider>(sp => sp.GetRequiredService<ParakeetSttProvider>());
        services.AddSingleton<ISpeechToTextProvider>(sp => sp.GetRequiredService<WhisperSttProvider>());
        services.AddSingleton<ISpeechToTextProvider>(sp => sp.GetRequiredService<OpenAiSttProvider>());
        services.AddSingleton<ISpeechToTextProviderFactory, SpeechToTextProviderFactory>();

        // Text to speech: ElevenLabs synthesis + local generated-audio playback.
        services.AddSingleton<ITextToSpeechProvider, ElevenLabsTtsProvider>();
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

        // System integration.
        services.AddSingleton<IStartupRegistration, StartupRegistration>();
        services.AddSingleton<IProtocolRegistration, ProtocolRegistration>();
        services.AddSingleton<FileAssociationRegistration>();
        services.AddSingleton<ISingleInstanceGuard, SingleInstanceGuard>();

        return services;
    }
}

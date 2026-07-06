using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Octadock.App.CaptureUx;
using Octadock.App.Clipboard;
using Octadock.App.DependencyInjection;
using Octadock.App.Editing;
using Octadock.App.TextTools;
using Octadock.Core.Abstractions;
using Octadock.Core.DependencyInjection;
using Octadock.Core.Services;
using Octadock.Data.DependencyInjection;
using Octadock.Platform.Windows.DependencyInjection;
using Octadock.Platform.Windows.Monitors;

namespace Octadock.App;

/// <summary>
/// Process entry point. Builds the composition root (Serilog into
/// Microsoft.Extensions.Logging; Core + Data + Platform.Windows + App services +
/// the CaptureUx/Editing feature modules), enforces a single instance (forwarding
/// launch arguments to the primary over a named pipe), sets per-monitor-DPI
/// awareness before any WPF window exists, and runs the WPF <see cref="App"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Configure Serilog against the real logs directory before the host exists.
        IStoragePaths bootstrapPaths = new StoragePaths();
        Directory.CreateDirectory(bootstrapPaths.LogsDirectory);

        // Per-process log files: the shared:true sink went permanently silent
        // after a force-killed instance (its cross-process lock never
        // recovered), which cost us every log line for half a day. One file
        // per process id cannot collide, needs no shared lock, and flushes to
        // disk every 2 s so crashes/kills lose almost nothing.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(
                    bootstrapPaths.LogsDirectory,
                    $"octadock-{Environment.ProcessId}-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .WriteTo.Debug()
            .CreateLogger();

        try
        {
            ServiceProvider provider = BuildServiceProvider();

            // Enforce a single instance; forward arguments to the primary if one runs.
            var singleInstance = provider.GetRequiredService<ISingleInstanceGuard>();
            if (!singleInstance.TryAcquire())
            {
                Log.Information("Another Octadock instance is running; forwarding arguments and exiting.");
                int forwardedExitCode = 0;
                try
                {
                    var response = singleInstance.ForwardArgumentsAsync(args).GetAwaiter().GetResult();
                    forwardedExitCode = response.Success || response.ExitCode != 0
                        ? response.ExitCode
                        : 1;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to forward arguments to the primary instance.");
                    forwardedExitCode = 1;
                }

                provider.Dispose();
                return forwardedExitCode;
            }

            // Primary instance: begin listening for forwarded launches.
            singleInstance.StartListening();

            // Per-monitor-DPI awareness must be set before any WPF window is created.
            DpiAwareness.EnsurePerMonitorV2();

            var app = new App();
            App.SetServiceProvider(provider);
            app.HookGlobalExceptionHandling();
            app.InitializeComponent();

            int exitCode = app.Run();
            provider.Dispose();
            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Octadock terminated unexpectedly.");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Logging: route Microsoft.Extensions.Logging through Serilog.
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: false);
        });

        // Layered composition. Data root defaults to %LOCALAPPDATA%\Octadock.
        services.AddOctadockCore();
        services.AddOctadockData();
        services.AddOctadockPlatformWindows();

        // WPF app services (imaging, clipboard, orchestration, tray, theme, windows).
        services.AddOctadockApp();

        // Client licensing/trial module (signed state, trial clock, gate state, activation).
        services.AddOctadockLicensing();

        // Feature modules provided by the sibling agents.
        // provided by CaptureUx/Editing modules
        services.AddCaptureUx();
        services.AddEditing();
        services.AddClipboardHistory();
        services.AddTextTools();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = false,
        });
    }
}

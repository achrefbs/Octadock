using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.AiSessions;
using Octadock.App.Diagnostics;
using Octadock.App.Services;
using Octadock.App.Theming;
using Octadock.App.Tray;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Hotkeys;
using Octadock.Core.Persistence;

namespace Octadock.App;

/// <summary>
/// The WPF application object. Exposes the process-wide <see cref="Services"/>
/// container (windows created with <c>new</c> resolve dependencies through it),
/// performs runtime initialization (database, settings, protocol, hotkeys, tray,
/// first-run, persisted pins) and handles unhandled dispatcher exceptions without
/// tearing the app down.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class App : System.Windows.Application
{
    private static IServiceProvider? _services;

    private TrayIconController? _tray;
    private ThemeManager? _theme;
    private IHotkeyService? _hotkeys;
    private ISingleInstanceGuard? _singleInstance;
    private ICommandDispatcher? _dispatcher;
    private ICommandParser? _parser;
    private IPinService? _pins;
    private IRetentionService? _retention;
    private AiSessionDiscoveryService? _aiSessionDiscovery;
    private AiSessionOverlayService? _aiSessionOverlay;
    private Octadock.App.Services.AiSessionProcessExitWatcher? _aiSessionExitWatcher;
    private Octadock.App.Services.AiSessionActivityWatchers? _aiSessionActivityWatchers;
    private Octadock.App.Clipboard.ClipboardHistoryService? _clipboardHistory;
    private DispatcherTimer? _retentionTimer;
    private int _retentionRunning;
    private ILogger<App>? _logger;
    private readonly object _launchArgsGate = new();
    private readonly Queue<PendingLaunchArgs> _pendingLaunchArgs = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _startupTask;
    private bool _launchDispatchReady;
    private bool _isShuttingDown;

    /// <summary>
    /// The application service provider. Set once during startup by
    /// <see cref="SetServiceProvider"/> before <see cref="OnStartup"/> runs.
    /// </summary>
    public static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("App.Services has not been initialized yet.");

    /// <summary>The permanent dock capsule, when shown (used to reflect recording state).</summary>
    internal static Octadock.App.CaptureUx.DockPill? Dock { get; private set; }

    /// <summary>Assigns the composed service provider. Called by <c>Program.Main</c>.</summary>
    public static void SetServiceProvider(IServiceProvider services)
        => _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = Services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Octadock starting up.");

        // Kick off async initialization but do not block the dispatcher.
        _startupTask = InitializeAsync(e?.Args ?? Array.Empty<string>(), _shutdownCts.Token);
    }

    private async Task InitializeAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Theme first so any window shown during init is styled.
            _theme = Services.GetRequiredService<ThemeManager>();
            _theme.Initialize();
            cancellationToken.ThrowIfCancellationRequested();

            // Subscribe early so secondary launches during startup are queued
            // instead of being dropped before command dispatch is ready.
            WireSingleInstance();
            cancellationToken.ThrowIfCancellationRequested();

            // Database schema must exist before settings are read from it.
            var database = Services.GetRequiredService<IOctadockDatabase>();
            await database.InitializeAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            var settings = Services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            // Re-apply theme now that persisted settings are loaded.
            _theme.Refresh();

            // System registrations follow persisted settings and are retried on startup.
            SyncStartupRegistration(settings);
            SyncProtocolRegistration(settings);
            RegisterFileAssociations();

            // Tray icon.
            _tray = Services.GetRequiredService<TrayIconController>();
            _tray.Initialize();
            cancellationToken.ThrowIfCancellationRequested();

            // Hotkeys.
            RegisterHotkeys(settings);
            cancellationToken.ThrowIfCancellationRequested();

            // The permanent dock: Octadock's always-on-screen glass capsule. Shown
            // only when enabled in settings, and toggled live via the Changed event.
            try
            {
                var monitors = Services.GetRequiredService<Octadock.Core.Abstractions.IMonitorService>();

                void ApplyDockVisibility(bool enabled)
                {
                    try
                    {
                        if (enabled)
                        {
                            if (Dock is null)
                            {
                                Dock = new Octadock.App.CaptureUx.DockPill();
                                Dock.ShowOn(monitors.GetPrimary());
                            }
                        }
                        else if (Dock is not null)
                        {
                            Dock.Close();
                            Dock = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "The dock capsule could not be shown; continuing without it.");
                    }
                }

                ApplyDockVisibility(settings.Current.Dock.Enabled);
                settings.Changed += (_, e) =>
                    Dispatcher.BeginInvoke(() => ApplyDockVisibility(e.Settings.Dock.Enabled));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "The dock capsule could not be shown; continuing without it.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // First run (modal, once).
            var presenter = Services.GetRequiredService<IWindowPresenter>();
            await presenter.ShowFirstRunIfNeededAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            // Restore persisted pins from a previous session.
            _pins = Services.GetService<IPinService>();
            if (_pins is not null)
            {
                await _pins.RestorePersistedPinsAsync().ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Enforce history retention: purge expired/soft-deleted captures and
            // their files at startup and then periodically. Without this the
            // History "retention" setting is inert and storage grows unbounded.
            StartRetention();
            cancellationToken.ThrowIfCancellationRequested();

            // Keep Active AI Sessions populated with already-running local AI
            // tools, even when they were not started through Octadock.
            _aiSessionDiscovery = Services.GetService<AiSessionDiscoveryService>();
            _aiSessionDiscovery?.Start();
            _aiSessionOverlay = Services.GetService<AiSessionOverlayService>();
            _aiSessionOverlay?.Start();

            // Real-time layer: instant process-exit completion plus WMI/file
            // triggers that scan the moment a tool starts or writes activity.
            _aiSessionExitWatcher = Services.GetService<Octadock.App.Services.AiSessionProcessExitWatcher>();
            _aiSessionExitWatcher?.Start();
            _aiSessionActivityWatchers = Services.GetService<Octadock.App.Services.AiSessionActivityWatchers>();
            _aiSessionActivityWatchers?.Start();
            cancellationToken.ThrowIfCancellationRequested();

            // Clipboard history: local-only monitor gated by its Settings toggle.
            _clipboardHistory = Services.GetService<Octadock.App.Clipboard.ClipboardHistoryService>();
            _clipboardHistory?.Start();
            cancellationToken.ThrowIfCancellationRequested();

            // Process any command that launched this instance (protocol/CLI).
            _dispatcher = Services.GetRequiredService<ICommandDispatcher>();
            _parser = Services.GetRequiredService<ICommandParser>();
            await EnableLaunchDispatchingAsync(args, cancellationToken).ConfigureAwait(true);

            _logger!.LogInformation("Octadock startup complete.");
        }
        catch (OperationCanceledException) when (_isShuttingDown || cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Octadock startup cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            if (_isShuttingDown)
            {
                _logger?.LogDebug(ex, "Ignoring startup failure because shutdown is already in progress.");
                return;
            }

            _logger!.LogCritical(ex, "Fatal error during startup.");
            TryWriteCrashReport(ex, "Startup", isTerminating: true);
            MessageBox.Show(
                "Octadock failed to start. See the logs under %LOCALAPPDATA%\\Octadock\\Logs for details.",
                "Octadock",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void StartRetention()
    {
        _retention = Services.GetService<IRetentionService>();
        if (_retention is null)
        {
            return;
        }

        // One pass now, then every six hours for long-running sessions.
        _ = RunRetentionAsync();

        _retentionTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _retentionTimer.Tick += (_, _) => _ = RunRetentionAsync();
        _retentionTimer.Start();
    }

    private async Task RunRetentionAsync()
    {
        // Skip if already running or shutting down; retention is best-effort.
        if (_retention is null || _isShuttingDown ||
            Interlocked.Exchange(ref _retentionRunning, 1) == 1)
        {
            return;
        }

        try
        {
            var settings = Services.GetRequiredService<ISettingsService>().Current;
            var result = await Task.Run(
                () => _retention.RunAsync(settings, _shutdownCts.Token), _shutdownCts.Token).ConfigureAwait(true);

            // Periodic durability: fold the WAL so hours of session/clip writes
            // never sit exclusively in the log.
            await (Services.GetService<IOctadockDatabase>()?.CheckpointAsync(_shutdownCts.Token)
                ?? Task.CompletedTask).ConfigureAwait(true);
            if (result.CapturesDeleted > 0 || result.FilesDeleted > 0)
            {
                _logger?.LogInformation(
                    "Retention removed {Captures} captures and {Files} files ({Bytes} bytes).",
                    result.CapturesDeleted,
                    result.FilesDeleted,
                    result.BytesReclaimed);
            }
        }
        catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
        {
            // Shutdown; nothing to do.
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Retention pass failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _retentionRunning, 0);
        }
    }

    private void RegisterHotkeys(ISettingsService settings)
    {
        IHotkeyService hotkeys = Services.GetRequiredService<IHotkeyService>();
        if (_hotkeys is not null && !ReferenceEquals(_hotkeys, hotkeys))
        {
            _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        }

        _hotkeys = hotkeys;
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _hotkeys.HotkeyPressed += OnHotkeyPressed;

        IReadOnlyList<HotkeyRegistration> results = _hotkeys.RegisterAll(settings.Current.Shortcuts);
        foreach (HotkeyRegistration registration in results)
        {
            if (registration.IsConflict)
            {
                _logger!.LogWarning(
                    "Hotkey for {Action} ({Gesture}) could not be registered: {Error}",
                    registration.Action,
                    registration.Gesture,
                    registration.Error ?? "conflict");
            }
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        PostAsyncToDispatcher(() => HandleHotkeyPressedAsync(e.Action), $"hotkey {e.Action}");
    }

    private async Task HandleHotkeyPressedAsync(HotkeyAction hotkeyAction)
    {
        if (_tray?.IsPaused == true)
        {
            return;
        }

        var coordinator = Services.GetRequiredService<ICaptureCoordinator>();
        var presenter = Services.GetRequiredService<IWindowPresenter>();
        var ocr = Services.GetRequiredService<IOcrService>();
        PostCaptureAction action = Services.GetRequiredService<ISettingsService>().Current.Capture.DefaultAction;

        Task work = hotkeyAction switch
        {
            HotkeyAction.CaptureArea => coordinator.CaptureAreaAsync(action),
            HotkeyAction.CaptureWindow => coordinator.CaptureWindowAsync(action),
            HotkeyAction.CaptureFullscreen => coordinator.CaptureFullscreenAsync(action, null, false),
            HotkeyAction.CapturePreviousArea => coordinator.CapturePreviousAreaAsync(action),
            HotkeyAction.Dictation => Services.GetRequiredService<DictationController>().ToggleAsync(),
            HotkeyAction.Ocr => ocr.CaptureRegionTextAsync(Services.GetRequiredService<ISettingsService>().Current.Ocr.OutputMode, null),
            HotkeyAction.Record => Services.GetRequiredService<Octadock.App.Services.RecordingController>().ToggleAsync(),
            HotkeyAction.ClipboardHistory => Task.Run(presenter.ShowClipboardHistory),
            HotkeyAction.AllInOne => RunHud(presenter, null),
            _ => Task.CompletedTask,
        };

        await work.ConfigureAwait(true);
    }

    private static Task RunHud(IWindowPresenter presenter, CaptureMode? mode)
    {
        presenter.ShowAllInOneHud(mode);
        return Task.CompletedTask;
    }

    private void WireSingleInstance()
    {
        if (_singleInstance is not null)
        {
            return;
        }

        _singleInstance = Services.GetService<ISingleInstanceGuard>();
        if (_singleInstance is null)
        {
            return;
        }

        _singleInstance.SecondInstanceLaunched += OnSecondInstanceLaunchedAsync;
    }

    private Task<CommandResult> OnSecondInstanceLaunchedAsync(
        object? sender,
        SecondInstanceEventArgs e,
        CancellationToken cancellationToken)
        => QueueOrDispatchLaunchArgsAsync(e.Arguments, e.WorkingDirectory, cancellationToken);

    private async Task<CommandResult> QueueOrDispatchLaunchArgsAsync(
        IReadOnlyList<string> args,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            return CommandResult.Ok;
        }

        string[] snapshot = args as string[] ?? args.ToArray();
        PendingLaunchArgs? pending = null;
        lock (_launchArgsGate)
        {
            if (!_launchDispatchReady)
            {
                pending = new PendingLaunchArgs(snapshot, workingDirectory);
                _pendingLaunchArgs.Enqueue(pending);
            }
        }

        if (pending is not null)
        {
            return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await DispatchLaunchArgsOnUiAsync(snapshot, workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnableLaunchDispatchingAsync(IReadOnlyList<string> initialArgs, CancellationToken cancellationToken)
    {
        await DispatchLaunchArgsAsync(initialArgs, Environment.CurrentDirectory, cancellationToken).ConfigureAwait(true);

        while (true)
        {
            PendingLaunchArgs? next;
            lock (_launchArgsGate)
            {
                if (_pendingLaunchArgs.Count == 0)
                {
                    _launchDispatchReady = true;
                    return;
                }

                next = _pendingLaunchArgs.Dequeue();
            }

            try
            {
                CommandResult result = await DispatchLaunchArgsAsync(
                    next.Arguments,
                    next.WorkingDirectory,
                    cancellationToken).ConfigureAwait(true);
                next.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                next.Completion.TrySetCanceled(ex.CancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to dispatch queued launch arguments.");
                next.Completion.TrySetResult(CommandResult.Fail("Internal error processing the request."));
            }
        }
    }

    private async Task<CommandResult> DispatchLaunchArgsOnUiAsync(
        IReadOnlyList<string> args,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess())
        {
            return await DispatchLaunchArgsAsync(args, workingDirectory, cancellationToken).ConfigureAwait(true);
        }

        Task<CommandResult> dispatched = await Dispatcher
            .InvokeAsync(() => DispatchLaunchArgsAsync(args, workingDirectory, cancellationToken))
            .Task
            .ConfigureAwait(false);
        return await dispatched.ConfigureAwait(false);
    }

    private async Task<CommandResult> DispatchLaunchArgsAsync(
        IReadOnlyList<string> args,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        if (args.Count == 0 || _parser is null || _dispatcher is null)
        {
            return CommandResult.Ok;
        }

        bool protocolLaunch = AutomationLaunchSafety.IsProtocolLaunch(args);
        CommandResult? automationGate = CheckAutomationLaunchGate(args, protocolLaunch);
        if (automationGate is not null)
        {
            return automationGate;
        }

        CommandParseResult parsed = _parser.ParseArguments(args);
        if (!parsed.Success || parsed.Command is null)
        {
            _logger!.LogInformation("Ignoring unrecognized launch arguments: {Error}", parsed.Error);
            return CommandResult.Fail(parsed.Error ?? "Unrecognized launch arguments.");
        }

        if (AutomationLaunchSafety.BlocksProtocolCommand(args, parsed.Command))
        {
            return CommandResult.Fail(
                "This command cannot be launched from octadock:// URLs. Use octadock.exe from a local shell.");
        }

        try
        {
            OctadockCommand command = AttachWorkingDirectory(parsed.Command, workingDirectory);
            CommandResult result = await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(true);
            if (!result.Success)
            {
                _logger!.LogInformation("Launch command reported failure: {Message}", result.Message);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger!.LogError(ex, "Failed to dispatch launch command {Command}.", parsed.Command);
            return CommandResult.Fail($"Command failed: {ex.Message}");
        }
    }

    private static OctadockCommand AttachWorkingDirectory(OctadockCommand command, string? workingDirectory)
    {
        if (command.Type != CommandType.Run ||
            command.Has("cwd") ||
            string.IsNullOrWhiteSpace(workingDirectory))
        {
            return command;
        }

        var parameters = new Dictionary<string, string>(command.Parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["cwd"] = workingDirectory,
        };

        return command with { Parameters = parameters };
    }

    private static CommandResult? CheckAutomationLaunchGate(IReadOnlyList<string> args, bool protocolLaunch)
    {
        ISettingsService settings = Services.GetRequiredService<ISettingsService>();

        if (protocolLaunch)
        {
            return settings.Current.Automation.ProtocolEnabled
                ? null
                : CommandResult.Fail("octadock:// protocol automation is disabled in Settings.");
        }

        return settings.Current.Automation.CliEnabled
            ? null
            : CommandResult.Fail("octadock.exe command-line automation is disabled in Settings.");
    }

    private void SyncProtocolRegistration(ISettingsService settings)
    {
        try
        {
            var protocol = Services.GetRequiredService<IProtocolRegistration>();
            bool wanted = settings.Current.Automation.ProtocolEnabled;
            if (wanted && !protocol.IsRegistered())
            {
                protocol.Register();
            }
            else if (!wanted)
            {
                protocol.Unregister();
            }
        }
        catch (Exception ex)
        {
            _logger!.LogWarning(ex, "Failed to synchronize the octadock:// protocol registration.");
        }
    }

    private void RegisterFileAssociations()
    {
        // Advertise Octadock in Explorer's "Open with" list for the previewable file
        // types. Always-on for now (no setting yet); per-user registration under
        // HKCU needs no elevation, and it is refreshed on every startup so the
        // command tracks the current executable path after a move or update.
        try
        {
            var associations =
                Services.GetRequiredService<Octadock.Platform.Windows.System.FileAssociationRegistration>();
            if (!associations.IsRegistered())
            {
                associations.Register();
            }
        }
        catch (Exception ex)
        {
            _logger!.LogWarning(ex, "Failed to register the Octadock file associations.");
        }
    }

    private void SyncStartupRegistration(ISettingsService settings)
    {
        try
        {
            var startup = Services.GetRequiredService<IStartupRegistration>();
            bool wanted = settings.Current.General.LaunchAtLogin;
            if (wanted && !startup.IsEnabled())
            {
                startup.Enable();
            }
            else if (!wanted)
            {
                startup.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger!.LogWarning(ex, "Failed to synchronize launch-at-login registration.");
        }
    }

    private void PostAsyncToDispatcher(Func<Task> work, string description)
    {
        try
        {
            Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await work().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to process {Description}.", description);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to schedule {Description} on the dispatcher.", description);
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;
        _shutdownCts.Cancel();
        _logger?.LogInformation("Octadock shutting down.");

        CompletePendingLaunches(CommandResult.Fail("Octadock is shutting down."));

        try
        {
            _retentionTimer?.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error stopping the retention timer.");
        }

        try
        {
            _aiSessionActivityWatchers?.Dispose();
            _aiSessionExitWatcher?.Dispose();
            _aiSessionOverlay?.Stop();
            _aiSessionDiscovery?.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error stopping AI session services.");
        }

        try
        {
            _clipboardHistory?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error stopping the clipboard history service.");
        }

        if (_startupTask?.IsFaulted == true)
        {
            _logger?.LogDebug(_startupTask.Exception, "Startup task faulted before shutdown completed.");
        }

        try
        {
            _hotkeys?.UnregisterAll();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error unregistering hotkeys.");
        }

        try
        {
            _pins?.CloseAll();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error closing pins.");
        }

        try
        {
            Dock?.Close();
            Dock = null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error closing the dock capsule.");
        }

        _tray?.Dispose();
        _theme?.Dispose();

        try
        {
            _singleInstance?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error disposing the single-instance guard.");
        }

        // Fold the WAL into the base file on the way out so nothing is lost if
        // the NEXT session ends badly (force kill, crash, power loss).
        try
        {
            Services.GetService<IOctadockDatabase>()?.CheckpointAsync()
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Final WAL checkpoint failed.");
        }

        // The service provider itself is disposed by Program.Main after Run returns.
        _shutdownCts.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Registers the global dispatcher-exception handler. Called by
    /// <c>Program.Main</c> after the <see cref="App"/> is constructed. Logs and
    /// notifies but keeps the app running.
    /// </summary>
    public void HookGlobalExceptionHandling()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled dispatcher exception.");
        TryWriteCrashReport(e.Exception, "DispatcherUnhandledException");
        TryNotify("Something went wrong", "Octadock hit an unexpected error but is still running.");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.LogCritical(ex, "Unhandled domain exception (terminating: {Terminating}).", e.IsTerminating);
            TryWriteCrashReport(ex, "AppDomainUnhandledException", e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception.");
        TryWriteCrashReport(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    private void TryWriteCrashReport(Exception exception, string source, bool isTerminating = false)
    {
        try
        {
            Services.GetService<CrashReportService>()?.TryWrite(exception, source, isTerminating);
        }
        catch
        {
            // Best-effort only: crash reporting must never become the crash.
        }
    }

    private void TryNotify(string title, string message)
    {
        try
        {
            Services.GetService<INotificationService>()?.Notify(title, message, NotificationKind.Error);
        }
        catch
        {
            // Best-effort only.
        }
    }

    private void CompletePendingLaunches(CommandResult result)
    {
        lock (_launchArgsGate)
        {
            while (_pendingLaunchArgs.TryDequeue(out PendingLaunchArgs? pending))
            {
                pending.Completion.TrySetResult(result);
            }
        }
    }

    private sealed class PendingLaunchArgs(string[] arguments, string? workingDirectory)
    {
        public string[] Arguments { get; } = arguments;

        public string? WorkingDirectory { get; } = workingDirectory;

        public TaskCompletionSource<CommandResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

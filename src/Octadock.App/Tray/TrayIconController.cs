using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Pins;
using Octadock.App.Preview;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Licensing;
using Octadock.Core.Recording;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace Octadock.App.Tray;

/// <summary>
/// Owns the Windows tray (notification-area) icon and its context menu, wiring each
/// entry to the command dispatcher, capture coordinator and window presenter. Also
/// implements <see cref="INotificationSink"/> so the notification service can raise
/// balloon toasts through the same icon. Left-clicking opens the all-in-one HUD.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconController : INotificationSink, IDisposable
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ICaptureCoordinator _coordinator;
    private readonly IWindowPresenter _presenter;
    private readonly IShelfService _shelf;
    private readonly IPinService _pins;
    private readonly FilePreviewService _filePreview;
    private readonly ISettingsService _settings;
    private readonly NotificationService _notifications;
    private readonly ILicenseGate _licenseGate;
    private readonly WindowPresenter? _windowPresenter;
    private readonly ILogger<TrayIconController> _logger;

    private readonly RecordingController _recording;

    private DispatcherTimer? _licenseTooltipTimer;
    private Forms.NotifyIcon? _icon;
    private Forms.ContextMenuStrip? _contextMenu;
    private Forms.ToolStripMenuItem? _pauseItem;
    private Forms.ToolStripMenuItem? _recordItem;
    private Forms.ToolStripMenuItem? _recordAreaItem;
    private Forms.ToolStripMenuItem? _dockItem;
    private DrawingIcon? _nativeIcon;
    private Action? _notificationClickAction;
    private bool _paused;
    private bool _disposed;

    /// <summary>Creates the tray controller.</summary>
    public TrayIconController(
        ICommandDispatcher dispatcher,
        ICaptureCoordinator coordinator,
        IWindowPresenter presenter,
        IShelfService shelf,
        IPinService pins,
        FilePreviewService filePreview,
        ISettingsService settings,
        NotificationService notifications,
        RecordingController recording,
        ILicenseGate licenseGate,
        ILogger<TrayIconController> logger)
    {
        _dispatcher = dispatcher;
        _coordinator = coordinator;
        _presenter = presenter;
        _shelf = shelf;
        _pins = pins;
        _filePreview = filePreview;
        _settings = settings;
        _notifications = notifications;
        _recording = recording;
        _licenseGate = licenseGate;
        _windowPresenter = presenter as WindowPresenter;
        _logger = logger;
    }

    /// <summary>True while global capture is paused via the tray menu.</summary>
    public bool IsPaused => _paused;

    /// <summary>Creates and shows the tray icon. Call on the UI thread during startup.</summary>
    public void Initialize()
    {
        DrawingIcon? nativeIcon = LoadNativeIcon();
        _nativeIcon = nativeIcon;
        _contextMenu = BuildContextMenu();

        _icon = new Forms.NotifyIcon
        {
            Text = "Octadock",
            Icon = nativeIcon ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = _contextMenu,
            Visible = ShouldShowTrayIcon(_settings.Current),
        };

        _icon.MouseUp += OnTrayMouseUp;
        _icon.BalloonTipClicked += OnTrayBalloonTipClicked;
        _icon.BalloonTipClosed += OnTrayBalloonTipClosed;

        // Route balloons through this icon.
        _notifications.SetSink(this);

        _logger.LogInformation(
            "Native WinForms tray icon initialized (visible: {Visible}, native icon: {NativeIcon}).",
            _icon.Visible,
            nativeIcon is not null);

        _settings.Changed += OnSettingsChanged;

        // Ambient trial/license status (WS5, R31): the tray tooltip always reflects the
        // current state, refreshed on a gate refusal, on menu-open, and hourly so a
        // multi-day trial countdown stays current without any user interaction.
        RefreshLicenseTooltip();
        _licenseGate.Refused += OnLicenseRefused;
        _licenseTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        _licenseTooltipTimer.Tick += (_, _) => RefreshLicenseTooltip();
        _licenseTooltipTimer.Start();
    }

    private void OnLicenseRefused(object? sender, LicenseState state)
        => RunOnUiThread(RefreshLicenseTooltip, "refresh license tooltip");

    /// <summary>Sets the tray tooltip to the current trial/license status.</summary>
    private void RefreshLicenseTooltip()
    {
        if (_icon is null)
        {
            return;
        }

        try
        {
            _icon.Text = LicenseStatusFormatter.TrayTooltip(_licenseGate.State, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh the tray license tooltip.");
        }
    }

    /// <inheritdoc />
    public void ShowBalloon(string title, string message, NotificationKind kind, Action? clickAction = null)
    {
        Forms.ToolTipIcon icon = kind switch
        {
            NotificationKind.Success => Forms.ToolTipIcon.Info,
            NotificationKind.Warning => Forms.ToolTipIcon.Warning,
            NotificationKind.Error => Forms.ToolTipIcon.Error,
            _ => Forms.ToolTipIcon.Info,
        };

        RunOnUiThread(
            () =>
            {
                if (_icon is null)
                {
                    return;
                }

                try
                {
                    _notificationClickAction = clickAction;
                    _icon.BalloonTipTitle = title;
                    _icon.BalloonTipText = message;
                    _icon.BalloonTipIcon = icon;
                    _icon.ShowBalloonTip(5000);
                }
                catch (Exception ex)
                {
                    _notificationClickAction = null;
                    _logger.LogDebug(ex, "Failed to show a tray notification.");
                }
            },
            "show tray notification");
    }

    private void OnTrayBalloonTipClicked(object? sender, EventArgs e)
    {
        Action? action = _notificationClickAction;
        _notificationClickAction = null;
        if (action is null)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification click action failed.");
        }
    }

    private void OnTrayBalloonTipClosed(object? sender, EventArgs e)
        => _notificationClickAction = null;

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        RunOnUiThread(
            () => ApplyTrayVisibility(ShouldShowTrayIcon(e.Settings)),
            "apply tray visibility");
    }

    private void ApplyTrayVisibility(bool isVisible)
    {
        if (_icon is null)
        {
            return;
        }

        if (!isVisible)
        {
            CloseContextMenu();
        }

        _icon.Visible = isVisible;
    }

    private void OnTrayMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
        {
            ShowNativeContextMenu();
            return;
        }

        if (e.Button == Forms.MouseButtons.Left)
        {
            if (GuardPaused())
            {
                return;
            }

            RunMenuAction("All-in-One", () => _presenter.ShowAllInOneHud());
        }
    }

    private void ShowNativeContextMenu()
    {
        if (_disposed || _contextMenu is null)
        {
            return;
        }

        if (_contextMenu.Visible)
        {
            return;
        }

        try
        {
            PrepareContextMenuForOpen();
            _contextMenu.Show(Forms.Cursor.Position);
            _logger.LogDebug("Tray context menu opened via native NotifyIcon right-click.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show the tray context menu at the cursor.");
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
        => PrepareContextMenuForOpen();

    private void PrepareContextMenuForOpen()
    {
        if (_contextMenu is null)
        {
            return;
        }

        RefreshLicenseTooltip();

        if (_pauseItem is not null)
        {
            _pauseItem.Text = _paused ? "Resume Capture" : "Pause Capture";
        }

        if (_recordItem is not null)
        {
            _recordItem.Text = _recording.IsRecording ? "Stop Recording" : "Record";
        }

        if (_recordAreaItem is not null)
        {
            _recordAreaItem.Enabled = !_recording.IsRecording;
        }

        if (_dockItem is not null)
        {
            _dockItem.Text = _settings.Current.Dock.Enabled ? "Hide Dock" : "Show Dock";
        }
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
        };
        menu.Opening += OnContextMenuOpening;

        menu.Items.Add(CaptureItem("Capture Area", () => _coordinator.CaptureAreaAsync(DefaultAction())));
        menu.Items.Add(CaptureItem("Capture Window", () => _coordinator.CaptureWindowAsync(DefaultAction())));
        menu.Items.Add(CaptureItem("Capture Fullscreen", () => _coordinator.CaptureFullscreenAsync(DefaultAction(), null, false)));
        menu.Items.Add(CaptureItem("Capture All Monitors", () => _coordinator.CaptureFullscreenAsync(DefaultAction(), null, true)));
        menu.Items.Add(CaptureItem("Capture Previous Area", () => _coordinator.CapturePreviousAreaAsync(DefaultAction())));
        menu.Items.Add(CaptureItem("Scrolling Capture", () => _coordinator.CaptureScrollingAsync(DefaultAction())));
        menu.Items.Add(DispatchItem("All-in-One", CommandType.AllInOne));
        menu.Items.Add(DispatchItem("OCR Region", CommandType.CaptureText));
        menu.Items.Add(DispatchItem("Read Region Aloud", CommandType.ReadAloud));
        menu.Items.Add(DispatchItem(
            "Explain Region Aloud",
            CommandType.ReadAloud,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["explain"] = "true" }));
        _recordItem = RecordingItem();
        menu.Items.Add(_recordItem);
        _recordAreaItem = RecordingAreaItem();
        menu.Items.Add(_recordAreaItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(AsyncActionItem("Open a File...", OpenFileForPreviewAsync));
        menu.Items.Add(ActionItem("Open History", () => _presenter.ShowHistory()));
        menu.Items.Add(ActionItem("Clipboard History", () => _presenter.ShowClipboardHistory()));
        menu.Items.Add(ActionItem("Text Tools", () => _presenter.ShowTextTools()));
        menu.Items.Add(ActionItem("Context", () => _windowPresenter?.ShowContext()));
        menu.Items.Add(AsyncActionItem("Restore Recently Closed", () => _shelf.RestoreRecentlyClosedAsync()));
        menu.Items.Add(ActionItem("Show All Pins", ShowAllPins));
        _dockItem = AsyncActionItem("Hide Dock", ToggleDockAsync);
        menu.Items.Add(_dockItem);
        menu.Items.Add(ActionItem("Account & Billing", () => _presenter.ShowSettings("account")));
        menu.Items.Add(ActionItem("Settings", () => _presenter.ShowSettings()));

        _pauseItem = new Forms.ToolStripMenuItem("Pause Capture");
        _pauseItem.Click += (_, _) => TogglePause();
        menu.Items.Add(_pauseItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        menu.Items.Add(ActionItem("About", ShowAbout));
        menu.Items.Add(ActionItem("Exit Octadock", QuitApplication));

        return menu;
    }

    private Forms.ToolStripMenuItem CaptureItem(string header, Func<Task> action)
    {
        var item = new Forms.ToolStripMenuItem(header);
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            if (GuardPaused())
            {
                return;
            }

            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray capture action '{Header}' failed.", header);
            }
        };
        return item;
    }

    private Forms.ToolStripMenuItem DispatchItem(
        string header, CommandType type, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var item = new Forms.ToolStripMenuItem(header);
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            if (GuardPaused())
            {
                return;
            }

            try
            {
                await _dispatcher.DispatchAsync(OctadockCommand.Create(type, parameters)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray dispatch '{Header}' failed.", header);
            }
        };
        return item;
    }

    private Forms.ToolStripMenuItem ActionItem(string header, Action action)
    {
        var item = new Forms.ToolStripMenuItem(header);
        item.Click += (_, _) => RunMenuAction(header, action);
        return item;
    }

    private Forms.ToolStripMenuItem AsyncActionItem(string header, Func<Task> action)
    {
        var item = new Forms.ToolStripMenuItem(header);
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray action '{Header}' failed.", header);
            }
        };
        return item;
    }

    private Forms.ToolStripMenuItem RecordingItem()
    {
        var item = new Forms.ToolStripMenuItem("Record");
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            if (!_recording.IsRecording && GuardPaused())
            {
                return;
            }

            try
            {
                await _recording.ToggleAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray action 'Record' failed.");
            }
        };
        return item;
    }

    private Forms.ToolStripMenuItem RecordingAreaItem()
    {
        var item = new Forms.ToolStripMenuItem("Record Area");
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            if (_recording.IsRecording || GuardPaused())
            {
                return;
            }

            try
            {
                await _recording.ToggleAsync(new RecordingStartRequest { PromptForRegion = true }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray action 'Record Area' failed.");
            }
        };
        return item;
    }

    private void RunMenuAction(string header, Action action)
    {
        CloseContextMenu();

        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray action '{Header}' failed.", header);
        }
    }

    private void CloseContextMenu()
    {
        if (_contextMenu?.Visible == true)
        {
            _contextMenu.Close();
        }
    }

    private void RunOnUiThread(Action action, string operation)
    {
        Application? app = Application.Current;
        Dispatcher? dispatcher = app?.Dispatcher;

        void RunGuarded()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to {Operation}.", operation);
            }
        }

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RunGuarded();
        }
        else
        {
            _ = dispatcher.BeginInvoke(RunGuarded);
        }
    }

    private bool GuardPaused()
    {
        if (_paused)
        {
            ShowBalloon("Octadock is paused", "Resume capture from the tray menu.", NotificationKind.Info);
            return true;
        }

        return false;
    }

    private PostCaptureAction DefaultAction() => _settings.Current.Capture.DefaultAction;

    private static bool ShouldShowTrayIcon(Octadock.Core.Settings.OctadockSettings settings)
        => settings.General.ShowTrayIcon || !settings.General.ShowTaskbarIcon || !settings.Dock.Enabled;

    private void TogglePause()
    {
        _paused = !_paused;
        if (_pauseItem is not null)
        {
            _pauseItem.Text = _paused ? "Resume Capture" : "Pause Capture";
        }

        ShowBalloon(
            _paused ? "Capture paused" : "Capture resumed",
            _paused ? "Shortcuts and tray captures are paused." : "Octadock is capturing again.",
            NotificationKind.Info);
    }

    private async Task OpenFileForPreviewAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a file in Octadock",
            Filter =
                "Previewable files|*.csv;*.tsv;*.txt;*.log;*.md;*.json;*.xml;*.yaml;*.yml;*.toml;*.ini;*.cfg;*.cs;*.js;*.ts;*.jsx;*.tsx;*.py;*.rb;*.go;*.rs;*.java;*.c;*.cpp;*.h;*.css;*.html;*.htm;*.sql;*.sh;*.ps1;*.bat;*.csproj;*.sln;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.ico" +
                "|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            await _filePreview.PreviewAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private async Task ToggleDockAsync()
    {
        bool enableDock = !_settings.Current.Dock.Enabled;
        await _settings.UpdateAsync(s => s with
        {
            Dock = s.Dock with { Enabled = enableDock },
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Brings every open pin back on-screen, clamping any that were stranded off the
    /// visible desktop after a monitor layout change. The injected <see cref="IPinService"/>
    /// is the same singleton as the concrete <see cref="PinService"/>, so we reach the
    /// clamp helper through a cast rather than resolving a second instance.
    /// </summary>
    private void ShowAllPins()
    {
        if (_pins is PinService pinService)
        {
            pinService.GatherAllOnScreen();
        }
        else
        {
            // The DI registration always supplies the concrete PinService, so this
            // branch is defensive only; nothing on IPinService can gather the pins.
            _logger.LogWarning("Show All Pins is unavailable: pin service is not the concrete PinService.");
        }
    }

    private void ShowAbout()
    {
        if (_windowPresenter is not null)
        {
            _windowPresenter.ShowAbout();
        }
        else
        {
            _presenter.ShowSettings("advanced");
        }
    }

    private static void QuitApplication()
    {
        Application.Current?.Shutdown();
    }

    private static DrawingIcon? LoadNativeIcon()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            return string.IsNullOrWhiteSpace(processPath)
                ? null
                : DrawingIcon.ExtractAssociatedIcon(processPath);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _settings.Changed -= OnSettingsChanged;
        _licenseGate.Refused -= OnLicenseRefused;
        _licenseTooltipTimer?.Stop();
        _licenseTooltipTimer = null;

        if (_icon is not null)
        {
            _icon.MouseUp -= OnTrayMouseUp;
            _icon.BalloonTipClicked -= OnTrayBalloonTipClicked;
            _icon.BalloonTipClosed -= OnTrayBalloonTipClosed;
            _icon.ContextMenuStrip = null;
            _icon.Visible = false;
        }

        if (_contextMenu is not null)
        {
            _contextMenu.Opening -= OnContextMenuOpening;
            CloseContextMenu();
            _contextMenu.Dispose();
        }

        _icon?.Dispose();
        _nativeIcon?.Dispose();
        _notifications.ClearSink(this);
        _icon = null;
        _contextMenu = null;
        _pauseItem = null;
        _recordItem = null;
        _dockItem = null;
        _nativeIcon = null;
        _notificationClickAction = null;
    }
}

using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Ai;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;

namespace Octadock.App.Tray;

/// <summary>
/// Owns the Windows tray (notification-area) icon and its context menu, wiring each
/// entry to the command dispatcher, capture coordinator and window presenter. Also
/// implements <see cref="INotificationSink"/> so the notification service can raise
/// balloon toasts through the same icon. Left-clicking toggles the capture Shelf.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrayIconController : INotificationSink, IDisposable
{
    private readonly ICommandDispatcher _dispatcher;
    private readonly ICaptureCoordinator _coordinator;
    private readonly IWindowPresenter _presenter;
    private readonly IShelfService _shelf;
    private readonly ISettingsService _settings;
    private readonly NotificationService _notifications;
    private readonly WindowPresenter? _windowPresenter;
    private readonly CaptureCoordinator? _captureCoordinator;
    private readonly ILogger<TrayIconController> _logger;

    private readonly RecordingController _recording;

    private Forms.NotifyIcon? _icon;
    private Forms.ContextMenuStrip? _contextMenu;
    private Forms.ToolStripMenuItem? _recordItem;
    private DrawingIcon? _nativeIcon;
    private Action? _notificationClickAction;
    private bool _disposed;

    /// <summary>Creates the tray controller.</summary>
    public TrayIconController(
        ICommandDispatcher dispatcher,
        ICaptureCoordinator coordinator,
        IWindowPresenter presenter,
        IShelfService shelf,
        ISettingsService settings,
        NotificationService notifications,
        RecordingController recording,
        ILogger<TrayIconController> logger)
    {
        _dispatcher = dispatcher;
        _coordinator = coordinator;
        _presenter = presenter;
        _shelf = shelf;
        _settings = settings;
        _notifications = notifications;
        _recording = recording;
        _windowPresenter = presenter as WindowPresenter;
        _captureCoordinator = coordinator as CaptureCoordinator;
        _logger = logger;
    }

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

        _icon.Text = "Octadock · local capture workspace";
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
            RunMenuAction("Shelf", () => _shelf.ToggleVisibility());
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


        if (_recordItem is not null)
        {
            _recordItem.Text = _recording.IsRecording ? "Stop Recording (Beta)" : LabelFor(TrayMenuEntry.RecordToggle);
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

        // One Capture submenu holds every capture verb so the root menu stays short.
        var capture = new Forms.ToolStripMenuItem("Capture");
        foreach (TrayMenuEntry entry in CaptureSubmenuPlan)
        {
            capture.DropDownItems.Add(BuildEntry(entry));
        }

        menu.Items.Add(capture);
        menu.Items.Add(BuildEntry(TrayMenuEntry.Dictate));

        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (TrayMenuEntry entry in LibraryGroupPlan)
        {
            menu.Items.Add(BuildEntry(entry));
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (TrayMenuEntry entry in AppGroupPlan)
        {
            menu.Items.Add(BuildEntry(entry));
        }

        return menu;
    }

    private Forms.ToolStripMenuItem BuildEntry(TrayMenuEntry entry) => entry switch
    {
        TrayMenuEntry.CaptureArea => CaptureItem(entry, () => _coordinator.CaptureAreaAsync(DefaultAction())),
        TrayMenuEntry.CaptureWindow => CaptureItem(entry, () => _coordinator.CaptureWindowAsync(DefaultAction())),
        TrayMenuEntry.CaptureFullScreen => CaptureItem(entry, () => _coordinator.CaptureFullscreenAsync(DefaultAction(), null, false)),
        TrayMenuEntry.CaptureAllMonitors => CaptureItem(entry, () => _coordinator.CaptureFullscreenAsync(DefaultAction(), null, true)),
        TrayMenuEntry.CapturePreviousArea => CaptureItem(entry, () => _coordinator.CapturePreviousAreaAsync(DefaultAction())),
        TrayMenuEntry.CaptureTimer => CaptureItem(entry, CaptureTimerAsync),
        TrayMenuEntry.CaptureScrolling => CaptureItem(entry, () => _coordinator.CaptureScrollingAsync(DefaultAction())),
        TrayMenuEntry.OcrRegion => DispatchItem(entry, CommandType.CaptureText),
        TrayMenuEntry.RecordToggle => RecordToggleItem(),
        TrayMenuEntry.Dictate => DispatchItem(entry, CommandType.Dictation),
        TrayMenuEntry.Shelf => ActionItem(entry, () => _shelf.ToggleVisibility()),
        TrayMenuEntry.History => ActionItem(entry, () => _presenter.ShowHistory()),
        TrayMenuEntry.Clipboard => ActionItem(entry, () => _presenter.ShowClipboardHistory()),
        TrayMenuEntry.Context => ActionItem(entry, () => _windowPresenter?.ShowContext()),
        TrayMenuEntry.UseWithAi => ActionItem(entry, () => _presenter.ShowAiActions(AgentReviewLaunch.FromTray())),
        TrayMenuEntry.Settings => ActionItem(entry, () => _presenter.ShowSettings()),
        TrayMenuEntry.About => ActionItem(entry, ShowAbout),
        TrayMenuEntry.Exit => ActionItem(entry, QuitApplication),
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    private Task CaptureTimerAsync()
        => _captureCoordinator?.CaptureSelfTimerAsync(DefaultAction(), null) ?? Task.CompletedTask;

    private Forms.ToolStripMenuItem CaptureItem(TrayMenuEntry entry, Func<Task> action)
    {
        string header = LabelFor(entry);
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
                _logger.LogError(ex, "Tray capture action '{Header}' failed.", header);
            }
        };
        return item;
    }

    private Forms.ToolStripMenuItem DispatchItem(TrayMenuEntry entry, CommandType type)
    {
        string header = LabelFor(entry);
        var item = new Forms.ToolStripMenuItem(header);
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            try
            {
                await _dispatcher.DispatchAsync(OctadockCommand.Create(type)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray dispatch '{Header}' failed.", header);
            }
        };
        return item;
    }

    private Forms.ToolStripMenuItem ActionItem(TrayMenuEntry entry, Action action)
    {
        var item = new Forms.ToolStripMenuItem(LabelFor(entry));
        item.Click += (_, _) => RunMenuAction(LabelFor(entry), action);
        return item;
    }

    private Forms.ToolStripMenuItem RecordToggleItem()
    {
        var item = new Forms.ToolStripMenuItem(LabelFor(TrayMenuEntry.RecordToggle));
        item.Click += async (_, _) =>
        {
            CloseContextMenu();

            try
            {
                await _recording.ToggleAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tray action 'Record' failed.");
            }
        };
        _recordItem = item;
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

    private PostCaptureAction DefaultAction() => _settings.Current.Capture.DefaultAction;

    /// <summary>The logical entries of the streamlined tray menu (the menu construction seam).</summary>
    internal enum TrayMenuEntry
    {
        CaptureArea,
        CaptureWindow,
        CaptureFullScreen,
        CaptureAllMonitors,
        CapturePreviousArea,
        CaptureTimer,
        CaptureScrolling,
        OcrRegion,
        RecordToggle,
        Dictate,
        Shelf,
        History,
        Clipboard,
        Context,
        UseWithAi,
        Settings,
        About,
        Exit,
    }

    /// <summary>The Capture submenu entries, in order.</summary>
    internal static IReadOnlyList<TrayMenuEntry> CaptureSubmenuPlan { get; } =
    [
        TrayMenuEntry.CaptureArea,
        TrayMenuEntry.CaptureWindow,
        TrayMenuEntry.CaptureFullScreen,
        TrayMenuEntry.CaptureAllMonitors,
        TrayMenuEntry.CapturePreviousArea,
        TrayMenuEntry.CaptureTimer,
        TrayMenuEntry.CaptureScrolling,
        TrayMenuEntry.OcrRegion,
        TrayMenuEntry.RecordToggle,
    ];

    /// <summary>The library group entries, in order (after Dictate).</summary>
    internal static IReadOnlyList<TrayMenuEntry> LibraryGroupPlan { get; } =
    [
        TrayMenuEntry.Shelf,
        TrayMenuEntry.History,
        TrayMenuEntry.Clipboard,
        TrayMenuEntry.Context,
    ];

    /// <summary>The application group entries, in order.</summary>
    internal static IReadOnlyList<TrayMenuEntry> AppGroupPlan { get; } =
    [
        TrayMenuEntry.UseWithAi,
        TrayMenuEntry.Settings,
        TrayMenuEntry.About,
        TrayMenuEntry.Exit,
    ];

    /// <summary>The display label for a tray entry (WinForms mnemonics are doubled where needed).</summary>
    internal static string LabelFor(TrayMenuEntry entry) => entry switch
    {
        TrayMenuEntry.CaptureArea => "Area",
        TrayMenuEntry.CaptureWindow => "Window",
        TrayMenuEntry.CaptureFullScreen => "Full screen",
        TrayMenuEntry.CaptureAllMonitors => "All monitors",
        TrayMenuEntry.CapturePreviousArea => "Previous area",
        TrayMenuEntry.CaptureTimer => "Timer",
        TrayMenuEntry.CaptureScrolling => "Scrolling — manual vertical (Beta)",
        TrayMenuEntry.OcrRegion => "OCR",
        TrayMenuEntry.RecordToggle => "Record (Beta)",
        TrayMenuEntry.Dictate => "Dictate",
        TrayMenuEntry.Shelf => "Shelf",
        TrayMenuEntry.History => "History",
        TrayMenuEntry.Clipboard => "Clipboard",
        TrayMenuEntry.Context => "Context",
        TrayMenuEntry.UseWithAi => "Local export…",
        TrayMenuEntry.Settings => "Settings…",
        TrayMenuEntry.About => "About Octadock",
        TrayMenuEntry.Exit => "Exit Octadock",
        _ => throw new ArgumentOutOfRangeException(nameof(entry)),
    };

    private static bool ShouldShowTrayIcon(Octadock.Core.Settings.OctadockSettings settings)
        => settings.General.ShowTrayIcon || !settings.General.ShowTaskbarIcon || !settings.Dock.Enabled;

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
        _recordItem = null;
        _nativeIcon = null;
        _notificationClickAction = null;
    }
}

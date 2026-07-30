using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.About;
using Octadock.App.Ai;
using Octadock.App.CaptureUx;
using Octadock.App.Clipboard;
using Octadock.App.FirstRun;
using Octadock.App.History;
using Octadock.App.Settings;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Licensing;

namespace Octadock.App.Services;

/// <summary>
/// Optional seam implemented by the history feature module. When registered, the
/// presenter delegates <c>ShowHistory</c> to it; otherwise it opens Settings on the
/// History tab as a graceful fallback during bring-up.
/// </summary>
public interface IHistoryPresenter
{
    /// <summary>Shows (and focuses) the local history window.</summary>
    void ShowHistory();
}

/// <summary>
/// Optional seam implemented by the clipboard-history feature module. When
/// registered, the presenter delegates <c>ShowClipboardHistory</c> to it.
/// </summary>
public interface IClipboardHistoryPresenter
{
    /// <summary>Shows (and focuses) the clipboard history window.</summary>
    void ShowClipboardHistory();
}

/// <summary>
/// Optional seam implemented by the text-tools feature module. When registered,
/// the presenter delegates <c>ShowTextTools</c> to it.
/// </summary>
public interface ITextToolsPresenter
{
    /// <summary>Shows (and focuses) the text-transform toolbox window.</summary>
    void ShowTextTools();
}

/// <summary>
/// <see cref="IWindowPresenter"/>. Owns single instances of the non-modal windows
/// Octadock presents (Settings, First-run, About) and delegates History
/// to their feature modules when those are registered. Everything runs on the UI
/// dispatcher.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowPresenter : IWindowPresenter
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly ILogger<WindowPresenter> _logger;

    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private Octadock.App.Context.ContextWindow? _contextWindow;
    private AgentWorkspaceWindow? _agentWorkspaceWindow;

    /// <summary>Creates the window presenter.</summary>
    public WindowPresenter(IServiceProvider services, ISettingsService settings, ILogger<WindowPresenter> logger)
    {
        _services = services;
        _settings = settings;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher
        ?? throw new InvalidOperationException("The WPF application dispatcher is not available.");

    /// <inheritdoc />
    public void ShowHistory()
    {
        OnUi(() =>
        {
            if (_services.GetService(typeof(IHistoryPresenter)) is IHistoryPresenter history)
            {
                history.ShowHistory();
            }
            else
            {
                _logger.LogDebug("No history window registered; opening the History settings tab instead.");
                ShowSettingsCore("history");
            }
        });
    }

    /// <inheritdoc />
    public void ShowClipboardHistory()
    {
        OnUi(() =>
        {
            if (_services.GetService(typeof(IClipboardHistoryPresenter)) is IClipboardHistoryPresenter clipboard)
            {
                clipboard.ShowClipboardHistory();
            }
            else
            {
                _logger.LogDebug("No clipboard history window registered; opening Settings instead.");
                ShowSettingsCore("clipboard");
            }
        });
    }

    /// <inheritdoc />
    public void ShowTextTools()
    {
        OnUi(() =>
        {
            // Trial/license gate (WS5): text transforms are a paid feature post-expiry.
            // Resolved lazily to avoid a construction cycle (LicenseGate → IWindowPresenter).
            if (_services.GetService(typeof(ILicenseGate)) is ILicenseGate gate && !gate.Allow(GatedFeature.TextTools))
            {
                return;
            }

            if (_services.GetService(typeof(ITextToolsPresenter)) is ITextToolsPresenter tools)
            {
                tools.ShowTextTools();
            }
            else
            {
                _logger.LogDebug("No text tools window registered; ignoring ShowTextTools.");
            }
        });
    }

    /// <inheritdoc />
    public void ShowSettings(string? tab = null) => OnUi(() => ShowSettingsCore(tab));

    /// <inheritdoc />
    public async Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.Current.General.FirstRunCompleted)
        {
            return false;
        }

        return await Dispatcher.InvokeAsync(() =>
        {
            var window = ActivatorUtilities.CreateInstance<FirstRunWindow>(_services);
            PrepareUtilityWindow(window);
            window.ShowDialog();

            // "I have a license key" on first run → open Account & Billing once the
            // modal has closed, so the buyer can paste their key immediately (WS5).
            if (window.WantsLicenseEntry)
            {
                ShowSettingsCore("account");
            }
            else
            {
                // Activation nudge: first value is capture → Shelf, not a feature tour.
                _services.GetService<INotificationService>()?.Notify(
                    "Try your first capture",
                    "Press Ctrl+Shift+4 or the Dock Area button — it lands on the Shelf.",
                    NotificationKind.Info);
            }

            return true;
        }, DispatcherPriority.Normal, cancellationToken);
    }

    /// <summary>Shows the About window (used by the tray menu).</summary>
    public void ShowAbout()
    {
        OnUi(() =>
        {
            if (_aboutWindow is { IsVisible: true })
            {
                PrepareUtilityWindow(_aboutWindow);
                ActivateUtilityWindow(_aboutWindow);
                return;
            }

            _aboutWindow = ActivatorUtilities.CreateInstance<AboutWindow>(_services);
            AboutWindow window = _aboutWindow;
            PrepareUtilityWindow(window);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_aboutWindow, window))
                {
                    _aboutWindow = null;
                }
            };
            _aboutWindow.Show();
            ActivateUtilityWindow(_aboutWindow);
        });
    }

    /// <summary>Shows the Context window (used by the tray). A single reused instance.</summary>
    public void ShowContext()
    {
        OnUi(() =>
        {
            if (_contextWindow is { IsVisible: true })
            {
                PrepareUtilityWindow(_contextWindow);
                _contextWindow.PlaceOnCursorScreen();
                ActivateUtilityWindow(_contextWindow);
                return;
            }

            _contextWindow = ActivatorUtilities.CreateInstance<Octadock.App.Context.ContextWindow>(_services);
            Octadock.App.Context.ContextWindow window = _contextWindow;
            PrepareUtilityWindow(window);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_contextWindow, window))
                {
                    _contextWindow = null;
                }
            };
            _contextWindow.Show();
            _contextWindow.PlaceOnCursorScreen();
            ActivateUtilityWindow(_contextWindow);
        });
    }

    /// <inheritdoc />
    public void ShowAiActions(OctadockCommand? launchCommand = null)
    {
        OnUi(() =>
        {
            Window? sourceOwner = FindReviewOwner(launchCommand);
            if (_agentWorkspaceWindow is { IsVisible: true })
            {
                PrepareUtilityWindow(_agentWorkspaceWindow);
                _agentWorkspaceWindow.Topmost = sourceOwner?.Topmost == true;
                ActivateUtilityWindow(_agentWorkspaceWindow);
                if (launchCommand is not null)
                {
                    _ = _agentWorkspaceWindow.ApplyLaunchCommandAsync(launchCommand);
                }
                return;
            }

            // Keep the stable ShowAiActions automation contract, but present the
            // evidence-rich review as a child of the invoking product surface.
            // The old text-action types remain compatibility adapters only.
            _agentWorkspaceWindow = ActivatorUtilities.CreateInstance<AgentWorkspaceWindow>(_services);
            AgentWorkspaceWindow window = _agentWorkspaceWindow;
            PrepareUtilityWindow(window);
            if (sourceOwner is not null)
            {
                window.Owner = sourceOwner;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowInTaskbar = false;
                window.Topmost = sourceOwner.Topmost;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_agentWorkspaceWindow, window))
                {
                    _agentWorkspaceWindow = null;
                }
            };
            window.Show();
            if (sourceOwner is not null)
            {
                // Owner gives WPF a DPI-correct CenterOwner calculation. Detach
                // immediately after first layout so closing a transient Shelf
                // or Context surface cannot also close the review.
                window.Owner = null;
                PrepareUtilityWindow(window);
            }
            ActivateUtilityWindow(window);
            _ = window.ApplyLaunchCommandAsync(launchCommand);
        });
    }

    private Window? FindReviewOwner(OctadockCommand? launchCommand)
    {
        IReadOnlyList<Window> visible = Application.Current?.Windows
            .OfType<Window>()
            .Where(window => window.IsVisible && !ReferenceEquals(window, _agentWorkspaceWindow))
            .ToList() ?? [];
        string? source = launchCommand?.Get(AgentReviewLaunch.ReviewSourceParameter)
            ?.Trim().ToLowerInvariant();
        List<Window> matching = visible.Where(window => MatchesReviewSource(window, source)).ToList();

        // Shelf and Dock deliberately never activate, so source identity and
        // pointer ownership must outrank IsActive. This keeps centering/z-order
        // bound to the surface that actually opened the review instead of
        // whichever app window last held keyboard focus.
        return matching.FirstOrDefault(window => window.IsMouseOver)
            ?? matching.FirstOrDefault(window => window.IsActive)
            ?? matching.LastOrDefault()
            ?? (source is null or "octadock"
                ? visible.FirstOrDefault(window => window.IsActive)
                : null);
    }

    private static bool MatchesReviewSource(Window window, string? source)
        => source switch
        {
            "shelf" => window is ShelfWindow,
            "context" => window is Octadock.App.Context.ContextWindow,
            "history" => window is HistoryWindow,
            "clipboard" => window is ClipboardHistoryWindow,
            "dock" => window is DockPill,
            _ => false,
        };

    private void ShowSettingsCore(string? tab)
    {
        if (_settingsWindow is { IsVisible: true })
        {
            PrepareUtilityWindow(_settingsWindow);
            _settingsWindow.SelectTab(tab);
            ActivateUtilityWindow(_settingsWindow);
            return;
        }

        _settingsWindow = ActivatorUtilities.CreateInstance<SettingsWindow>(_services);
        SettingsWindow window = _settingsWindow;
        PrepareUtilityWindow(window);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, window))
            {
                _settingsWindow = null;
            }
        };
        window.SelectTab(tab);
        window.Show();
        ActivateUtilityWindow(window);
    }

    private void PrepareUtilityWindow(Window window)
    {
        window.ShowInTaskbar = _settings.Current.General.ShowTaskbarIcon;
    }

    private static void ActivateUtilityWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    }

    private static void OnUi(Action action)
    {
        Dispatcher dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}

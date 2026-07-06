using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.About;
using Octadock.App.FirstRun;
using Octadock.App.Settings;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
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
/// Octadock presents (Settings, First-run, About) and delegates the HUD and History
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
    public void ShowAllInOneHud(
        CaptureMode? mode = null,
        PixelRect? preloadedRegion = null,
        int? preloadedWidth = null,
        int? preloadedHeight = null)
    {
        OnUi(() =>
        {
            if (_services.GetService(typeof(IHudService)) is IHudService hud)
            {
                hud.Show(mode, preloadedRegion, preloadedWidth, preloadedHeight);
            }
            else
            {
                _logger.LogDebug("No HUD service registered; ignoring ShowAllInOneHud.");
            }
        });
    }

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

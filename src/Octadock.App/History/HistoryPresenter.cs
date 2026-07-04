using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.Core.Abstractions;

namespace Octadock.App.History;

/// <summary>
/// <see cref="IHistoryPresenter"/>. Owns a single <see cref="HistoryWindow"/>
/// instance, showing and focusing it on the UI dispatcher. The
/// <c>WindowPresenter</c> delegates <c>ShowHistory</c> here when this module is
/// registered.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HistoryPresenter : IHistoryPresenter
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private HistoryWindow? _window;

    /// <summary>Creates the history presenter.</summary>
    public HistoryPresenter(IServiceProvider services, ISettingsService settings)
    {
        _services = services;
        _settings = settings;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher
        ?? throw new InvalidOperationException("The WPF application dispatcher is not available.");

    /// <inheritdoc />
    public void ShowHistory()
    {
        void Show()
        {
            if (_window is { IsVisible: true })
            {
                _window.ShowInTaskbar = _settings.Current.General.ShowTaskbarIcon;
                ActivateWindow(_window);
                return;
            }

            _window = ActivatorUtilities.CreateInstance<HistoryWindow>(_services);
            HistoryWindow window = _window;
            window.ShowInTaskbar = _settings.Current.General.ShowTaskbarIcon;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                {
                    _window = null;
                }
            };
            window.Show();
            ActivateWindow(window);
        }

        if (Dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            Dispatcher.Invoke(Show);
        }
    }

    private static void ActivateWindow(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    }
}

using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.Core.Abstractions;

namespace Octadock.App.TextTools;

/// <summary>
/// <see cref="ITextToolsPresenter"/>. Owns a single <see cref="TextToolsWindow"/>
/// instance, showing and focusing it on the UI dispatcher.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TextToolsPresenter : ITextToolsPresenter
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private TextToolsWindow? _window;

    /// <summary>Creates the presenter.</summary>
    public TextToolsPresenter(IServiceProvider services, ISettingsService settings)
    {
        _services = services;
        _settings = settings;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher
        ?? throw new InvalidOperationException("The WPF application dispatcher is not available.");

    /// <inheritdoc />
    public void ShowTextTools()
    {
        void Show()
        {
            if (_window is { IsVisible: true })
            {
                _window.ShowInTaskbar = _settings.Current.General.ShowTaskbarIcon;
                ActivateWindow(_window);
                return;
            }

            _window = ActivatorUtilities.CreateInstance<TextToolsWindow>(_services);
            TextToolsWindow window = _window;
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

using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// <see cref="IHudService"/>: owns a single all-in-one HUD window and the shared
/// <see cref="HudState"/> (remembered selection / preferences). Marshals to the UI
/// dispatcher, reuses an existing HUD if one is open, and optionally launches a
/// preselected mode.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class HudService : IHudService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<HudService> _logger;
    private readonly HudState _state = new();

    private HudWindow? _window;

    /// <summary>Creates the HUD service.</summary>
    public HudService(IServiceProvider services, ILogger<HudService> logger)
    {
        _services = services;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public void Show(CaptureMode? mode = null, PixelRect? preloadedRegion = null, int? preloadedWidth = null, int? preloadedHeight = null)
    {
        void ShowCore()
        {
            try
            {
                if (_window is { } existing)
                {
                    existing.ApplyPreload(preloadedRegion, preloadedWidth, preloadedHeight);

                    if (!existing.IsVisible)
                    {
                        existing.Show();
                    }

                    existing.Activate();
                    existing.ApplyMode(mode);
                    return;
                }

                var window = new HudWindow(_services, _state);
                window.Closed += OnWindowClosed;
                _window = window;
                window.ApplyPreload(preloadedRegion, preloadedWidth, preloadedHeight);
                window.Show();
                window.Activate();
                window.ApplyMode(mode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show the all-in-one HUD.");
            }
        }

        if (Dispatcher.CheckAccess())
        {
            ShowCore();
        }
        else
        {
            Dispatcher.BeginInvoke(ShowCore);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_window, sender))
        {
            _window = null;
        }
    }
}

using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;

namespace Octadock.App.CaptureUx;

/// <summary>
/// <see cref="IShelfService"/>: owns the single Capture Shelf window + its
/// <see cref="ShelfViewModel"/>. All work marshals to the UI dispatcher. Captures are
/// added as the newest active card; the shelf appears on the active monitor and
/// repositions itself as needed. Restore re-adds the most recently closed capture, and
/// <see cref="CloseAll"/> clears every card without deleting the underlying captures.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class ShelfService : IShelfService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ShelfService> _logger;

    private ShelfViewModel? _viewModel;
    private ShelfWindow? _window;
    private bool _userHidden;

    /// <summary>Creates the shelf service.</summary>
    public ShelfService(IServiceProvider services, ILogger<ShelfService> logger)
    {
        _services = services;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public Task<bool> ShowAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Dispatcher.InvokeAsync(() =>
        {
            try
            {
                ShelfWindow window = EnsureWindow();
                _viewModel!.Add(record);
                window.NotifyCaptureAdded();

                if (!_userHidden && !window.IsVisible)
                {
                    window.Show();
                }

                // Captures that arrive while the user has minimized the Shelf stay
                // quiet; the eye action in the capsule restores the populated stack.
                if (!_userHidden)
                {
                    window.Reposition();
                }

                window.Topmost = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show capture {Id} on the shelf.", record.Id);
                return false;
            }
        }).Task;
    }

    /// <inheritdoc />
    public Task<bool> RestoreRecentlyClosedAsync(CancellationToken cancellationToken = default)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                ShelfWindow window = EnsureWindow();
                bool restored = await _viewModel!
                    .RestoreRecentlyClosedAsync(cancellationToken)
                    .ConfigureAwait(true);
                if (restored)
                {
                    _userHidden = false;
                    if (!window.IsVisible)
                    {
                        window.Show();
                    }

                    window.Reposition();
                }

                return restored;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore the most recently closed shelf item.");
                return false;
            }
        }).Task.Unwrap();
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        void Close()
        {
            _userHidden = false;
            _viewModel?.CloseAll();
            _window?.Hide();
        }

        if (Dispatcher.CheckAccess())
        {
            Close();
        }
        else
        {
            Dispatcher.BeginInvoke(Close);
        }
    }

    /// <inheritdoc />
    public void ToggleVisibility()
    {
        void Toggle()
        {
            ShelfWindow window = EnsureWindow();
            if (window.IsVisible)
            {
                _userHidden = true;
                window.Hide();
                return;
            }

            if (_viewModel!.Items.Count == 0)
            {
                return;
            }

            _userHidden = false;
            window.RestoreFromPeek();
            window.Topmost = true;
            window.Reposition();
        }

        if (Dispatcher.CheckAccess())
        {
            Toggle();
        }
        else
        {
            Dispatcher.BeginInvoke(Toggle);
        }
    }

    /// <summary>Refreshes visible shelf thumbnails for an image file that was edited in place.</summary>
    public async Task RefreshSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            Task refresh = await Dispatcher.InvokeAsync(
                    () => _viewModel?.RefreshSourceAsync(sourcePath) ?? Task.CompletedTask,
                    DispatcherPriority.Normal,
                    cancellationToken)
                .Task
                .ConfigureAwait(false);

            await refresh.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh shelf thumbnails after saving {Path}.", sourcePath);
        }
    }

    private ShelfWindow EnsureWindow()
    {
        if (_window is { } existing && existing.IsLoaded)
        {
            return existing;
        }

        _viewModel ??= ActivatorUtilities.CreateInstance<ShelfViewModel>(_services);

        var monitors = _services.GetRequiredService<IMonitorService>();
        var settings = _services.GetRequiredService<ISettingsService>();
        var preview = _services.GetRequiredService<FilePreviewService>();
        var window = new ShelfWindow(_viewModel, monitors, settings, preview);
        window.MinimizeRequested += OnMinimizeRequested;
        window.Closed += OnWindowClosed;
        _window = window;
        return window;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_window, sender))
        {
            if (sender is ShelfWindow shelfWindow)
            {
                shelfWindow.MinimizeRequested -= OnMinimizeRequested;
            }

            _window = null;
        }
    }

    private void OnMinimizeRequested(object? sender, EventArgs e)
    {
        if (sender is not ShelfWindow window)
        {
            return;
        }

        _userHidden = true;
        window.Hide();
    }
}

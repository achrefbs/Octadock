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

                if (!window.IsVisible)
                {
                    window.Show();
                }

                // Bring to the active monitor's anchor and above other windows.
                window.Reposition();
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
        return Dispatcher.InvokeAsync(() =>
        {
            try
            {
                ShelfWindow window = EnsureWindow();
                bool restored = _viewModel!.RestoreRecentlyClosed();
                if (restored)
                {
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
        }).Task;
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        void Close()
        {
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
        window.Closed += OnWindowClosed;
        _window = window;
        return window;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_window, sender))
        {
            _window = null;
        }
    }
}

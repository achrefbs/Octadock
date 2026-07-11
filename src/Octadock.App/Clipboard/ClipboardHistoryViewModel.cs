using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Commands;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Clipboard;

/// <summary>A kind/favorite filter chip for the clipboard history window.</summary>
/// <param name="Label">Display label.</param>
/// <param name="Kinds">Kind filter, or null for all kinds.</param>
/// <param name="FavoritesOnly">True to show only favorites.</param>
public sealed record ClipFilterOption(
    string Label,
    IReadOnlyCollection<ClipboardClipKind>? Kinds,
    bool FavoritesOnly);

/// <summary>
/// The clipboard history window's view model: a searchable, filterable, paged
/// list of recorded clips with restore/favorite/delete/clear actions. Live
/// clipboard activity refreshes the list while the window is open.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ClipboardHistoryViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 100;

    private readonly IClipboardClipRepository _repository;
    private readonly IClipboardService _clipboard;
    private readonly ClipboardHistoryService _history;
    private readonly IStoragePaths _paths;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly IWindowPresenter _presenter;
    private readonly ILogger<ClipboardHistoryViewModel> _logger;

    private int _offset;
    private int _queryVersion;
    private bool _disposed;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ClipFilterOption _selectedFilter;

    [ObservableProperty]
    private ClipItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canLoadMore;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _monitorEnabled;

    /// <summary>Creates the view model.</summary>
    public ClipboardHistoryViewModel(
        IClipboardClipRepository repository,
        IClipboardService clipboard,
        ClipboardHistoryService history,
        IStoragePaths paths,
        ISettingsService settings,
        INotificationService notifications,
        IClock clock,
        IWindowPresenter presenter,
        ILogger<ClipboardHistoryViewModel> logger)
    {
        _repository = repository;
        _clipboard = clipboard;
        _history = history;
        _paths = paths;
        _settings = settings;
        _notifications = notifications;
        _clock = clock;
        _presenter = presenter;
        _logger = logger;

        Filters =
        [
            new ClipFilterOption("All", null, FavoritesOnly: false),
            new ClipFilterOption("Text", [ClipboardClipKind.Text], FavoritesOnly: false),
            new ClipFilterOption("Images", [ClipboardClipKind.Image], FavoritesOnly: false),
            new ClipFilterOption("Favorites", null, FavoritesOnly: true),
        ];
        _selectedFilter = Filters[0];
        _monitorEnabled = settings.Current.Clipboard.MonitorEnabled;

        _history.HistoryChanged += OnHistoryChanged;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>The filter chips.</summary>
    public IReadOnlyList<ClipFilterOption> Filters { get; }

    /// <summary>The visible clips, newest activity first.</summary>
    public ObservableCollection<ClipItemViewModel> Items { get; } = [];

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _history.HistoryChanged -= OnHistoryChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    partial void OnSearchTextChanged(string value) => _ = RefreshAsync();

    partial void OnSelectedFilterChanged(ClipFilterOption value) => _ = RefreshAsync();

    /// <summary>Reloads the first page with the current filter.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        int version = Interlocked.Increment(ref _queryVersion);
        IsBusy = true;
        try
        {
            _offset = 0;
            IReadOnlyList<ClipboardClipRecord> page = await QueryPageAsync(0).ConfigureAwait(true);
            if (version != _queryVersion || _disposed)
            {
                return;
            }

            Items.Clear();
            AppendPage(page);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            LogRefreshFailed(ex);
            StatusMessage = "Could not load clipboard history.";
        }
        finally
        {
            if (version == _queryVersion)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Loads the next page.</summary>
    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        int version = _queryVersion;
        try
        {
            IReadOnlyList<ClipboardClipRecord> page = await QueryPageAsync(_offset).ConfigureAwait(true);
            if (version != _queryVersion || _disposed)
            {
                return;
            }

            AppendPage(page);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            LogRefreshFailed(ex);
        }
    }

    /// <summary>Puts a clip back on the Windows clipboard.</summary>
    [RelayCommand]
    public void Copy(ClipItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            if (item.IsText)
            {
                _clipboard.SetText(item.Record.Text ?? string.Empty);
            }
            else if (item.Record.ImagePath is { Length: > 0 } imagePath)
            {
                _clipboard.SetImageFromFile(_paths.ToAbsolute(imagePath));
            }
            else
            {
                return;
            }

            _notifications.Notify("Copied", "The clip is back on your clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            LogClipActionFailed(ex, "copy");
            _notifications.Notify("Copy failed", "The clip could not be copied.", NotificationKind.Error);
        }
    }

    /// <summary>Opens one stored clip as preloaded evidence on the AI screen.</summary>
    [RelayCommand]
    public void UseWithAi(ClipItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = "Use clipboard item with AI",
            ["source"] = $"Clipboard history · {item.SourceLabel}",
            ["workflow"] = "choose",
        };
        if (item.IsText)
        {
            parameters["text"] = item.Record.Text ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(item.Record.ImagePath))
        {
            parameters["filepath"] = _paths.ToAbsolute(item.Record.ImagePath);
        }
        else
        {
            _notifications.Notify("AI", "This clipboard item no longer has usable content.", NotificationKind.Warning);
            return;
        }

        _presenter.ShowAiActions(OctadockCommand.Create(CommandType.AiActions, parameters));
        StatusMessage = "Clipboard evidence is ready on the AI screen. Choose what should happen next.";
    }

    /// <summary>Stars/unstars a clip. Favorites are never auto-trimmed.</summary>
    [RelayCommand]
    public async Task ToggleFavoriteAsync(ClipItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            ClipboardClipRecord updated = item.Record with { IsFavorite = !item.Record.IsFavorite };
            await _repository.UpdateAsync(updated).ConfigureAwait(true);
            item.Apply(updated);
        }
        catch (Exception ex)
        {
            LogClipActionFailed(ex, "favorite");
        }
    }

    /// <summary>Permanently removes a clip and its managed files.</summary>
    [RelayCommand]
    public async Task DeleteAsync(ClipItemViewModel? item)
    {
        item ??= SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            await _repository.HardDeleteAsync(item.Record.Id).ConfigureAwait(true);
            _history.DeleteClipFiles(item.Record);
            Items.Remove(item);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            LogClipActionFailed(ex, "delete");
            _notifications.Notify("Delete failed", "The clip could not be removed.", NotificationKind.Error);
        }
    }

    /// <summary>Permanently clears the whole clipboard history after confirmation.</summary>
    [RelayCommand]
    public async Task ClearAllAsync()
    {
        MessageBoxResult confirmed = MessageBox.Show(
            "Permanently delete the entire clipboard history? Favorites are removed too.",
            "Clear clipboard history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsBusy = true;
            IReadOnlyList<ClipboardClipRecord> all;
            do
            {
                all = await _repository.QueryAsync(
                    new ClipboardClipFilter { IncludeDeleted = true, Limit = 500 }).ConfigureAwait(true);
                foreach (ClipboardClipRecord clip in all)
                {
                    await _repository.HardDeleteAsync(clip.Id).ConfigureAwait(true);
                    _history.DeleteClipFiles(clip);
                }
            }
            while (all.Count > 0);

            Items.Clear();
            UpdateStatus();
            _notifications.Notify("Clipboard history cleared", "All clips were removed.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            LogClipActionFailed(ex, "clear");
            _notifications.Notify("Clear failed", "Some clips could not be removed.", NotificationKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Enables/disables the clipboard monitor from the window header toggle.</summary>
    [RelayCommand]
    public async Task ToggleMonitorAsync()
    {
        try
        {
            bool target = !_settings.Current.Clipboard.MonitorEnabled;
            await _settings.UpdateAsync(s => s with
            {
                Clipboard = s.Clipboard with { MonitorEnabled = target },
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LogClipActionFailed(ex, "toggle monitor");
        }
    }

    private async Task<IReadOnlyList<ClipboardClipRecord>> QueryPageAsync(int offset)
    {
        var filter = new ClipboardClipFilter
        {
            Kinds = SelectedFilter.Kinds,
            IsFavorite = SelectedFilter.FavoritesOnly ? true : null,
            SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            SortOrder = ClipboardClipSortOrder.LastSeenNewestFirst,
            Limit = PageSize,
            Offset = offset,
        };

        return await Task.Run(() => _repository.QueryAsync(filter)).ConfigureAwait(true);
    }

    private void AppendPage(IReadOnlyList<ClipboardClipRecord> page)
    {
        DateTimeOffset now = _clock.UtcNow;
        foreach (ClipboardClipRecord record in page)
        {
            var item = new ClipItemViewModel(record, _paths, now);
            Items.Add(item);
        }

        _offset += page.Count;
        CanLoadMore = page.Count == PageSize;

        // Thumbnails decode off the UI thread after the rows appear.
        ClipItemViewModel[] images = [.. Items.Where(i => i.IsImage && i.Thumbnail is null)];
        if (images.Length > 0)
        {
            _ = Task.Run(() =>
            {
                foreach (ClipItemViewModel image in images)
                {
                    image.LoadThumbnail();
                }
            });
        }
    }

    private void UpdateStatus()
    {
        IsEmpty = Items.Count == 0;
        StatusMessage = Items.Count == 0
            ? (MonitorEnabled
                ? "Nothing here yet — copy something and it will appear."
                : "Clipboard monitoring is turned off.")
            : $"{Items.Count} clip{(Items.Count == 1 ? string.Empty : "s")}";
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || _disposed)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                _ = RefreshAsync();
            }
        });
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || _disposed)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            MonitorEnabled = e.Settings.Clipboard.MonitorEnabled;
            UpdateStatus();
        });
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Refreshing clipboard history failed.")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Clipboard history action '{Action}' failed.")]
    private partial void LogClipActionFailed(Exception exception, string action);
}

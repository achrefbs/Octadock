using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Io;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.History;

/// <summary>
/// The history window's view model. Drives a searchable, filterable, paged view over
/// the capture library (via <see cref="ICaptureRepository.QueryAsync"/>) and provides
/// the per-item actions (open as a native pin, copy, save, soft-delete, restore),
/// a confirmed "clear history", and a "show deleted" toggle. All DB/image work is
/// marshalled off the UI thread; the bound collection is updated on the dispatcher.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class HistoryViewModel : ObservableObject
{
    private const int PageSize = 60;

    private readonly ICaptureRepository _captures;
    private readonly IPinRepository _pins;
    private readonly IImageLoadService _images;
    private readonly IStoragePaths _paths;
    private readonly IClipboardService _clipboard;
    private readonly IAnnotationService _annotation;
    private readonly IPinService _pinService;
    private readonly INotificationService _notifications;
    private readonly IActionRepository _actions;
    private readonly IWindowPresenter _presenter;
    private readonly ILogger<HistoryViewModel> _logger;

    private int _offset;
    private int _queryVersion;
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty]
    private HistoryFilterOption _selectedFilter = HistoryFilterOption.All[0];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showDeleted;

    [ObservableProperty]
    private DateTime? _createdAfterDate;

    [ObservableProperty]
    private DateTime? _createdBeforeDate;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CaptureItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _canLoadMore;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>Creates the history view model.</summary>
    public HistoryViewModel(
        ICaptureRepository captures,
        IPinRepository pins,
        IImageLoadService images,
        IStoragePaths paths,
        IClipboardService clipboard,
        IAnnotationService annotation,
        IPinService pinService,
        INotificationService notifications,
        IActionRepository actions,
        IWindowPresenter presenter,
        ILogger<HistoryViewModel> logger)
    {
        _captures = captures;
        _pins = pins;
        _images = images;
        _paths = paths;
        _clipboard = clipboard;
        _annotation = annotation;
        _pinService = pinService;
        _notifications = notifications;
        _actions = actions;
        _presenter = presenter;
        _logger = logger;
    }

    /// <summary>The captures currently shown.</summary>
    public ObservableCollection<CaptureItemViewModel> Items { get; } = [];

    /// <summary>The available type filters.</summary>
    public IReadOnlyList<HistoryFilterOption> Filters => HistoryFilterOption.All;

    public bool HasSelection => SelectedItem is not null;

    private bool CanUseApprovedMockup()
        => SelectedItem is { IsDeleted: false, HasApprovedMockup: true } item &&
           File.Exists(item.ApprovedMockupAbsolutePath);

    private bool CanUseWithAi() => SelectedItem is { IsDeleted: false };

    [RelayCommand(CanExecute = nameof(CanUseWithAi))]
    private void UseWithAi()
    {
        if (SelectedItem is not { IsDeleted: false } item)
        {
            return;
        }

        _presenter.ShowAiActions(OctadockCommand.Create(
            CommandType.AiActions,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["captureid"] = item.Id.ToString("D"),
                ["workflow"] = "choose",
                ["title"] = $"Use {item.FileName} with AI",
                ["target"] = item.SourceLabel,
            }));
        StatusMessage = "Capture is ready on the AI screen. Choose what should happen next.";
    }

    /// <summary>True when the current query returned nothing (drives the empty-state text).</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Loads (or reloads) the first page for the current filter/search.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        int version = Interlocked.Increment(ref _queryVersion);
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? old = Interlocked.Exchange(ref _refreshCts, cts);
        old?.Cancel();
        old?.Dispose();

        _offset = 0;
        SelectedItem = null;
        Items.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        IsBusy = true;
        try
        {
            await LoadPageCoreAsync(cts.Token, version).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (version != _queryVersion || cancellationToken.IsCancellationRequested)
        {
            // Superseded by a newer filter/search refresh.
        }
        catch (Exception ex)
        {
            if (version == _queryVersion)
            {
                _logger.LogError(ex, "Failed to load history page.");
                StatusMessage = "Failed to load history.";
            }
        }
        finally
        {
            if (version == _queryVersion)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMore() => LoadPageAsync();

    private async Task LoadPageAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        int version = _queryVersion;

        // B-7: link this page load to the current refresh CTS so a
        // filter/search refresh that starts mid-flight cancels it. Previously a
        // superseded load-more cleared IsBusy while the refresh was still
        // running, letting a second load-more race the refresh's first page and
        // append it twice.
        CancellationTokenSource linked;
        try
        {
            CancellationTokenSource? refreshCts = _refreshCts;
            linked = refreshCts is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, refreshCts.Token);
        }
        catch (ObjectDisposedException)
        {
            // A newer refresh disposed the CTS; that refresh owns the list now.
            return;
        }

        IsBusy = true;
        try
        {
            await LoadPageCoreAsync(linked.Token, version).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (version != _queryVersion || linked.IsCancellationRequested)
        {
            // Superseded by a newer filter/search refresh.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load history page.");
            StatusMessage = "Failed to load history.";
        }
        finally
        {
            linked.Dispose();
            if (version == _queryVersion)
            {
                IsBusy = false;
            }
        }
    }

    private async Task LoadPageCoreAsync(CancellationToken cancellationToken, int version)
    {
        HashSet<Guid>? pinnedCaptureIds = null;
        if (SelectedFilter.Kind == HistoryFilterKind.Pinned)
        {
            IReadOnlyList<PinRecord> pins = await _pins.GetAllAsync(cancellationToken).ConfigureAwait(true);
            pinnedCaptureIds = pins.Where(p => p.CaptureId is not null)
                .Select(p => p.CaptureId!.Value)
                .ToHashSet();

            if (pinnedCaptureIds.Count == 0)
            {
                ApplyPage([], scannedOffset: _offset, canLoadMore: false, cancellationToken, version);
                return;
            }
        }

        var visible = new List<CaptureRecord>(PageSize);
        int scannedOffset = _offset;
        bool canLoadMore;

        do
        {
            int pageOffset = scannedOffset;
            CaptureFilter filter = BuildFilter(scannedOffset);
            IReadOnlyList<CaptureRecord> results =
                await _captures.QueryAsync(filter, cancellationToken).ConfigureAwait(true);

            canLoadMore = results.Count >= PageSize;
            for (var i = 0; i < results.Count; i++)
            {
                CaptureRecord record = results[i];
                if (pinnedCaptureIds is null || pinnedCaptureIds.Contains(record.Id))
                {
                    visible.Add(record);
                    if (visible.Count >= PageSize)
                    {
                        scannedOffset = pageOffset + i + 1;
                        canLoadMore = i < results.Count - 1 || results.Count >= PageSize;
                        break;
                    }
                }
            }

            if (visible.Count < PageSize)
            {
                scannedOffset = pageOffset + results.Count;
            }
        }
        while (pinnedCaptureIds is not null && visible.Count < PageSize && canLoadMore);

        ApplyPage(visible, scannedOffset, canLoadMore, cancellationToken, version);
    }

    private void ApplyPage(
        IReadOnlyList<CaptureRecord> visible,
        int scannedOffset,
        bool canLoadMore,
        CancellationToken cancellationToken,
        int version)
    {
        if (version != _queryVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (CaptureRecord record in visible)
        {
            var item = new CaptureItemViewModel(record, _images, _paths);
            Items.Add(item);
            _ = item.EnsureThumbnailAsync(cancellationToken);
        }

        _offset = scannedOffset;
        CanLoadMore = canLoadMore;
        LoadMoreCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEmpty));
        StatusMessage = Items.Count == 0 ? "No captures match your filters." : $"{Items.Count} shown";
    }

    private CaptureFilter BuildFilter(int offset)
    {
        IReadOnlyCollection<CaptureType>? types = SelectedFilter.Kind == HistoryFilterKind.Types
            ? SelectedFilter.Types
            : null;

        bool? hasProject = SelectedFilter.Kind == HistoryFilterKind.Annotated ? true : null;

        return new CaptureFilter
        {
            Types = types,
            HasProject = hasProject,
            SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            IncludeDeleted = ShowDeleted,
            CreatedAfter = StartOfLocalDay(CreatedAfterDate),
            CreatedBefore = EndOfLocalDay(CreatedBeforeDate),
            SortOrder = CaptureSortOrder.NewestFirst,
            Limit = PageSize,
            Offset = offset,
        };
    }

    private static DateTimeOffset? StartOfLocalDay(DateTime? value)
    {
        if (value is not { } date)
        {
            return null;
        }

        DateTime start = date.Date;
        return new DateTimeOffset(start, TimeZoneInfo.Local.GetUtcOffset(start));
    }

    private static DateTimeOffset? EndOfLocalDay(DateTime? value)
    {
        if (value is not { } date)
        {
            return null;
        }

        DateTime end = date.Date.AddDays(1).AddTicks(-1);
        return new DateTimeOffset(end, TimeZoneInfo.Local.GetUtcOffset(end));
    }

    // Reload whenever a filter input changes.
    partial void OnSelectedFilterChanged(HistoryFilterOption value) => _ = RefreshAsync();

    partial void OnShowDeletedChanged(bool value) => _ = RefreshAsync();

    partial void OnCreatedAfterDateChanged(DateTime? value) => _ = RefreshAsync();

    partial void OnCreatedBeforeDateChanged(DateTime? value) => _ = RefreshAsync();

    [RelayCommand]
    private void ClearDateFilters()
    {
        CreatedAfterDate = null;
        CreatedBeforeDate = null;
    }

    /// <summary>Re-runs the query for the current search text (bound to the search box's action).</summary>
    [RelayCommand]
    private Task Search() => RefreshAsync();

    // ---- Per-item actions ----

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task OpenAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            await _pinService.ViewCaptureAsync(item.Record).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open capture {Id} as an image pin.", item.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task PinAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            await _pinService.PinCaptureAsync(item.Record).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pin capture {Id}.", item.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseApprovedMockup))]
    private async Task ViewApprovedMockupAsync()
    {
        if (SelectedItem is not { } item) return;
        try
        {
            await _pinService.ViewImageFileAsync(item.ApprovedMockupAbsolutePath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to view approved mockup for capture {Id}.", item.Id);
            _notifications.Notify("Mockup unavailable", "The approved variant could not be opened.", NotificationKind.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseApprovedMockup))]
    private Task CopyApprovedMockupAsync()
    {
        if (SelectedItem is not { } item) return Task.CompletedTask;
        try
        {
            _clipboard.SetImageFromFile(item.ApprovedMockupAbsolutePath);
            _notifications.Notify("Mockup copied", "The approved variant is on the clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy approved mockup for capture {Id}.", item.Id);
            _notifications.Notify("Copy failed", "The approved variant could not be copied.", NotificationKind.Error);
        }
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanUseApprovedMockup))]
    private async Task CopyApprovedMockupSpecAsync()
    {
        if (SelectedItem is not { } item) return;
        try
        {
            IReadOnlyList<ActionRecord> actions = await _actions.GetForCaptureAsync(item.Id).ConfigureAwait(true);
            ActionRecord? approval = actions.LastOrDefault(action =>
                action.ActionType == ActionType.MockupApproved && !string.IsNullOrWhiteSpace(action.MetadataJson));
            if (approval?.MetadataJson is null)
            {
                throw new InvalidOperationException("The approved mockup has no implementation spec.");
            }

            using JsonDocument metadata = JsonDocument.Parse(approval.MetadataJson);
            string instruction = metadata.RootElement.TryGetProperty("instruction", out JsonElement value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(instruction))
            {
                throw new InvalidOperationException("The approved mockup has no implementation spec.");
            }

            string spec =
                "Use the approved Octadock mockup as the visual reference.\n" +
                $"Binding UI delta: {instruction.Trim()}\n" +
                $"Mockup image: {item.ApprovedMockupAbsolutePath}\n" +
                "Preserve everything outside that delta.";
            _clipboard.SetText(spec);
            _notifications.Notify("Implementation spec copied", "Paste it beside the approved mockup.", NotificationKind.Success);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Failed to read approved mockup spec for capture {Id}.", item.Id);
            _notifications.Notify("Spec unavailable", ex.Message, NotificationKind.Warning);
        }
    }

    /// <summary>Copies the extracted OCR text stored on an OCR-source row.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopyExtractedTextAsync()
    {
        if (SelectedItem is not { IsOcr: true } item)
        {
            return;
        }

        try
        {
            IReadOnlyList<ActionRecord> actions = await _actions.GetForCaptureAsync(item.Id).ConfigureAwait(true);
            string? text = actions
                .Where(a => a.ActionType == ActionType.OcrExtracted)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => OcrHistoryRecorder.TryReadExtractedText(a.MetadataJson))
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));

            if (string.IsNullOrEmpty(text))
            {
                _notifications.Notify("No text stored", "This OCR entry has no recoverable text.", NotificationKind.Info);
                return;
            }

            _clipboard.SetText(text);
            _notifications.Notify("Text copied", "The extracted text is on the clipboard.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy OCR text for capture {Id}.", item.Id);
            _notifications.Notify("Copy failed", "The extracted text could not be copied.", NotificationKind.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            string path = _paths.ToAbsolute(item.Record.OriginalPath);
            if (File.Exists(path))
            {
                _clipboard.SetImageFromFile(path);
                _notifications.Notify("Copied", "The capture is on the clipboard.", NotificationKind.Success);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy capture {Id}.", item.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveAsync()
    {
        if (SelectedItem is not { } item)
        {
            return;
        }

        string source = _paths.ToAbsolute(item.Record.OriginalPath);
        if (!File.Exists(source))
        {
            return;
        }

        string ext = Path.GetExtension(source);
        var dialog = new SaveFileDialog
        {
            Title = "Save capture",
            FileName = Path.GetFileName(source),
            DefaultExt = ext,
            Filter = $"Capture (*{ext})|*{ext}|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await App.Services.GetRequiredService<ISafeFileWriter>()
                .CopyAsync(source, dialog.FileName).ConfigureAwait(true);
            _notifications.Notify("Saved", Path.GetFileName(dialog.FileName), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save capture {Id}.", item.Id);
            _notifications.Notify("Save failed", "Could not save the capture.", NotificationKind.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedItem is not { } item || item.IsDeleted)
        {
            return;
        }

        try
        {
            SelectedItem = null;
            await _captures.SoftDeleteAsync(item.Id, DateTimeOffset.UtcNow).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete capture {Id}.", item.Id);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RestoreAsync()
    {
        if (SelectedItem is not { } item || !item.IsDeleted)
        {
            return;
        }

        try
        {
            SelectedItem = null;
            await _captures.RestoreAsync(item.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore capture {Id}.", item.Id);
        }
    }

    /// <summary>
    /// Clears the visible history by soft-deleting every currently-listed, live
    /// capture. Files are purged later by the retention pass. The window confirms
    /// before invoking this.
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        IsBusy = true;
        try
        {
            CaptureItemViewModel[] toDelete = [.. Items.Where(i => !i.IsDeleted)];
            foreach (CaptureItemViewModel item in toDelete)
            {
                await _captures.SoftDeleteAsync(item.Id, DateTimeOffset.UtcNow).ConfigureAwait(true);
            }

            _notifications.Notify("History cleared", $"{toDelete.Length} captures moved to deleted.", NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear history.");
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(CaptureItemViewModel? value)
    {
        OpenCommand.NotifyCanExecuteChanged();
        PinCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        UseWithAiCommand.NotifyCanExecuteChanged();
        ViewApprovedMockupCommand.NotifyCanExecuteChanged();
        CopyApprovedMockupCommand.NotifyCanExecuteChanged();
        CopyApprovedMockupSpecCommand.NotifyCanExecuteChanged();
    }
}

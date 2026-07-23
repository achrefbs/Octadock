using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// View model backing the Capture Shelf window. Holds the stack of live cards (newest
/// first, capped at <see cref="ShelfSettings.MaxItems"/>), keeps a bounded stack of
/// recently-closed records for restore, and owns the auto-close
/// <see cref="DispatcherTimer"/> whose behavior follows <see cref="ShelfSettings.AutoClose"/>
/// (never / after-action / 30s / 1m / 5m). Hover suspends the timer.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed partial class ShelfViewModel : ObservableObject
{
    private const int RestoreStackLimit = 16;

    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly ILogger _logger;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly Stack<CaptureRecord> _recentlyClosed = new();

    private bool _hoverSuspended;
    private bool _showChrome;
    private double _cardWidth;
    private double _thumbnailHeight;
    private double _rowHeight;

    /// <summary>The live shelf cards, newest first (index 0 is the active card).</summary>
    public ObservableCollection<ShelfItemViewModel> Items { get; } = new();

    /// <summary>Whether the optional Shelf frame/header is visible.</summary>
    public bool ShowChrome
    {
        get => _showChrome;
        private set
        {
            if (SetProperty(ref _showChrome, value))
            {
                OnPropertyChanged(nameof(ThumbnailWidth));
            }
        }
    }

    /// <summary>Card body width in DIPs, derived from <see cref="ShelfSettings.Size"/>.</summary>
    public double CardWidth
    {
        get => _cardWidth;
        private set
        {
            if (SetProperty(ref _cardWidth, value))
            {
                OnPropertyChanged(nameof(ThumbnailWidth));
            }
        }
    }

    /// <summary>Thumbnail well height in DIPs, derived from <see cref="ShelfSettings.Size"/>.</summary>
    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        private set
        {
            if (SetProperty(ref _thumbnailHeight, value))
            {
                OnPropertyChanged(nameof(ThumbnailWidth));
            }
        }
    }

    /// <summary>Usable screenshot width, inset only when the optional frame is visible.</summary>
    public double ThumbnailWidth => Math.Max(0, CardWidth - (ShowChrome ? 12 : 0));

    /// <summary>Compact shelf-row height in DIPs.</summary>
    public double RowHeight
    {
        get => _rowHeight;
        private set => SetProperty(ref _rowHeight, value);
    }

    /// <summary>Raised when the shelf has no more items and the window should hide.</summary>
    public event EventHandler? Emptied;

    /// <summary>Creates the shelf view model.</summary>
    public ShelfViewModel(IServiceProvider services, ISettingsService settings, ILoggerFactory loggerFactory)
    {
        _services = services;
        _settings = settings;
        _logger = loggerFactory.CreateLogger("Shelf");

        _autoCloseTimer = new DispatcherTimer(DispatcherPriority.Normal);
        _autoCloseTimer.Tick += OnAutoCloseTick;

        ApplyShelfSettings(_settings.Current.Shelf);
        _settings.Changed += OnSettingsChanged;
    }

    private ShelfSettings Shelf => _settings.Current.Shelf;

    // Every item in a Shelf density uses the same compact canvas. The media is
    // aspect-fitted inside that canvas instead of changing the card's outer size.
    internal static ShelfLayoutMetrics GetLayoutMetrics(ShelfSize size) => size switch
    {
        ShelfSize.Small => new ShelfLayoutMetrics(CardWidth: 176, ThumbnailHeight: 96, RowHeight: 96),
        ShelfSize.Large => new ShelfLayoutMetrics(CardWidth: 224, ThumbnailHeight: 126, RowHeight: 126),
        _ => new ShelfLayoutMetrics(CardWidth: 196, ThumbnailHeight: 108, RowHeight: 108),
    };

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        => ApplyShelfSettings(e.Settings.Shelf);

    private void ApplyShelfSettings(ShelfSettings shelf)
    {
        ShowChrome = shelf.ShowChrome;
        ShelfLayoutMetrics metrics = GetLayoutMetrics(shelf.Size);
        CardWidth = metrics.CardWidth;
        ThumbnailHeight = metrics.ThumbnailHeight;
        RowHeight = metrics.RowHeight;
        foreach (ShelfItemViewModel item in Items)
        {
            item.ApplyDisplayMetrics(ThumbnailWidth, RowHeight);
        }
    }

    /// <summary>Adds a capture as the newest, active card and (re)arms auto-close.</summary>
    public void Add(CaptureRecord record)
    {
        var item = new ShelfItemViewModel(record, _services, DiscardAsync, OnActionCompleted, RemoveDeleted);
        item.ApplyDisplayMetrics(ThumbnailWidth, RowHeight);

        // A capture can return with an approved mockup path. Replace its old card
        // instead of showing two actions that mutate the same capture record.
        ShelfItemViewModel? previousVersion = Items.FirstOrDefault(existing => existing.Record.Id == record.Id);
        if (previousVersion is not null)
        {
            Items.Remove(previousVersion);
        }

        foreach (ShelfItemViewModel existing in Items)
        {
            existing.IsActive = false;
        }

        item.IsActive = true;
        Items.Insert(0, item);

        // Enforce the maximum by trimming the oldest cards (they stay in history).
        while (Items.Count > Math.Max(1, Shelf.MaxItems))
        {
            Items.RemoveAt(Items.Count - 1);
        }

        RestartAutoCloseTimer();
    }

    /// <summary>Brings an existing card forward without creating another capture.</summary>
    public bool TryActivateCapture(Guid captureId)
    {
        ShelfItemViewModel? item = Items.FirstOrDefault(existing => existing.Record.Id == captureId);
        if (item is null)
        {
            return false;
        }

        ActivateExisting(item);
        return true;
    }

    private void ActivateExisting(ShelfItemViewModel item)
    {
        foreach (ShelfItemViewModel existing in Items)
        {
            existing.IsActive = ReferenceEquals(existing, item);
        }

        int index = Items.IndexOf(item);
        if (index > 0)
        {
            Items.Move(index, 0);
        }

        RestartAutoCloseTimer();
    }

    /// <summary>Removes a specific card (used after Discard) and pushes it to the restore stack.</summary>
    public async Task DiscardAsync(ShelfItemViewModel item)
    {
        RememberClosed(item.Record);
        RemoveCard(item);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Removes a card whose capture was permanently deleted. Unlike Discard, nothing
    /// is pushed to the restore stack — and any earlier closed copy of the same record
    /// is scrubbed — so a deleted capture can never be restored onto the Shelf.
    /// </summary>
    public void RemoveDeleted(ShelfItemViewModel item)
    {
        ForgetClosed(item.Record.Id);
        RemoveCard(item);
    }

    private void ForgetClosed(Guid captureId)
    {
        if (_recentlyClosed.Count == 0)
        {
            return;
        }

        // ToArray() yields top..bottom; rebuild pushing bottom-first to keep the order.
        CaptureRecord[] kept = _recentlyClosed.Where(record => record.Id != captureId).ToArray();
        if (kept.Length == _recentlyClosed.Count)
        {
            return;
        }

        _recentlyClosed.Clear();
        for (int i = kept.Length - 1; i >= 0; i--)
        {
            _recentlyClosed.Push(kept[i]);
        }
    }

    /// <summary>Closes every card without deleting the underlying captures.</summary>
    public void CloseAll()
    {
        // Push oldest-first so the most recent card ends up on top of the restore stack.
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            RememberClosed(Items[i].Record);
        }

        Items.Clear();
        _autoCloseTimer.Stop();
        Emptied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-adds the most recently closed capture as a fresh card, honoring
    /// <see cref="ShelfSettings.RestoreEnabled"/>. Returns false when disabled/empty.
    /// </summary>
    public Task<bool> RestoreRecentlyClosedAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || !Shelf.RestoreEnabled || _recentlyClosed.Count == 0)
        {
            return Task.FromResult(false);
        }

        CaptureRecord closed = _recentlyClosed.Peek();
        Add(closed);
        _recentlyClosed.Pop();
        return Task.FromResult(true);
    }

    /// <summary>Suspends/resumes the auto-close timer while the pointer is over the shelf.</summary>
    public void SetHoverSuspended(bool suspended)
    {
        _hoverSuspended = suspended;
        if (suspended)
        {
            _autoCloseTimer.Stop();
        }
        else
        {
            RestartAutoCloseTimer();
        }
    }

    /// <summary>Refreshes any visible card backed by <paramref name="sourcePath"/>.</summary>
    public async Task RefreshSourceAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || Items.Count == 0)
        {
            return;
        }

        foreach (ShelfItemViewModel item in Items.ToArray())
        {
            await item.RefreshThumbnailForSourceAsync(sourcePath).ConfigureAwait(true);
        }
    }

    /// <summary>Called by the window when a card action completes, for After-Action auto-close.</summary>
    public void OnActionCompleted(ShelfItemViewModel completedItem)
    {
        if (Shelf.AutoClose == ShelfAutoCloseMode.AfterAction)
        {
            RememberClosed(completedItem.Record);
            RemoveCard(completedItem);
        }
    }

    private void RestartAutoCloseTimer()
    {
        _autoCloseTimer.Stop();

        if (_hoverSuspended || Items.Count == 0)
        {
            return;
        }

        int seconds = Shelf.AutoClose.AutoCloseSeconds();
        if (seconds <= 0)
        {
            // Never or AfterAction: no timed close.
            return;
        }

        _autoCloseTimer.Interval = TimeSpan.FromSeconds(seconds);
        _autoCloseTimer.Start();
    }

    private void OnAutoCloseTick(object? sender, EventArgs e)
    {
        _autoCloseTimer.Stop();
        if (!_hoverSuspended)
        {
            CloseAll();
        }
    }

    private void RemoveCard(ShelfItemViewModel item)
    {
        int index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        Items.RemoveAt(index);

        if (Items.Count == 0)
        {
            _autoCloseTimer.Stop();
            Emptied?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Promote the new newest card to active.
            Items[0].IsActive = true;
        }
    }

    private void RememberClosed(CaptureRecord record)
    {
        _recentlyClosed.Push(record);
        if (_recentlyClosed.Count > RestoreStackLimit)
        {
            // Drop the oldest (bottom) entry by rebuilding without it. Stack<T> has no
            // bottom-remove; ToArray() yields top..bottom, so skip the last element.
            CaptureRecord[] kept = _recentlyClosed.ToArray();
            _recentlyClosed.Clear();
            for (int i = kept.Length - 2; i >= 0; i--)
            {
                _recentlyClosed.Push(kept[i]);
            }
        }
    }

}

internal readonly record struct ShelfLayoutMetrics(double CardWidth, double ThumbnailHeight, double RowHeight);

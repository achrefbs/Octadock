using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Models;
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
    private double _cardWidth;
    private double _thumbnailHeight;

    /// <summary>The live shelf cards, newest first (index 0 is the active card).</summary>
    public ObservableCollection<ShelfItemViewModel> Items { get; } = new();

    /// <summary>Card body width in DIPs, derived from <see cref="ShelfSettings.Size"/>.</summary>
    public double CardWidth
    {
        get => _cardWidth;
        private set => SetProperty(ref _cardWidth, value);
    }

    /// <summary>Thumbnail well height in DIPs, derived from <see cref="ShelfSettings.Size"/>.</summary>
    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        private set => SetProperty(ref _thumbnailHeight, value);
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

        ApplyShelfSize(_settings.Current.Shelf.Size);
        _settings.Changed += OnSettingsChanged;
    }

    private ShelfSettings Shelf => _settings.Current.Shelf;

    internal static ShelfLayoutMetrics GetLayoutMetrics(ShelfSize size) => size switch
    {
        ShelfSize.Small => new ShelfLayoutMetrics(CardWidth: 196, ThumbnailHeight: 112),
        ShelfSize.Large => new ShelfLayoutMetrics(CardWidth: 300, ThumbnailHeight: 176),
        _ => new ShelfLayoutMetrics(CardWidth: 240, ThumbnailHeight: 140),
    };

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
        => ApplyShelfSize(e.Settings.Shelf.Size);

    private void ApplyShelfSize(ShelfSize size)
    {
        ShelfLayoutMetrics metrics = GetLayoutMetrics(size);
        CardWidth = metrics.CardWidth;
        ThumbnailHeight = metrics.ThumbnailHeight;
    }

    /// <summary>Adds a capture as the newest, active card and (re)arms auto-close.</summary>
    public void Add(CaptureRecord record)
    {
        var item = new ShelfItemViewModel(record, _services, DiscardAsync, OnActionCompleted);

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

    /// <summary>Removes a specific card (used after Discard) and pushes it to the restore stack.</summary>
    public async Task DiscardAsync(ShelfItemViewModel item)
    {
        RememberClosed(item.Record);
        RemoveCard(item);
        await Task.CompletedTask;
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
    public bool RestoreRecentlyClosed()
    {
        if (!Shelf.RestoreEnabled || _recentlyClosed.Count == 0)
        {
            return false;
        }

        CaptureRecord record = _recentlyClosed.Pop();
        Add(record);
        return true;
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

internal readonly record struct ShelfLayoutMetrics(double CardWidth, double ThumbnailHeight);

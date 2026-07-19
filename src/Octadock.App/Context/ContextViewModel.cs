using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.App.Ai;
using Octadock.App.Preview;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Context;

namespace Octadock.App.Context;

/// <summary>One Context item plus its explicit include/exclude state for the next export.</summary>
public sealed partial class ContextItemExportViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public ContextItemExportViewModel(ContextItem item, bool isIncluded, Action selectionChanged)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _isIncluded = isIncluded;
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
    }

    public ContextItem Item { get; }

    [ObservableProperty] private bool _isIncluded;

    public string DisplayName => Item.DisplayName;

    public string OwnershipLabel => Item.Ownership == ContextOwnership.Snapshot ? "Local snapshot" : "Verified reference";

    public string MetadataLabel
    {
        get
        {
            string derivativeLabel = Item.Derivatives.Count switch
            {
                0 => "no derived files",
                1 => "1 derived file",
                _ => $"{Item.Derivatives.Count:N0} derived files",
            };
            return $"{FormatBytes(Item.SizeBytes)}  ·  {derivativeLabel}";
        }
    }

    partial void OnIsIncludedChanged(bool value) => _selectionChanged();

    private static string FormatBytes(long value)
    {
        double size = Math.Max(0, value);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}

/// <summary>
/// View model for the Context window (WS10): a persistent packaging surface, kept
/// separate from the Capture Shelf and NOT AI. Wraps <see cref="ContextService"/> —
/// creating/adding is gated post-trial; viewing and exporting existing packages is not.
/// Dialog-driven actions (file/save pickers, the export toggles) are orchestrated by the
/// window code-behind, which calls the public methods here.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ContextViewModel : ObservableObject
{
    private readonly ContextService _context;
    private readonly ActiveContextState _activeContext;
    private readonly IStoragePaths _paths;
    private readonly FilePreviewService _preview;
    private readonly IPinService _pins;
    private readonly IWindowPresenter _presenter;
    private readonly ILogger<ContextViewModel> _logger;
    private readonly HashSet<Guid> _excludedItemIds = [];

    [ObservableProperty] private ContextPackage? _selectedPackage;
    [ObservableProperty] private ContextItemExportViewModel? _selectedItem;
    [ObservableProperty] private string _packageNotes = string.Empty;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasPackages;

    public ContextViewModel(
        ContextService context,
        ActiveContextState activeContext,
        IStoragePaths paths,
        FilePreviewService preview,
        IPinService pins,
        IWindowPresenter presenter,
        ILogger<ContextViewModel> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _activeContext = activeContext ?? throw new ArgumentNullException(nameof(activeContext));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _pins = pins ?? throw new ArgumentNullException(nameof(pins));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Packages, newest first.</summary>
    public ObservableCollection<ContextPackage> Packages { get; } = [];

    /// <summary>Items in the selected package.</summary>
    public ObservableCollection<ContextItemExportViewModel> Items { get; } = [];

    /// <summary>Shared destination state used by Preview, Shelf, Pin, and this visible surface.</summary>
    public ActiveContextState ActiveState => _activeContext;

    /// <summary>Reloads all packages and their items.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            Guid? keepSelected = SelectedPackage?.Id ?? _activeContext.ActivePackageId;
            IReadOnlyList<ContextPackage> packages = await _context.GetPackagesAsync().ConfigureAwait(true);

            Packages.Clear();
            foreach (ContextPackage package in packages)
            {
                Packages.Add(package);
            }

            HasPackages = Packages.Count > 0;
            ContextPackage? selection = Packages.FirstOrDefault(package => package.Id == keepSelected);
            if (keepSelected.HasValue && selection is null)
            {
                _activeContext.Clear();
            }

            // A sole package is unambiguous. With several packages and no active
            // destination, leave the choice visible and explicit instead of using packages[0].
            selection ??= !keepSelected.HasValue && Packages.Count == 1
                ? Packages.Single()
                : null;
            SelectedPackage = selection;
            OnPropertyChanged(nameof(PackagePositionLabel));
            OnPropertyChanged(nameof(SelectedItemCountLabel));
            OnPropertyChanged(nameof(CanNavigatePackages));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Context packages.");
            StatusMessage = "Could not load Context packages.";
        }
    }

    /// <summary>"2 / 5"-style position of the selected package within the stack (empty when none).</summary>
    public string PackagePositionLabel
    {
        get
        {
            if (Packages.Count == 0 || SelectedPackage is null)
            {
                return string.Empty;
            }

            int index = Packages.IndexOf(SelectedPackage);
            return index < 0 ? string.Empty : $"{index + 1} / {Packages.Count}";
        }
    }

    /// <summary>Item count of the selected package, as a short label.</summary>
    public string SelectedItemCountLabel
    {
        get
        {
            int count = Items.Count;
            return count == 1 ? "1 item" : $"{count} items";
        }
    }

    /// <summary>True when there is more than one package to navigate between.</summary>
    public bool CanNavigatePackages => Packages.Count > 1;

    public int IncludedItemCount => Items.Count(item => item.IsIncluded);

    public bool HasIncludedItems => IncludedItemCount > 0;

    [RelayCommand(CanExecute = nameof(HasIncludedItems))]
    private void UseContextWithAi()
    {
        if (SelectedPackage is not { } package || !HasIncludedItems)
        {
            StatusMessage = "Include at least one Context item first.";
            return;
        }

        _presenter.ShowAiActions(AgentReviewLaunch.FromContext(
            package.Id,
            Items.Where(item => item.IsIncluded).Select(item => item.Item.Id),
            package.Name));
        StatusMessage = "Opened the handoff review. Item availability and hashes are validated there before handoff.";
    }

    public string ExportPreviewLabel
    {
        get
        {
            int included = IncludedItemCount;
            int derived = Items.Where(item => item.IsIncluded).Sum(item => item.Item.Derivatives.Count);
            long bytes = Items.Where(item => item.IsIncluded).Sum(item => Math.Max(0, item.Item.SizeBytes));
            string items = included == 1 ? "1 item" : $"{included:N0} items";
            string derivatives = derived == 1 ? "1 derived file" : $"{derived:N0} derived files";
            return $"Export preview: {items} + {derivatives}  ·  {FormatBytes(bytes)} primary content";
        }
    }

    partial void OnSelectedPackageChanged(ContextPackage? value)
    {
        Items.Clear();
        PackageNotes = value?.Notes ?? string.Empty;
        if (value is not null)
        {
            _activeContext.SetActive(value);
            foreach (ContextItem item in value.Items)
            {
                Items.Add(new ContextItemExportViewModel(
                    item,
                    isIncluded: !_excludedItemIds.Contains(item.Id),
                    OnExportSelectionChanged));
            }
        }

        OnPropertyChanged(nameof(PackagePositionLabel));
        OnPropertyChanged(nameof(SelectedItemCountLabel));
        OnExportSelectionChanged();
    }

    /// <summary>Selects the next package in the stack (wraps around).</summary>
    public void SelectNextPackage() => StepPackage(1);

    /// <summary>Selects the previous package in the stack (wraps around).</summary>
    public void SelectPreviousPackage() => StepPackage(-1);

    private void StepPackage(int delta)
    {
        if (Packages.Count == 0)
        {
            return;
        }

        if (SelectedPackage is null)
        {
            SelectedPackage = delta > 0 ? Packages[0] : Packages[^1];
            return;
        }

        int current = Packages.IndexOf(SelectedPackage);
        if (current < 0)
        {
            current = 0;
        }

        int next = ((current + delta) % Packages.Count + Packages.Count) % Packages.Count;
        SelectedPackage = Packages[next];
    }

    /// <summary>Creates and selects a one-click, automatically named package.</summary>
    public async Task CreatePackageAsync()
    {
        string name = NextDefaultPackageName();

        ContextPackage? created = await _context.CreatePackageAsync(name).ConfigureAwait(true);
        if (created is null)
        {
            StatusMessage = "Creating a Context package needs an active trial or license.";
            return;
        }

        _activeContext.SetActive(created);
        await RefreshAsync().ConfigureAwait(true);
        SelectedPackage = Packages.FirstOrDefault(p => p.Id == created.Id);
        StatusMessage = $"Created '{created.Name}'.";
    }

    /// <summary>Renames the selected package after inline editing in the package rail.</summary>
    public async Task RenameSelectedPackageAsync(string name)
    {
        if (SelectedPackage is not { } package)
        {
            return;
        }

        string trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            StatusMessage = "A Context name cannot be empty.";
            return;
        }

        if (string.Equals(trimmed, package.Name, StringComparison.Ordinal))
        {
            return;
        }

        await _context.RenamePackageAsync(package.Id, trimmed).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = $"Renamed to '{trimmed}'.";
    }

    /// <summary>Persists the selected package's local notes.</summary>
    public async Task SaveSelectedPackageNotesAsync()
    {
        if (SelectedPackage is not { } package)
        {
            return;
        }

        if (PackageNotes.Length > ContextService.MaxPackageNotesLength)
        {
            StatusMessage = $"Context notes are limited to {ContextService.MaxPackageNotesLength:N0} characters.";
            return;
        }

        await _context.UpdatePackageNotesAsync(package.Id, PackageNotes).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = "Saved Context notes.";
    }

    private string NextDefaultPackageName()
    {
        var existing = new HashSet<string>(
            Packages.Select(package => package.Name),
            StringComparer.OrdinalIgnoreCase);
        for (int index = 1; ; index++)
        {
            string candidate = $"Context {index}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Adds files to the selected package.</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (SelectedPackage is not { } package)
        {
            return;
        }

        int added = 0;
        foreach (string path in paths)
        {
            if (await _context.AddFileAsync(package.Id, path).ConfigureAwait(true))
            {
                added++;
            }
        }

        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = added == 1 ? "Added 1 item." : $"Added {added} items.";
    }

    /// <summary>Removes the selected item.</summary>
    public async Task RemoveSelectedItemAsync()
    {
        if (SelectedItem is not { } selected)
        {
            return;
        }

        _excludedItemIds.Remove(selected.Item.Id);
        await _context.RemoveItemAsync(selected.Item.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = "Removed item.";
    }

    /// <summary>Moves one item by one position and persists the package's exact item order.</summary>
    public async Task MoveItemAsync(ContextItemExportViewModel item, int delta)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (SelectedPackage is not { } package || delta == 0)
        {
            return;
        }

        int current = Items.IndexOf(item);
        int destination = current + Math.Sign(delta);
        if (current < 0 || destination < 0 || destination >= Items.Count)
        {
            return;
        }

        List<Guid> orderedIds = Items.Select(entry => entry.Item.Id).ToList();
        (orderedIds[current], orderedIds[destination]) = (orderedIds[destination], orderedIds[current]);
        await _context.ReorderItemsAsync(package.Id, orderedIds).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        SelectedItem = Items.FirstOrDefault(entry => entry.Item.Id == item.Item.Id);
        StatusMessage = $"Moved '{item.DisplayName}' {((delta < 0) ? "up" : "down")}.";
    }

    /// <summary>Opens a Context item using the same preview/image-viewer route as normal file opens.</summary>
    public async Task OpenItemAsync(ContextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string? path = ResolveItemPath(item);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "That item is no longer available on disk.";
            return;
        }

        try
        {
            if (ImageFileSupport.IsSupportedRasterPath(path))
            {
                await _pins.ViewImageFileAsync(path).ConfigureAwait(true);
                return;
            }

            bool previewed = await _preview.PreviewExistingAsync(path).ConfigureAwait(true);
            if (!previewed)
            {
                StatusMessage = "That item could not be previewed.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open Context item {Id}.", item.Id);
            StatusMessage = "Could not open that item.";
        }
    }

    private string? ResolveItemPath(ContextItem item)
        => item.Ownership == ContextOwnership.Reference
            ? item.ReferenceSourcePath
            : string.IsNullOrWhiteSpace(item.StorageRelativePath)
                ? null
                : _paths.ToAbsolute(item.StorageRelativePath);

    /// <summary>Deletes the selected package.</summary>
    public async Task DeleteSelectedPackageAsync()
    {
        if (SelectedPackage is not { } package)
        {
            return;
        }

        int removedIndex = Packages.IndexOf(package);
        foreach (ContextItem item in package.Items)
        {
            _excludedItemIds.Remove(item.Id);
        }

        await _context.DeletePackageAsync(package.Id).ConfigureAwait(true);
        _activeContext.Clear();
        await RefreshAsync().ConfigureAwait(true);
        if (SelectedPackage is null && Packages.Count > 0)
        {
            SelectedPackage = Packages[Math.Clamp(removedIndex, 0, Packages.Count - 1)];
        }

        StatusMessage = "Deleted package.";
    }

    /// <summary>Exports the selected package (all items + derivatives) to a zip.</summary>
    public async Task ExportSelectedAsync(string destinationZipPath)
    {
        if (SelectedPackage is not { } package)
        {
            return;
        }

        try
        {
            if (!HasIncludedItems)
            {
                StatusMessage = "Choose at least one item to export.";
                return;
            }

            bool ok = await _context.ExportAsync(package.Id, BuildExportSelection(), destinationZipPath).ConfigureAwait(true);
            StatusMessage = ok ? "Exported." : "Nothing to export.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Context export failed.");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>Exports the selected package to a plain (browsable) folder — the default the UI offers.</summary>
    public async Task ExportSelectedToFolderAsync(string destinationDirectory)
    {
        if (SelectedPackage is not { } package)
        {
            return;
        }

        try
        {
            if (!HasIncludedItems)
            {
                StatusMessage = "Choose at least one item to export.";
                return;
            }

            bool ok = await _context.ExportToFolderAsync(package.Id, BuildExportSelection(), destinationDirectory).ConfigureAwait(true);
            StatusMessage = ok ? $"Exported '{package.Name}' to a folder." : "Nothing to export.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Context folder export failed.");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private ContextExportSelection BuildExportSelection()
        => ContextExportSelection.FromReviewedItems(
            Items.Select(item => item.Item.Id),
            Items.Where(item => item.IsIncluded).Select(item => item.Item.Id));

    private void OnExportSelectionChanged()
    {
        foreach (ContextItemExportViewModel item in Items)
        {
            if (item.IsIncluded)
            {
                _excludedItemIds.Remove(item.Item.Id);
            }
            else
            {
                _excludedItemIds.Add(item.Item.Id);
            }
        }

        OnPropertyChanged(nameof(IncludedItemCount));
        OnPropertyChanged(nameof(HasIncludedItems));
        OnPropertyChanged(nameof(ExportPreviewLabel));
        UseContextWithAiCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBytes(long value)
    {
        double size = Math.Max(0, value);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}

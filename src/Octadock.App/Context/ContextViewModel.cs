using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Octadock.App.Preview;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Context;

namespace Octadock.App.Context;

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
    private readonly IStoragePaths _paths;
    private readonly FilePreviewService _preview;
    private readonly ILogger<ContextViewModel> _logger;

    [ObservableProperty] private ContextPackage? _selectedPackage;
    [ObservableProperty] private ContextItem? _selectedItem;
    [ObservableProperty] private string _newPackageName = string.Empty;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasPackages;

    public ContextViewModel(
        ContextService context,
        IStoragePaths paths,
        FilePreviewService preview,
        ILogger<ContextViewModel> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Packages, newest first.</summary>
    public ObservableCollection<ContextPackage> Packages { get; } = [];

    /// <summary>Items in the selected package.</summary>
    public ObservableCollection<ContextItem> Items { get; } = [];

    /// <summary>Reloads all packages and their items.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            Guid? keepSelected = SelectedPackage?.Id;
            IReadOnlyList<ContextPackage> packages = await _context.GetPackagesAsync().ConfigureAwait(true);

            Packages.Clear();
            foreach (ContextPackage package in packages)
            {
                Packages.Add(package);
            }

            HasPackages = Packages.Count > 0;
            SelectedPackage = Packages.FirstOrDefault(p => p.Id == keepSelected) ?? Packages.FirstOrDefault();
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

    partial void OnSelectedPackageChanged(ContextPackage? value)
    {
        Items.Clear();
        if (value is not null)
        {
            foreach (ContextItem item in value.Items)
            {
                Items.Add(item);
            }
        }

        OnPropertyChanged(nameof(PackagePositionLabel));
        OnPropertyChanged(nameof(SelectedItemCountLabel));
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

        int current = SelectedPackage is null ? 0 : Packages.IndexOf(SelectedPackage);
        if (current < 0)
        {
            current = 0;
        }

        int next = ((current + delta) % Packages.Count + Packages.Count) % Packages.Count;
        SelectedPackage = Packages[next];
    }

    /// <summary>Creates a package from <see cref="NewPackageName"/> (or a default) and selects it.</summary>
    public async Task CreatePackageAsync()
    {
        string name = string.IsNullOrWhiteSpace(NewPackageName)
            ? $"Context {DateTimeOffset.Now:yyyy-MM-dd HH:mm}"
            : NewPackageName.Trim();

        ContextPackage? created = await _context.CreatePackageAsync(name).ConfigureAwait(true);
        if (created is null)
        {
            StatusMessage = "Creating a Context package needs an active trial or license.";
            return;
        }

        NewPackageName = string.Empty;
        await RefreshAsync().ConfigureAwait(true);
        SelectedPackage = Packages.FirstOrDefault(p => p.Id == created.Id);
        StatusMessage = $"Created '{created.Name}'.";
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
        if (SelectedItem is not { } item)
        {
            return;
        }

        await _context.RemoveItemAsync(item.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = "Removed item.";
    }

    /// <summary>Opens a Context item using the same preview/image-viewer route as normal file opens.</summary>
    public async Task OpenItemAsync(ContextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        string? path = ResolveItemPath(item);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "That item is no longer available on disk.";
            return;
        }

        try
        {
            await _preview.PreviewAsync(path).ConfigureAwait(true);
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

        await _context.DeletePackageAsync(package.Id).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
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
            bool ok = await _context.ExportAsync(package.Id, new ContextExportSelection(), destinationZipPath).ConfigureAwait(true);
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
            bool ok = await _context.ExportToFolderAsync(package.Id, new ContextExportSelection(), destinationDirectory).ConfigureAwait(true);
            StatusMessage = ok ? $"Exported '{package.Name}' to a folder." : "Nothing to export.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Context folder export failed.");
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }
}

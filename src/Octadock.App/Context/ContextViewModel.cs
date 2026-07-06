using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
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
    private readonly ILogger<ContextViewModel> _logger;

    [ObservableProperty] private ContextPackage? _selectedPackage;
    [ObservableProperty] private ContextItem? _selectedItem;
    [ObservableProperty] private string _newPackageName = string.Empty;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasPackages;

    public ContextViewModel(ContextService context, ILogger<ContextViewModel> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Context packages.");
            StatusMessage = "Could not load Context packages.";
        }
    }

    partial void OnSelectedPackageChanged(ContextPackage? value)
    {
        Items.Clear();
        if (value is null)
        {
            return;
        }

        foreach (ContextItem item in value.Items)
        {
            Items.Add(item);
        }
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
}

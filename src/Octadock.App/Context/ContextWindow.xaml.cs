using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Win32;

namespace Octadock.App.Context;

/// <summary>
/// The Context window (WS10): a persistent packaging surface, visually distinct from the
/// Capture Shelf and NOT AI. Constructed through DI; dialogs (add file, export) are handled
/// here and delegated to <see cref="ContextViewModel"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class ContextWindow : Window
{
    private readonly ContextViewModel _viewModel;

    /// <summary>Creates the Context window with an injected view model.</summary>
    public ContextWindow(ContextViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnNewPackage(object sender, RoutedEventArgs e)
        => await _viewModel.CreatePackageAsync().ConfigureAwait(true);

    private async void OnAddFile(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is null)
        {
            _viewModel.StatusMessage = "Create or select a package first.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Add files to this Context",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.AddFilesAsync(dialog.FileNames).ConfigureAwait(true);
        }
    }

    private async void OnRemoveItem(object sender, RoutedEventArgs e)
        => await _viewModel.RemoveSelectedItemAsync().ConfigureAwait(true);

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is not { } package)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Context package",
            Filter = "Zip archive (*.zip)|*.zip",
            FileName = MakeSafeFileName(package.Name) + ".zip",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ExportSelectedAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private async void OnDeletePackage(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            "Delete this Context package? Its items and any snapshotted copies are removed. Your original captures and files are untouched.",
            "Delete package",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
        {
            await _viewModel.DeleteSelectedPackageAsync().ConfigureAwait(true);
        }
    }

    private static string MakeSafeFileName(string name)
    {
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        cleaned = cleaned.Trim();
        return string.IsNullOrEmpty(cleaned) ? "context" : cleaned;
    }
}

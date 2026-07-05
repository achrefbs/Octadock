using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using Octadock.Core.Hotkeys;

namespace Octadock.App.Settings;

/// <summary>
/// The Octadock settings window. A tabbed editor bound to <see cref="SettingsViewModel"/>.
/// Constructed through DI so the view model receives its Core services.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>Creates the settings window with an injected view model.</summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.Saved += (_, _) => Close();
    }

    /// <summary>
    /// Selects the tab whose <c>Tag</c> matches the given key (e.g.
    /// "shortcuts"). Tabs are grouped into sections (Capture / Voice /
    /// Library / System), so a legacy key selects both the section and the
    /// sub-page inside it.
    /// </summary>
    public void SelectTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        foreach (object? outer in Tabs.Items)
        {
            if (outer is not TabItem outerItem)
            {
                continue;
            }

            if (string.Equals(outerItem.Tag as string, tab, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = outerItem;
                return;
            }

            if (outerItem.Content is not TabControl section)
            {
                continue;
            }

            foreach (object? inner in section.Items)
            {
                if (inner is TabItem innerItem &&
                    string.Equals(innerItem.Tag as string, tab, StringComparison.OrdinalIgnoreCase))
                {
                    Tabs.SelectedItem = outerItem;
                    section.SelectedItem = innerItem;
                    return;
                }
            }
        }
    }

    private void OnGestureCaptured(object sender, HotkeyGesture gesture)
    {
        if (sender is FrameworkElement { Tag: HotkeyGestureViewModel row })
        {
            _viewModel.ApplyGesture(row, gesture);
        }
    }

    private void OnBrowseSaveDirectory(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder captures are saved to",
        };

        if (!string.IsNullOrWhiteSpace(_viewModel.SaveDirectory) && System.IO.Directory.Exists(_viewModel.SaveDirectory))
        {
            dialog.InitialDirectory = _viewModel.SaveDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SaveDirectory = dialog.FolderName;
        }
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        // B-9: same confirmation the History window shows — clearing every
        // capture should never be a single silent click.
        MessageBoxResult result = MessageBox.Show(
            this,
            "Move all captures to deleted? Files are purged later by the retention pass. You can restore them until then.",
            "Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK && _viewModel.ClearHistoryCommand.CanExecute(null))
        {
            await _viewModel.ClearHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

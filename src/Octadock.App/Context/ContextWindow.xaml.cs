using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Octadock.App.Windows;
using Forms = System.Windows.Forms;

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
    private bool _isRenamingPackage;
    private bool _isCommittingRename;

    /// <summary>Creates the Context window with an injected view model.</summary>
    public ContextWindow(ContextViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Octadock.App.Windows.ScreenFit.Attach(this);
        ShowInTaskbar = Environment.GetEnvironmentVariable(ToolWindowBase.UiAuditEnvVar) == "1";
        DataContext = _viewModel;
        Loaded += (_, _) => EntranceMotion.Play(StackRoot);
        Loaded += async (_, _) =>
        {
            await _viewModel.RefreshAsync().ConfigureAwait(true);
            UpdateLayout();
            PlaceOnCursorScreen();
            BringToFront();
        };
    }

    /// <summary>Moves the floating stack to the top-right of the screen the user is working on.</summary>
    public void PlaceOnCursorScreen()
    {
        ScreenFit.Place(this, center: true, atCursor: true);
    }

    private void BringToFront()
    {
        Topmost = false;
        Topmost = true;
        Activate();
    }

    private async void OnNewPackage(object sender, RoutedEventArgs e)
        => await _viewModel.CreatePackageAsync().ConfigureAwait(true);

    private void OnBeginRenamePackage(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is not { } package || _isRenamingPackage)
        {
            return;
        }

        _isRenamingPackage = true;
        PackageNameBox.Text = package.Name;
        PackageNameText.Visibility = Visibility.Collapsed;
        PackageNameBox.Visibility = Visibility.Visible;
        RenamePackageButton.Visibility = Visibility.Collapsed;
        Dispatcher.BeginInvoke(() =>
        {
            PackageNameBox.Focus();
            PackageNameBox.SelectAll();
        });
    }

    private async void OnPackageNameLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isRenamingPackage && !_isCommittingRename)
        {
            await CommitPackageRenameAsync().ConfigureAwait(true);
        }
    }

    private async void OnPackageNamePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndPackageRename();
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await CommitPackageRenameAsync().ConfigureAwait(true);
        }
    }

    private async Task CommitPackageRenameAsync()
    {
        if (!_isRenamingPackage || _isCommittingRename)
        {
            return;
        }

        _isCommittingRename = true;
        try
        {
            await _viewModel.RenameSelectedPackageAsync(PackageNameBox.Text).ConfigureAwait(true);
        }
        finally
        {
            _isCommittingRename = false;
            EndPackageRename();
        }
    }

    private void EndPackageRename()
    {
        _isRenamingPackage = false;
        PackageNameBox.Visibility = Visibility.Collapsed;
        PackageNameText.Visibility = Visibility.Visible;
        RenamePackageButton.Visibility = Visibility.Visible;
    }

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

    private void OnPreviousPackage(object sender, RoutedEventArgs e) => _viewModel.SelectPreviousPackage();

    private void OnNextPackage(object sender, RoutedEventArgs e) => _viewModel.SelectNextPackage();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDragHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnDragHeaderPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space or Key.Apps) &&
            !(e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            return;
        }

        SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(16, 16)));
        e.Handled = true;
    }

    private async void OnSavePackageNotes(object sender, RoutedEventArgs e)
        => await _viewModel.SaveSelectedPackageNotesAsync().ConfigureAwait(true);

    private void OnDragFilesOver(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel.SelectedPackage is not null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDropFiles(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_viewModel.SelectedPackage is null ||
            !e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        string[] files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length > 0)
        {
            await _viewModel.AddFilesAsync(files).ConfigureAwait(true);
        }
    }

    private static bool IsInsideButton(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    /// <summary>Per-row remove: selects the clicked item, then removes it from Context.</summary>
    private async void OnRemoveItemRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContextItemExportViewModel item })
        {
            e.Handled = true;
            _viewModel.SelectedItem = item;
            await _viewModel.RemoveSelectedItemAsync().ConfigureAwait(true);
        }
    }

    private async void OnMoveItemUp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContextItemExportViewModel item })
        {
            e.Handled = true;
            await _viewModel.MoveItemAsync(item, -1).ConfigureAwait(true);
        }
    }

    private async void OnMoveItemDown(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContextItemExportViewModel item })
        {
            e.Handled = true;
            await _viewModel.MoveItemAsync(item, 1).ConfigureAwait(true);
        }
    }

    /// <summary>Opens the clicked Context item using the normal Octadock file/image viewer route.</summary>
    private async void OnOpenItemRow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: ContextItemExportViewModel item })
        {
            _viewModel.SelectedItem = item;
            await _viewModel.OpenItemAsync(item.Item).ConfigureAwait(true);
            e.Handled = true;
        }
    }

    private async void OnOpenItemRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            sender is not FrameworkElement { DataContext: ContextItemExportViewModel item })
        {
            return;
        }

        _viewModel.SelectedItem = item;
        await _viewModel.OpenItemAsync(item.Item).ConfigureAwait(true);
        e.Handled = true;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is null)
        {
            _viewModel.StatusMessage = "Create or select a Context first.";
            return;
        }

        // Export to a plain, browsable folder (not a zip) — the current default.
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where to export this Context",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ExportSelectedToFolderAsync(dialog.FolderName).ConfigureAwait(true);
        }
    }

    private async void OnExportZip(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is not { } package)
        {
            _viewModel.StatusMessage = "Create or select a Context first.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Context as a zip",
            Filter = "Zip archive (*.zip)|*.zip",
            AddExtension = true,
            DefaultExt = ".zip",
            FileName = SanitizeSuggestedName(package.Name) + ".zip",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ExportSelectedAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private static string SanitizeSuggestedName(string value)
    {
        string cleaned = string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Octadock Context" : cleaned;
    }

    private async void OnDeletePackage(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is null)
        {
            return;
        }

        bool confirmed = ConfirmationDialog.Confirm(
            this,
            "Delete Context",
            "Delete this Context? Its items and any local snapshots are removed. Your original captures and files stay untouched.",
            confirmText:
                "Delete");

        if (confirmed)
        {
            await _viewModel.DeleteSelectedPackageAsync().ConfigureAwait(true);
        }
    }
}

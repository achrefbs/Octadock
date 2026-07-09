using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
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

    /// <summary>Creates the Context window with an injected view model.</summary>
    public ContextWindow(ContextViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
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
        Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        Rect workArea = ToDeviceIndependentRect(screen.WorkingArea);
        const double margin = 18;

        double windowWidth = ResolveExtent(ActualWidth, Width, MinWidth);
        double windowHeight = ResolveExtent(ActualHeight, Height, MinHeight);

        Left = Clamp(workArea.Right - windowWidth - margin, workArea.Left + margin, workArea.Right - windowWidth - margin);
        Top = Clamp(workArea.Top + margin, workArea.Top + margin, workArea.Bottom - windowHeight - margin);
    }

    private void BringToFront()
    {
        Topmost = false;
        Topmost = true;
        Activate();
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

    private Rect ToDeviceIndependentRect(System.Drawing.Rectangle rectangle)
    {
        Matrix transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point topLeft = transform.Transform(new Point(rectangle.Left, rectangle.Top));
        Point bottomRight = transform.Transform(new Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private static double ResolveExtent(double actual, double configured, double fallback)
    {
        if (!double.IsNaN(actual) && actual > 1)
        {
            return actual;
        }

        if (!double.IsNaN(configured) && configured > 1)
        {
            return configured;
        }

        return fallback > 1 ? fallback : 320;
    }

    /// <summary>Per-row remove: selects the clicked item, then removes it from the stack.</summary>
    private async void OnRemoveItemRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Octadock.Core.Context.ContextItem item })
        {
            e.Handled = true;
            _viewModel.SelectedItem = item;
            await _viewModel.RemoveSelectedItemAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Opens the clicked Context item using the normal Octadock file/image viewer route.</summary>
    private async void OnOpenItemRow(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: Octadock.Core.Context.ContextItem item })
        {
            _viewModel.SelectedItem = item;
            await _viewModel.OpenItemAsync(item).ConfigureAwait(true);
            e.Handled = true;
        }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedPackage is null)
        {
            _viewModel.StatusMessage = "Create or select a stack first.";
            return;
        }

        // Export to a plain, browsable folder (not a zip) — the current default.
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to export this stack into",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.ExportSelectedToFolderAsync(dialog.FolderName).ConfigureAwait(true);
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
}

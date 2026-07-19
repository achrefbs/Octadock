using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Octadock.App.Clipboard;

/// <summary>The clipboard history window. Rows restore to the clipboard on double-click.</summary>
[SupportedOSPlatform("windows")]
public partial class ClipboardHistoryWindow : Window
{
    private readonly ClipboardHistoryViewModel _viewModel;

    /// <summary>Creates the window over its injected view model.</summary>
    public ClipboardHistoryWindow(ClipboardHistoryViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedItem is not null)
        {
            _viewModel.Copy(_viewModel.SelectedItem);
        }
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
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

    private void OnHeaderPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space or Key.Apps) &&
            !(e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            return;
        }

        SystemCommands.ShowSystemMenu(this, PointToScreen(new Point(16, 16)));
        e.Handled = true;
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

    private void OnClearSearch(object sender, RoutedEventArgs e) => _viewModel.SearchText = string.Empty;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}

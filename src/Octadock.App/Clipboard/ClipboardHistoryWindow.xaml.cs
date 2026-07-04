using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;

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
}

using System.Runtime.Versioning;
using System.Windows;

namespace Octadock.App.History;

/// <summary>
/// The local capture history window: a searchable, filterable thumbnail grid bound
/// to <see cref="HistoryViewModel"/>. Constructed through DI so the view model
/// receives its Core services. Loads the first page when it opens.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;

    /// <summary>Creates the history window with an injected view model.</summary>
    public HistoryWindow(HistoryViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            this,
            "Move all currently listed captures to deleted? Files are purged later by the retention pass. You can restore them until then.",
            "Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
        {
            await _viewModel.ClearHistoryAsync().ConfigureAwait(true);
        }
    }
}

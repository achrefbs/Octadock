using System.Runtime.Versioning;
using System.Windows;
using Octadock.App.Windows;

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
        Octadock.App.Windows.ScreenFit.Attach(this);
        DataContext = _viewModel;
        SizeChanged += (_, _) => AdaptLayout();
        _viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelChanged;

        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
        Loaded += (_, _) => EntranceMotion.Play(RootGrid);
    }

    private bool? _compact;
    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.HasSelection)) AdaptLayout();
    }

    private void AdaptLayout()
    {
        bool compact = ActualWidth < 800;
        if (_compact != compact) FiltersPanel.IsExpanded = !compact;
        _compact = compact;
        System.Windows.Controls.Grid.SetRow(FiltersPanel, compact ? 0 : 1);
        System.Windows.Controls.Grid.SetColumn(FiltersPanel, compact ? 2 : 0);
        FiltersPanel.MaxHeight = compact ? Math.Max(90, ActualHeight * .30) : double.PositiveInfinity;
        System.Windows.Controls.Grid.SetRow(DetailsPanel, compact ? 2 : 1);
        System.Windows.Controls.Grid.SetColumn(DetailsPanel, compact ? 2 : 4);
        DetailPreview.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DetailsPanel.MaxHeight = compact ? Math.Max(140, ActualHeight * .30) : double.PositiveInfinity;
        HistoryBody.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 176);
        HistoryBody.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 8);
        HistoryBody.ColumnDefinitions[3].Width = new GridLength(!compact && _viewModel.HasSelection ? 8 : 0);
        HistoryBody.ColumnDefinitions[4].Width = new GridLength(!compact && _viewModel.HasSelection ? 280 : 0);
        System.Windows.Controls.Grid.SetRow(SearchContainer, compact ? 1 : 0);
        System.Windows.Controls.Grid.SetColumn(SearchContainer, compact ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(SearchContainer, compact ? 3 : 1);
        SearchContainer.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(12, 0, 12, 0);
        SearchContainer.MaxWidth = compact ? double.PositiveInfinity : 350;
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        // The shared themed dialog keeps the same contract: owned by this window,
        // the safe answer is the default, and only an explicit confirm proceeds.
        bool confirmed = ConfirmationDialog.Confirm(
            this,
            "Clear history",
            "Move all currently listed captures to deleted? Files are purged later by the retention pass. You can restore them until then.",
            confirmText: "Clear history");

        if (confirmed)
        {
            await _viewModel.ClearHistoryAsync().ConfigureAwait(true);
        }
    }
}

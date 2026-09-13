using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Octadock.App.Windows;

namespace Octadock.App.History;

/// <summary>A local image library with an optional metadata inspector.</summary>
[SupportedOSPlatform("windows")]
public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _viewModel;
    private bool _detailsOpen;

    public static readonly DependencyProperty ThumbnailWidthProperty =
        DependencyProperty.Register(nameof(ThumbnailWidth), typeof(double), typeof(HistoryWindow),
            new PropertyMetadata(236d));

    public static readonly DependencyProperty ThumbnailHeightProperty =
        DependencyProperty.Register(nameof(ThumbnailHeight), typeof(double), typeof(HistoryWindow),
            new PropertyMetadata(176d));

    public double ThumbnailWidth
    {
        get => (double)GetValue(ThumbnailWidthProperty);
        private set => SetValue(ThumbnailWidthProperty, value);
    }

    public double ThumbnailHeight
    {
        get => (double)GetValue(ThumbnailHeightProperty);
        private set => SetValue(ThumbnailHeightProperty, value);
    }

    /// <summary>Creates the history window with an injected view model.</summary>
    public HistoryWindow(HistoryViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        ScreenFit.Attach(this);
        DataContext = _viewModel;
        SizeChanged += (_, _) => AdaptLayout();
        _viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelChanged;
        Loaded += async (_, _) => await _viewModel.RefreshAsync().ConfigureAwait(true);
        Loaded += (_, _) =>
        {
            AdaptLayout();
            EntranceMotion.Play(RootGrid);
        };
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HistoryViewModel.HasSelection) or nameof(HistoryViewModel.SelectedItem))
        {
            if (!_viewModel.HasSelection) _detailsOpen = false;
            AdaptLayout();
        }
        if (e.PropertyName is nameof(HistoryViewModel.StatusMessage) or nameof(HistoryViewModel.CanLoadMore) or nameof(HistoryViewModel.IsEmpty))
            UpdateFooter();
    }

    private void AdaptLayout()
    {
        if (Header is null) return;
        bool compact = ActualWidth < 800;
        RootGrid.Margin = compact ? new Thickness(18, 16, 18, 12) : new Thickness(24, 20, 24, 14);
        FilterList.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactFilter.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(SearchContainer, compact ? 1 : 0);
        Grid.SetColumn(SearchContainer, compact ? 0 : 1);
        Grid.SetColumnSpan(SearchContainer, compact ? 3 : 1);
        SearchContainer.Margin = compact ? new Thickness(0, 15, 0, 0) : new Thickness(18, 0, 10, 0);
        SearchContainer.Width = compact ? double.NaN : 300;

        // Info is opt-in. On narrow windows it overlays only the library, leaving
        // the action bar reachable; on wide windows it shares the available width.
        bool showDetails = _detailsOpen && _viewModel.HasSelection;
        DetailsPanel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        HistoryBody.ColumnDefinitions[1].Width = new GridLength(!compact && showDetails ? 300 : 0);
        Grid.SetColumn(DetailsPanel, compact ? 0 : 1);
        DetailsPanel.HorizontalAlignment = compact ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        DetailsPanel.Width = compact ? Math.Min(320, Math.Max(0, HistoryBody.ActualWidth)) : double.NaN;
        CaptureGrid.Padding = new Thickness(0, 0, 0, _viewModel.HasSelection ? 66 : 0);
        UpdateThumbnailSize();
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        bool hasNotice = !_viewModel.IsEmpty &&
            !string.IsNullOrWhiteSpace(_viewModel.StatusMessage) &&
            !_viewModel.StatusMessage.EndsWith(" shown", StringComparison.Ordinal);
        FooterMessage.Visibility = hasNotice ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = hasNotice || _viewModel.CanLoadMore ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGallerySizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumbnailSize();

    private void UpdateThumbnailSize()
    {
        if (CaptureGrid is null || CaptureGrid.ActualWidth <= 0) return;
        // Reserve the scrollbar even when absent, so adding a row cannot oscillate
        // the column count at its boundary.
        double available = Math.Max(80, CaptureGrid.ActualWidth - SystemParameters.VerticalScrollBarWidth - 2);
        int columns = Math.Max(1, (int)Math.Floor(available / 224));
        if (available is >= 360 and < 448) columns = 2;
        ThumbnailWidth = Math.Max(72, Math.Floor(available / columns) - 8);
        ThumbnailHeight = Math.Round(ThumbnailWidth * 0.625);
    }

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasSelection) return;
        _detailsOpen = !_detailsOpen;
        AdaptLayout();
        if (_detailsOpen) CloseDetailsButton.Focus();
        else InfoButton.Focus();
    }

    private void OnOpenFilters(object sender, RoutedEventArgs e)
    {
        FiltersPopup.IsOpen = !FiltersPopup.IsOpen;
        if (FiltersPopup.IsOpen) DateFrom.Focus();
    }

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnCaptureDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(CaptureGrid, source) is ListBoxItem)
        {
            Execute(_viewModel.OpenCommand);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (FiltersPopup.IsOpen || _detailsOpen))
        {
            FiltersPopup.IsOpen = false;
            _detailsOpen = false;
            AdaptLayout();
            CaptureGrid.Focus();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        // Standard text editing must keep its own Ctrl+C, Delete and Enter keys.
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.C: Execute(_viewModel.CopyCommand); e.Handled = true; break;
                case Key.S: Execute(_viewModel.SaveCommand); e.Handled = true; break;
                case Key.I when _viewModel.HasSelection:
                    OnToggleDetails(sender, e);
                    e.Handled = true;
                    break;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && CaptureGrid.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter) { Execute(_viewModel.OpenCommand); e.Handled = true; }
            if (e.Key == Key.Delete) { Execute(_viewModel.DeleteCommand); e.Handled = true; }
        }
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
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

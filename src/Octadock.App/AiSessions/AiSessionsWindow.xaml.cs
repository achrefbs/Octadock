using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;

namespace Octadock.App.AiSessions;

/// <summary>
/// The first visible Active AI Sessions surface. It lists persisted run/watch
/// sessions and shows the selected timeline so the CLI foundation is no longer
/// hidden behind notifications only. While open it refreshes itself every few
/// seconds so it never disagrees with the overlay or keeps showing dead
/// sessions as "Running".
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AiSessionsWindow : Window
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly AiSessionsViewModel _viewModel;
    private readonly DispatcherTimer _autoRefresh;

    /// <summary>Creates the window with an injected view model.</summary>
    public AiSessionsWindow(AiSessionsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        _autoRefresh = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = AutoRefreshInterval,
        };
        _autoRefresh.Tick += OnAutoRefreshTick;

        Loaded += OnLoaded;
        Closed += (_, _) => _autoRefresh.Stop();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _autoRefresh.Start();
        await _viewModel.RefreshAsync().ConfigureAwait(true);
    }

    private async void OnAutoRefreshTick(object? sender, EventArgs e)
    {
        if (_viewModel.IsBusy || !IsVisible)
        {
            return;
        }

        // Repository read only — the discovery loop keeps the data fresh.
        await _viewModel.RefreshAsync(runDiscovery: false).ConfigureAwait(true);
    }
}

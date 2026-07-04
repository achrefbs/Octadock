using System.Runtime.Versioning;
using System.Windows;

namespace Octadock.App.AiSessions;

/// <summary>
/// The first visible Active AI Sessions surface. It lists persisted run/watch
/// sessions and shows the selected timeline so the CLI foundation is no longer
/// hidden behind notifications only.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AiSessionsWindow : Window
{
    private readonly AiSessionsViewModel _viewModel;

    /// <summary>Creates the window with an injected view model.</summary>
    public AiSessionsWindow(AiSessionsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshAsync().ConfigureAwait(true);
}

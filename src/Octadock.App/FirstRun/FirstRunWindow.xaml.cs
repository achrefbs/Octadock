using System.Runtime.Versioning;
using System.Windows;

namespace Octadock.App.FirstRun;

/// <summary>
/// The one-time first-run wizard. Shown modally by the window presenter when
/// <c>General.FirstRunCompleted</c> is false. Constructed through DI.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class FirstRunWindow : Window
{
    private readonly FirstRunViewModel _viewModel;

    /// <summary>Creates the first-run window with an injected view model.</summary>
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        Octadock.App.Windows.ScreenFit.Attach(this);
        DataContext = viewModel;
        viewModel.Completed += (_, _) => Close();
    }

}

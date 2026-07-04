using System.Runtime.Versioning;
using System.Windows;

namespace Octadock.App.TextTools;

/// <summary>The text-transform toolbox window.</summary>
[SupportedOSPlatform("windows")]
public partial class TextToolsWindow : Window
{
    /// <summary>Creates the window over its injected view model.</summary>
    public TextToolsWindow(TextToolsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }
}

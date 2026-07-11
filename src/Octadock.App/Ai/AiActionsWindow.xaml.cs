using System.Runtime.Versioning;
using System.Windows;
using Octadock.Core.Commands;

namespace Octadock.App.Ai;

/// <summary>Standard WPF surface for one reviewed, explicit AI text action.</summary>
[SupportedOSPlatform("windows")]
public partial class AiActionsWindow : Window
{
    private readonly AiActionsViewModel _viewModel;

    public AiActionsWindow(AiActionsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    public Task ApplyLaunchCommandAsync(
        OctadockCommand? command,
        CancellationToken cancellationToken = default)
        => _viewModel.ApplyLaunchCommandAsync(command, cancellationToken);

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}

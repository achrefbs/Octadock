using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;

namespace Octadock.App.Ai;

/// <summary>Compact compose/review/handoff surface for Agent Packets.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public partial class AgentWorkspaceWindow : Window
{
    private readonly AgentWorkspaceViewModel _viewModel;
    private readonly ICommandDispatcher _commands;
    private bool _initialized;

    public AgentWorkspaceWindow(
        AgentWorkspaceViewModel viewModel,
        ICommandDispatcher commands)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        InitializeComponent();
        Octadock.App.Windows.ScreenFit.Attach(this);
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    public Task ApplyLaunchCommandAsync(
        OctadockCommand? command,
        CancellationToken cancellationToken = default)
        => _viewModel.ApplyLaunchCommandAsync(command, cancellationToken);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.InitializeAsync().ConfigureAwait(true);
        AddFilesButton.Focus();
    }

    private async void OnDictateGoalClick(object sender, RoutedEventArgs e)
    {
        GoalBox.Focus();
        Keyboard.Focus(GoalBox);
        await Dispatcher.InvokeAsync(
            () => { },
            DispatcherPriority.ApplicationIdle);
        GoalBox.Focus();
        Keyboard.Focus(GoalBox);
        await _commands.DispatchAsync(OctadockCommand.Create(CommandType.Dictation)).ConfigureAwait(true);
    }

    private void OnCommitDraftInput(object sender, RoutedEventArgs e)
        => CommitTextBindings(this);

    private static void CommitTextBindings(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is TextBox textBox)
            {
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }

            CommitTextBindings(child);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel.IsNotBusy && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!_viewModel.IsNotBusy ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] paths ||
            paths.Length == 0)
        {
            return;
        }

        await _viewModel.AddDroppedFilesAsync(paths).ConfigureAwait(true);
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}

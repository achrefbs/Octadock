using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Octadock.Core.Hotkeys;
using Octadock.App.Windows;

namespace Octadock.App.Settings;

/// <summary>
/// The Octadock preferences window, bound to <see cref="SettingsViewModel"/>.
/// Constructed through DI so the view model receives its Core services.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    /// <summary>Switches the sidebar to a page selector when space is limited.</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(SettingsWindow), new PropertyMetadata(false));

    /// <summary>Whether the window uses compact navigation.</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    /// <summary>Plain navigation data avoids reparenting the real tab controls in the compact selector.</summary>
    public IReadOnlyList<SettingsNavigationItem> NavigationItems { get; } =
    [
        new("general", "General"),
        new("capture", "Screenshots"),
        new("shelf", "Shelf"),
        new("recording", "Recording"),
        new("history", "History"),
        new("clipboard", "Clipboard"),
        new("ocr", "Text recognition"),
        new("dictation", "Dictation"),
        new("read-aloud", "Read aloud"),
        new("speech-advanced", "Voice models"),
        new("shortcuts", "Shortcuts"),
        new("automation", "Automation"),
        new("advanced", "Storage & reset"),
    ];

    /// <summary>Creates the settings window with an injected view model.</summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        Octadock.App.Windows.ScreenFit.Attach(this);
        DataContext = _viewModel;

        _viewModel.Saved += (_, _) => Close();

        // WPF may scroll the selected settings page to whichever child receives
        // initial focus. A settings window should always open at the page title.
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            ScrollVisiblePagesToTop,
            DispatcherPriority.ContextIdle);
        Loaded += (_, _) => EntranceMotion.Play(RootGrid);
        SizeChanged += (_, e) => IsCompact = e.NewSize.Width < 760;
    }

    /// <summary>
    /// Selects the tab whose <c>Tag</c> matches the given key (e.g.
    /// "shortcuts"). Legacy category links select their first preference page.
    /// </summary>
    public void SelectTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab))
        {
            return;
        }

        string page = tab.ToLowerInvariant() switch
        {
            "section-capture" => "capture",
            "speech" => "dictation",
            "section-library" => "shelf",
            "section-system" => "general",
            _ => tab,
        };

        foreach (TabItem item in Tabs.Items.OfType<TabItem>())
        {
            if (string.Equals(item.Tag as string, page, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = item;
                return;
            }
        }
    }

    private void OnPageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Tabs) || !IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            ScrollVisiblePagesToTop();
            if (Tabs.SelectedItem is TabItem { Content: FrameworkElement content })
            {
                EntranceMotion.Play(content);
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnGestureCaptured(object sender, HotkeyGesture gesture)
    {
        if (sender is FrameworkElement { Tag: HotkeyGestureViewModel row })
        {
            _viewModel.ApplyGesture(row, gesture);
        }
    }

    private void OnBrowseSaveDirectory(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the folder captures are saved to",
        };

        if (!string.IsNullOrWhiteSpace(_viewModel.SaveDirectory) && System.IO.Directory.Exists(_viewModel.SaveDirectory))
        {
            dialog.InitialDirectory = _viewModel.SaveDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SaveDirectory = dialog.FolderName;
        }
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        // B-9: same confirmation the History window shows — clearing every
        // capture should never be a single silent click.
        bool confirmed = ConfirmationDialog.Confirm(
            this,
            "Clear history",
            "Move all captures to deleted? Files are purged later by the retention pass. You can restore them until then.",
            confirmText:
                "Clear history");

        if (confirmed && _viewModel.ClearHistoryCommand.CanExecute(null))
        {
            await _viewModel.ClearHistoryCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ScrollVisiblePagesToTop()
    {
        foreach (ScrollViewer viewer in FindVisualChildren<ScrollViewer>(this))
        {
            if (viewer.IsVisible && viewer.ScrollableHeight > 0)
            {
                viewer.ScrollToTop();
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

/// <summary>A lightweight label and route for compact preference navigation.</summary>
public sealed record SettingsNavigationItem(string Key, string Label);

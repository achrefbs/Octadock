using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Preview;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The Capture Shelf window. A borderless, capture-excluded, always-on-top surface that
/// docks to the configured corner (<see cref="ShelfSettings.Anchor"/>, default
/// bottom-left) of the active monitor's <em>work area</em> — so it sits above the
/// taskbar — with <see cref="ShelfSettings.MarginDip"/> of breathing room. It hosts the
/// <see cref="ShelfViewModel"/>'s stacked cards, repositions itself when the display
/// topology changes or its own size changes, suspends the auto-close timer while hovered,
/// and hides when the shelf empties.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public partial class ShelfWindow : ToolWindowBase
{
    private readonly ShelfViewModel _viewModel;
    private readonly IMonitorService _monitors;
    private readonly ISettingsService _settings;
    private readonly FilePreviewService _preview;

    /// <summary>Creates the shelf window bound to its view model.</summary>
    public ShelfWindow(
        ShelfViewModel viewModel,
        IMonitorService monitors,
        ISettingsService settings,
        FilePreviewService preview)
    {
        _viewModel = viewModel;
        _monitors = monitors;
        _settings = settings;
        _preview = preview;

        InitializeComponent();
        DataContext = _viewModel;
        AllowDrop = true;

        _viewModel.Emptied += OnEmptied;
        _monitors.MonitorsChanged += OnMonitorsChanged;

        Loaded += OnLoaded;
        SizeChanged += (_, _) => Reposition();
        MouseEnter += (_, _) => _viewModel.SetHoverSuspended(true);
        MouseLeave += (_, _) => _viewModel.SetHoverSuspended(false);
        DragEnter += OnFileDragOver;
        DragOver += OnFileDragOver;
        Drop += OnFileDrop;
        Closed += OnClosed;
    }

    /// <summary>The view model driving the shelf.</summary>
    public ShelfViewModel ViewModel => _viewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
        Reposition();
    }

    /// <summary>Re-anchors the window to the configured corner of the active monitor.</summary>
    public void Reposition()
    {
        if (Hwnd == IntPtr.Zero)
        {
            return;
        }

        DisplayInfo monitor = _monitors.GetActiveMonitor();
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;

        // Current window size in physical pixels.
        int widthPx = (int)Math.Round(ActualWidth * scale);
        int heightPx = (int)Math.Round(ActualHeight * scale);
        if (widthPx <= 0 || heightPx <= 0)
        {
            // Fall back to the desired size before the first arrange pass completes.
            widthPx = (int)Math.Round(Math.Max(ActualWidth, 260) * scale);
            heightPx = (int)Math.Round(Math.Max(ActualHeight, 160) * scale);
        }

        int marginPx = (int)Math.Round(_settings.Current.Shelf.MarginDip * scale);
        PixelRect work = monitor.WorkArea;

        ShelfAnchor anchor = _settings.Current.Shelf.Anchor;
        int x = anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft
            ? work.X + marginPx
            : work.Right - widthPx - marginPx;
        int y = anchor is ShelfAnchor.TopLeft or ShelfAnchor.TopRight
            ? work.Y + marginPx
            : work.Bottom - heightPx - marginPx;

        // Keep the shelf fully inside the work area.
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - widthPx));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - heightPx));

        // SizeToContent owns the size; move-only so WPF's layout and our anchor agree.
        NativeMethods.MovePhysical(Hwnd, x, y);
    }

    private void OnMonitorsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(Reposition));

    /// <summary>Header "Clear all": closes every card (captures stay in history).</summary>
    private void OnClearAll(object sender, RoutedEventArgs e) => _viewModel.CloseAll();

    /// <summary>Header gear: opens Settings on the Shelf tab.</summary>
    private void OnOpenSettings(object sender, RoutedEventArgs e)
        => App.Services.GetService<IWindowPresenter>()?.ShowSettings("Shelf");

    private void OnEmptied(object? sender, EventArgs e)
    {
        // Nothing to show: hide (do not Close) so the singleton window can be reused.
        Hide();
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetFileDropPath(e.Data, out _, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!TryGetFileDropPath(e.Data, out string? path, out bool addToDock))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (addToDock)
        {
            await _preview.AddImageToDockAsync(path).ConfigureAwait(true);
        }
        else
        {
            await _preview.PreviewAsync(path).ConfigureAwait(true);
        }
    }

    private static bool TryGetFileDropPath(IDataObject data, out string path, out bool addToDock)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            if (TryPickDockImageDropPath(paths, out path))
            {
                addToDock = true;
                return true;
            }

            if (TryPickPreviewDropPath(paths, out path))
            {
                addToDock = false;
                return true;
            }
        }

        path = string.Empty;
        addToDock = false;
        return false;
    }

    internal static bool TryPickDockImageDropPath(IEnumerable<string>? paths, out string path)
    {
        if (paths is not null)
        {
            foreach (string candidate in paths)
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    ImageFileSupport.IsSupportedRasterPath(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    internal static bool TryPickPreviewDropPath(IEnumerable<string>? paths, out string path)
    {
        if (paths is not null)
        {
            foreach (string candidate in paths)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Emptied -= OnEmptied;
        _monitors.MonitorsChanged -= OnMonitorsChanged;
    }
}

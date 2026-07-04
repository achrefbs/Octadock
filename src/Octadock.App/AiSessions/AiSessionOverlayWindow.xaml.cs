using System.Runtime.Versioning;
using System.Windows;
using Octadock.App.CaptureUx;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.AiSessions;

/// <summary>Passive bottom-right surface for live/recent AI sessions.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public partial class AiSessionOverlayWindow : ToolWindowBase
{
    private const int MarginDip = 16;
    private const int MinOverlayDip = 58;
    private const int MaxOverlayDip = 420;
    private readonly IMonitorService _monitors;

    public AiSessionOverlayWindow(
        AiSessionOverlayViewModel viewModel,
        IMonitorService monitors)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));

        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        SizeChanged += (_, _) => Reposition();
        _monitors.MonitorsChanged += OnMonitorsChanged;
        Closed += OnClosed;
    }

    /// <summary>Re-anchors the overlay to the active monitor's bottom-right work area.</summary>
    public void Reposition()
    {
        if (Hwnd == IntPtr.Zero)
        {
            return;
        }

        DisplayInfo monitor = _monitors.GetActiveMonitor();
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        PixelRect work = monitor.WorkArea;
        int marginPx = (int)Math.Round(MarginDip * scale);
        double availableWidthDip = Math.Max(
            MinOverlayDip,
            (work.Width - (marginPx * 2)) / scale);
        double availableHeightDip = Math.Max(
            MinOverlayDip,
            (work.Height - (marginPx * 2)) / scale);

        SessionsHost.MaxWidth = Math.Min(MaxOverlayDip, availableWidthDip);
        SessionsHost.MaxHeight = availableHeightDip;
        UpdateLayout();

        PixelRect actual = NativeMethods.GetPhysicalWindowRect(Hwnd);
        int widthPx = actual.Width > 0
            ? actual.Width
            : (int)Math.Round(Math.Max(ActualWidth, MinOverlayDip) * scale);
        int heightPx = actual.Height > 0
            ? actual.Height
            : (int)Math.Round(Math.Max(ActualHeight, MinOverlayDip) * scale);

        widthPx = Math.Min(widthPx, Math.Max(1, work.Width - (marginPx * 2)));
        heightPx = Math.Min(heightPx, Math.Max(1, work.Height - (marginPx * 2)));

        int x = work.Right - widthPx - marginPx;
        int y = work.Bottom - heightPx - marginPx;
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - widthPx));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - heightPx));

        NativeMethods.MovePhysical(Hwnd, x, y);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
        Reposition();
    }

    private void OnMonitorsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(Reposition));

    private void OnClosed(object? sender, EventArgs e)
    {
        _monitors.MonitorsChanged -= OnMonitorsChanged;
        Loaded -= OnLoaded;
    }
}

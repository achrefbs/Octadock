using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Windows;

/// <summary>Keeps utility windows within the target display, including after DPI/topology changes.</summary>
[SupportedOSPlatform("windows")]
internal static class ScreenFit
{
    private static readonly ConditionalWeakTable<Window, State> States = new();

    public static void Attach(Window window)
    {
        if (States.TryGetValue(window, out _)) return;
        var state = new State(Math.Min(window.MinWidth, 480), Math.Min(window.MinHeight, 320));
        States.Add(window, state);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) => Place(window, center: true, atCursor: true);
        window.SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(
                (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
                {
                    // Clamp after native dragging finishes, not while crossing a display seam.
                    if (message == 0x0232) QueueFit(window, state); // WM_EXITSIZEMOVE
                    return 0;
                });
        };
        window.Loaded += (_, _) => Place(window, center: true, atCursor: true);
        window.DpiChanged += (_, _) => QueueFit(window, state);
        window.StateChanged += (_, _) => QueueFit(window, state);
        window.LocationChanged += (_, _) => QueueFit(window, state);
        window.SizeChanged += (_, _) => QueueFit(window, state);
        var monitors = App.Services.GetRequiredService<IMonitorService>();
        EventHandler changed = (_, _) => QueueFit(window, state);
        monitors.MonitorsChanged += changed;
        window.Closed += (_, _) => { state.Closed = true; monitors.MonitorsChanged -= changed; };
    }

    private static void QueueFit(Window window, State state)
    {
        if (state.Busy || state.Queued || state.Closed) return;
        state.Queued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            state.Queued = false;
            if (!state.Closed) Place(window);
        });
    }

    public static void Place(Window window, bool center = false, bool atCursor = false)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || window.WindowState != WindowState.Normal) return;
        if (!center && System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) return;
        if (!States.TryGetValue(window, out State? state)) return;
        if (state.Busy) return;
        state.Busy = true;
        try
        {
            var monitors = App.Services.GetRequiredService<IMonitorService>();
            PixelRect current = NativeMethods.GetPhysicalWindowRect(hwnd);
            DisplayInfo monitor = atCursor ? monitors.GetActiveMonitor() : monitors.GetMonitorFromPoint(current.Center);
            double scale = monitor.DpiScale > 0 ? monitor.DpiScale : 1;
            int margin = (int)Math.Round(12 * scale);
            double maxWidth = Math.Max(1, monitor.WorkArea.Width - margin * 2) / scale;
            double maxHeight = Math.Max(1, monitor.WorkArea.Height - margin * 2) / scale;
            window.MinWidth = Math.Min(state.MinWidth, maxWidth);
            window.MinHeight = Math.Min(state.MinHeight, maxHeight);
            window.MaxWidth = maxWidth;
            window.MaxHeight = maxHeight;
            if (double.IsFinite(window.Width)) window.Width = Math.Min(window.Width, maxWidth);
            if (double.IsFinite(window.Height)) window.Height = Math.Min(window.Height, maxHeight);
            int width = Math.Min(current.Width, (int)Math.Floor(maxWidth * scale));
            int height = Math.Min(current.Height, (int)Math.Floor(maxHeight * scale));
            PixelRect proposed = center
                ? new PixelRect(monitor.WorkArea.X + (monitor.WorkArea.Width - width) / 2,
                    monitor.WorkArea.Y + (monitor.WorkArea.Height - height) / 2, width, height)
                : new PixelRect(current.X, current.Y, width, height);
            PixelRect fitted = WindowPlacement.Fit(proposed, monitor.WorkArea, margin);
            if (current != fitted) NativeMethods.PositionPhysical(hwnd, fitted);
        }
        finally { state.Busy = false; }
    }

    private sealed class State(double minWidth, double minHeight)
    {
        public double MinWidth { get; } = minWidth;
        public double MinHeight { get; } = minHeight;
        public bool Busy { get; set; }
        public bool Queued { get; set; }
        public bool Closed { get; set; }
    }
}

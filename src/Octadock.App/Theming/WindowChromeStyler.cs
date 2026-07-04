using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Octadock.App.Theming;

/// <summary>
/// Applies native window chrome that matches the active Octadock palette: an
/// immersive dark (or light) title bar, a caption color taken from the theme's
/// surface color, and rounded corners on Windows 11. Without this, dark-themed
/// windows get glaring white Win32 title bars. All DWM calls are best-effort —
/// older Windows builds simply ignore the attributes they do not support.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowChromeStyler
{
    private const int DwmwaUseImmersiveDarkModeLegacy = 19; // pre-20H1 builds
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwcpRound = 2;

    private static bool _handlerRegistered;

    /// <summary>Whether the currently applied palette is dark (set by the theme manager).</summary>
    internal static bool CurrentIsDark { get; set; }

    /// <summary>
    /// Registers a class handler so every window in the app gets themed chrome
    /// as soon as it loads. Call once, before the first window is created.
    /// </summary>
    public static void RegisterAutoStyling()
    {
        if (_handlerRegistered)
        {
            return;
        }

        _handlerRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(static (sender, _) =>
            {
                if (sender is Window window)
                {
                    Apply(window, CurrentIsDark);
                }
            }));
    }

    /// <summary>Re-applies chrome to every open window (after a theme switch).</summary>
    public static void ApplyToAllWindows(bool dark)
    {
        CurrentIsDark = dark;
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        foreach (Window window in app.Windows)
        {
            Apply(window, dark);
        }
    }

    /// <summary>Applies themed chrome to one window. Safe on borderless windows (no-op).</summary>
    public static void Apply(Window window, bool dark)
    {
        // Layered/borderless tool windows draw their own chrome.
        if (window.AllowsTransparency || window.WindowStyle == WindowStyle.None)
        {
            return;
        }

        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        int enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }

        int corner = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

        // Blend the caption into the theme surface (Windows 11+; ignored elsewhere).
        if (TryGetThemeColor("Octadock.Color.Surface", out Color surface))
        {
            int caption = ToColorRef(surface);
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
        }

        if (TryGetThemeColor("Octadock.Color.Text", out Color text))
        {
            int caption = ToColorRef(text);
            _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref caption, sizeof(int));
        }
    }

    private static bool TryGetThemeColor(string key, out Color color)
    {
        if (Application.Current?.TryFindResource(key) is Color found)
        {
            color = found;
            return true;
        }

        color = default;
        return false;
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

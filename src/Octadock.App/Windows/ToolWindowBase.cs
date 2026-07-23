using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;

namespace Octadock.App.Windows;

/// <summary>
/// Base class for Octadock's chrome-less tool windows (selection overlays, the
/// Capture Shelf, the HUD). It is borderless, transparent,
/// click-hosted above other windows and kept out of the taskbar. On
/// <see cref="Window.SourceInitialized"/> it applies capture exclusion by
/// resolving <see cref="ICaptureExclusion"/> and <see cref="ISettingsService"/>
/// from <see cref="App.Services"/> and calling
/// <c>SetExcluded(new WindowHandle(hwnd), settings.Capture.ExcludeOctadockWindows)</c>.
/// Exclusion is the privacy-safe default; the positive Settings opt-in reapplies
/// <c>WDA_NONE</c> live so these overlays can appear in screen captures and
/// desktop recordings.
/// </summary>
[SupportedOSPlatform("windows")]
public class ToolWindowBase : Window
{
    /// <summary>Creates a borderless, transparent, topmost tool window.</summary>
    public ToolWindowBase()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = Environment.GetEnvironmentVariable(UiAuditEnvVar) == "1";
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.Transparent;

        // Passive overlays should not steal activation from the app being captured;
        // interactive callers explicitly Activate() when they need keyboard focus.
        WindowStartupLocation = WindowStartupLocation.Manual;
    }

    /// <summary>The native window handle, valid after <see cref="Window.SourceInitialized"/>.</summary>
    protected IntPtr Hwnd { get; private set; }

    private ISettingsService? _observedSettings;
    private EventHandler<SettingsChangedEventArgs>? _settingsChangedHandler;

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Hwnd = new WindowInteropHelper(this).Handle;
        ApplyCaptureExclusion();

        // M15: re-apply exclusion when settings change, so toggling
        // capture.excludeOctadockWindows reaches windows that are already open
        // instead of only newly created ones.
        _observedSettings = App.Services.GetService<ISettingsService>();
        if (_observedSettings is not null)
        {
            _settingsChangedHandler = (_, _) => Dispatcher.BeginInvoke(ApplyCaptureExclusion);
            _observedSettings.Changed += _settingsChangedHandler;
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (_observedSettings is not null && _settingsChangedHandler is not null)
        {
            _observedSettings.Changed -= _settingsChangedHandler;
            _settingsChangedHandler = null;
            _observedSettings = null;
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Dev/design escape hatch: when this environment variable is "1", tool
    /// windows are never capture-excluded, so external tools can screenshot
    /// Octadock's own glass UI (docs/design work). Normal runs ignore it.
    /// </summary>
    internal const string DisableCaptureExclusionEnvVar = "OCTADOCK_DISABLE_CAPTURE_EXCLUSION";

    /// <summary>
    /// Design-QA escape hatch: exposes normally hidden tool windows to taskbar/
    /// window-enumeration tools so their rendered state can be inspected. Normal
    /// launches never set it and keep the native unobtrusive behavior.
    /// </summary>
    internal const string UiAuditEnvVar = "OCTADOCK_UI_AUDIT";

    /// <summary>
    /// Applies (or re-applies) capture exclusion for this window based on the
    /// current <c>capture.excludeOctadockWindows</c> setting. Safe to call again
    /// after the setting changes.
    /// </summary>
    protected void ApplyCaptureExclusion()
    {
        if (Hwnd == IntPtr.Zero)
        {
            return;
        }

        IServiceProvider services = App.Services;
        var exclusion = services.GetService<ICaptureExclusion>();
        var settings = services.GetService<ISettingsService>();
        if (exclusion is null || settings is null)
        {
            return;
        }

        bool exclude = settings.Current.Capture.ExcludeOctadockWindows
            && Environment.GetEnvironmentVariable(DisableCaptureExclusionEnvVar) != "1";
        exclusion.SetExcluded(new WindowHandle(Hwnd), exclude);
    }

    /// <summary>
    /// Resolves the monitor that owns this window (the one under its top-left
    /// corner, falling back to the active monitor). Useful for anchoring the shelf
    /// with the correct DPI scale.
    /// </summary>
    protected DisplayInfo GetOwningMonitor()
    {
        var monitors = App.Services.GetRequiredService<IMonitorService>();

        if (Hwnd != IntPtr.Zero)
        {
            // Left/Top are DIPs on the primary; for a manually-placed tool window
            // they are set in the same virtual coordinate space we position with,
            // so treat them as physical-pixel anchors for monitor resolution.
            var point = new PixelPoint((int)Math.Round(Left), (int)Math.Round(Top));
            return monitors.GetMonitorFromPoint(point);
        }

        return monitors.GetActiveMonitor();
    }
}

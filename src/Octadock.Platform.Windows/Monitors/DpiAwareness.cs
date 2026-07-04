using System.Runtime.Versioning;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Monitors;

/// <summary>
/// Ensures the process is per-monitor-DPI-v2 aware so all Win32 rectangles are
/// reported in true physical pixels. The WPF app normally declares this in its
/// application manifest; this helper is a defensive programmatic fallback that
/// the host may call at startup. It is a no-op once awareness is already set.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DpiAwareness
{
    private static int _applied;

    /// <summary>
    /// Attempts to switch the process to Per-Monitor-DPI-Aware v2. Safe to call
    /// multiple times; only the first successful call has effect. Returns true if
    /// the context was set by this call, false if it was already set (or setting
    /// it failed because a context is locked in via the manifest).
    /// </summary>
    public static bool EnsurePerMonitorV2()
    {
        // Guard so repeated calls do not spam the API. The manifest may already
        // have pinned an awareness context, in which case SetProcess... fails
        // with ERROR_ACCESS_DENIED — which is a benign "already configured".
        if (Interlocked.Exchange(ref _applied, 1) == 1)
        {
            return false;
        }

        if (DpiApi.SetProcessDpiAwarenessContext(DpiApi.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        {
            return true;
        }

        // v2 unavailable on very old builds; try v1 before giving up.
        return DpiApi.SetProcessDpiAwarenessContext(DpiApi.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE);
    }
}

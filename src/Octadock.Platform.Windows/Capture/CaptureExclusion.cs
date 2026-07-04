using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Capture;

/// <summary>
/// Wraps <c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c> so Octadock's
/// own overlays, shelf and pins are omitted from supported capture APIs. Full
/// exclude behavior requires Windows 10 version 2004 (build 19041) or later.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CaptureExclusion : ICaptureExclusion
{
    private readonly ILogger<CaptureExclusion> _logger;

    /// <summary>Creates the capture-exclusion helper.</summary>
    public CaptureExclusion(ILogger<CaptureExclusion> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsSupported => OsVersion.SupportsCaptureExclusion;

    /// <inheritdoc />
    public bool SetExcluded(WindowHandle window, bool excluded)
    {
        if (!window.IsValid)
        {
            return false;
        }

        if (!IsSupported)
        {
            _logger.LogDebug(
                "SetWindowDisplayAffinity exclusion unsupported on build {Build}; window {Window} not excluded.",
                OsVersion.BuildNumber,
                window);
            return false;
        }

        uint affinity = excluded ? NativeConstants.WDA_EXCLUDEFROMCAPTURE : NativeConstants.WDA_NONE;
        bool ok = User32.SetWindowDisplayAffinity((nint)window, affinity);
        if (!ok)
        {
            _logger.LogWarning(
                "SetWindowDisplayAffinity({Affinity}) failed for {Window} (error {Error}).",
                affinity,
                window,
                global::System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        return ok;
    }
}

namespace Octadock.Core.Models;

/// <summary>
/// Best-effort provenance for a capture: the foreground process and window it
/// came from. Any field may be <c>null</c> when the information is unavailable
/// (for example, fullscreen or all-monitor captures). <see cref="HwndHash"/> is
/// a privacy-preserving fingerprint of the source window handle rather than the
/// raw handle value.
/// </summary>
public sealed record CaptureSource(
    string? ProcessName,
    string? WindowTitle,
    string? HwndHash)
{
    /// <summary>An empty source with no known provenance.</summary>
    public static readonly CaptureSource Empty = new(null, null, null);

    public bool IsEmpty => ProcessName is null && WindowTitle is null && HwndHash is null;
}

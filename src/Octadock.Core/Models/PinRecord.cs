using Octadock.Core.Geometry;

namespace Octadock.Core.Models;

/// <summary>
/// Persisted state for a floating pin so pins can be restored across sessions.
/// Position and size are in physical pixels on the virtual desktop.
/// </summary>
public sealed record PinRecord
{
    public required Guid Id { get; init; }

    /// <summary>The capture the pin displays, when it originates from history.</summary>
    public Guid? CaptureId { get; init; }

    /// <summary>Root-relative managed image path for pins created outside capture history.</summary>
    public string? ImagePath { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Opacity in the range 0..1.</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>Whether the pin is in click-through (locked) mode.</summary>
    public bool ClickThrough { get; init; }

    public MonitorId MonitorId { get; init; } = MonitorId.Unknown;

    public DateTimeOffset? LastVisibleAt { get; init; }

    public PixelRect Bounds => new(X, Y, Width, Height);
}

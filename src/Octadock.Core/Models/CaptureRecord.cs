using Octadock.Core.Geometry;

namespace Octadock.Core.Models;

/// <summary>
/// The indexed metadata for one capture, mirroring the <c>captures</c> table and
/// the capture JSON in the data spec. Paths are stored relative to the Octadock
/// data root so the library remains portable.
/// </summary>
public sealed record CaptureRecord
{
    /// <summary>Stable unique id.</summary>
    public required Guid Id { get; init; }

    public required CaptureType Type { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Foreground process/window provenance (may be empty).</summary>
    public CaptureSource Source { get; init; } = CaptureSource.Empty;

    public MonitorId MonitorId { get; init; } = MonitorId.Unknown;

    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }

    public double DpiScale { get; init; } = 1.0;

    /// <summary>Path to the original raster, relative to the data root.</summary>
    public required string OriginalPath { get; init; }

    /// <summary>Path to the cached thumbnail, relative to the data root.</summary>
    public string? ThumbnailPath { get; init; }

    /// <summary>Path to the editable <c>.octadock</c> project, if one exists.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Latest user-approved AI mockup derived from this capture.</summary>
    public string? ApprovedMockupPath { get; init; }

    /// <summary>Duration for recordings; <c>null</c> for stills.</summary>
    public long? DurationMs { get; init; }

    /// <summary>When set, the capture is soft-deleted and pending cleanup.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>True when the capture has been soft-deleted.</summary>
    public bool IsDeleted => DeletedAt is not null;

    public PixelSize PixelSize => new(PixelWidth, PixelHeight);

    /// <summary>True when this record represents a video recording.</summary>
    public bool IsRecording => Type == CaptureType.Recording;
}

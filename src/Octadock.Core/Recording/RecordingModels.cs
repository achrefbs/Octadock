using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.Core.Recording;

/// <summary>Lifecycle state of a recording session.</summary>
public enum RecordingState
{
    Idle = 0,
    Countdown,
    Recording,
    Paused,
    Finalizing,
    Completed,
    Failed,
    Canceled,
}

/// <summary>
/// App-level request for starting a screen recording. The controller resolves
/// prompts and monitor tokens into concrete <see cref="RecordingOptions"/>.
/// </summary>
public sealed record RecordingStartRequest
{
    /// <summary>Default request: record the active monitor.</summary>
    public static RecordingStartRequest ActiveMonitor { get; } = new();

    /// <summary>Record this region in physical pixels. Takes precedence over <see cref="Monitor"/>.</summary>
    public PixelRect? Region { get; init; }

    /// <summary>Record this monitor when <see cref="Region"/> is null.</summary>
    public MonitorId? Monitor { get; init; }

    /// <summary>Prompt the user for an area before starting the recording.</summary>
    public bool PromptForRegion { get; init; }
}

/// <summary>Options for starting a screen recording.</summary>
public sealed record RecordingOptions
{
    /// <summary>Region to record, in physical pixels. When null, records the monitor.</summary>
    public PixelRect? Region { get; init; }

    /// <summary>Monitor to record when <see cref="Region"/> is null.</summary>
    public MonitorId? Monitor { get; init; }

    public int Fps { get; init; } = 30;

    public RecordingQuality Quality { get; init; } = RecordingQuality.Medium;

    public bool IncludeCursor { get; init; } = true;

    public bool IncludeMicrophone { get; init; }

    public bool IncludeSystemAudio { get; init; }

    /// <summary>Countdown seconds before recording starts (0 = none).</summary>
    public int CountdownSeconds { get; init; } = 3;

    /// <summary>Destination file path for the encoded MP4.</summary>
    public required string OutputPath { get; init; }
}

/// <summary>The result of a completed recording.</summary>
public sealed record RecordingResult
{
    public required string OutputPath { get; init; }

    public required long DurationMs { get; init; }

    public PixelSize FrameSize { get; init; }

    public long FileSizeBytes { get; init; }
}

/// <summary>Progress notification raised while recording.</summary>
public sealed record RecordingProgress(RecordingState State, long ElapsedMs, int? CountdownRemaining = null);

using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Core.Recording;

namespace Octadock.Core.Abstractions;

/// <summary>
/// Screen-recording engine. The shipped implementation encodes H.264/MP4 via
/// Media Foundation over a Windows.Graphics.Capture frame stream. Audio options
/// are modeled for the roadmap, but the current app build records video only.
/// </summary>
public interface IRecordingEngine
{
    /// <summary>True when recording is supported on this machine.</summary>
    bool IsSupported { get; }

    RecordingState State { get; }

    /// <summary>Raised as the session progresses (countdown, elapsed time, state changes).</summary>
    event EventHandler<RecordingProgress>? ProgressChanged;

    /// <summary>Begins a recording session (including any countdown).</summary>
    Task StartAsync(RecordingOptions options, CancellationToken cancellationToken = default);

    /// <summary>Stops and finalizes the recording, returning the encoded result.</summary>
    Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses an in-progress recording (P2; may be a no-op in early builds).</summary>
    void Pause();

    /// <summary>Resumes a paused recording.</summary>
    void Resume();

    /// <summary>Cancels and discards the current recording.</summary>
    Task CancelAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Captures a tall/wide region across manual or automatic scrolling and stitches
/// the frames into one image. Scaffolded for the P1 scrolling-capture milestone.
/// </summary>
public interface IScrollingCaptureEngine
{
    /// <summary>True when auto-scroll via UI Automation is possible for the target.</summary>
    bool SupportsAutoScroll { get; }

    /// <summary>Begins a scrolling-capture session over a region.</summary>
    Task StartSessionAsync(PixelRect region, ScrollingCaptureOptions options, CancellationToken cancellationToken = default);

    /// <summary>Adds the current viewport frame to the session, returning the running stitched height.</summary>
    Task<int> CaptureFrameAsync(CancellationToken cancellationToken = default);

    /// <summary>Finishes the session and returns the stitched image.</summary>
    Task<ScrollingCaptureResult> FinishAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels the session and releases resources.</summary>
    void Cancel();
}

/// <summary>Options for a scrolling-capture session.</summary>
public sealed record ScrollingCaptureOptions
{
    public Commands.ScrollDirection Direction { get; init; } = Commands.ScrollDirection.Vertical;

    public bool AutoScroll { get; init; }

    /// <summary>Warn/stop when the stitched output exceeds this many pixels tall/wide.</summary>
    public int MaxStitchedEdge { get; init; } = 32000;
}

/// <summary>Final image and completion metadata for a scrolling-capture session.</summary>
public sealed record ScrollingCaptureResult(
    CapturedFrame Frame,
    bool Truncated,
    int StitchedHeight,
    int MaxStitchedEdge);

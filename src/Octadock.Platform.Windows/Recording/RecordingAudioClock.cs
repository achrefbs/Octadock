using System.Runtime.Versioning;

namespace Octadock.Platform.Windows.Recording;

/// <summary>
/// Media-time clock for one recording audio track. Samples are stamped from a
/// running sample count (jitter-free), while the wall-clock elapsed time is only
/// used to bound long-run drift between the audio device clock and the system
/// clock: when the audio timeline lags the wall clock beyond a tolerance, the
/// gap is padded with silence; when it runs ahead beyond a tolerance, the
/// current chunk is dropped. Both corrections are bounded per call, so A/V sync
/// stays within roughly the tolerance over arbitrarily long recordings.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class RecordingAudioClock
{
    internal const long HnsPerSecond = 10_000_000L;

    /// <summary>How far the audio timeline may lag the wall clock before silence is padded.</summary>
    internal const long LagToleranceHns = HnsPerSecond / 10; // 100 ms

    /// <summary>How far the audio timeline may run ahead before a chunk is dropped.</summary>
    internal const long AheadToleranceHns = HnsPerSecond / 10; // 100 ms

    private readonly int _sampleRate;
    private long _nextTimestampHns;

    internal RecordingAudioClock(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        _sampleRate = sampleRate;
    }

    /// <summary>Media timestamp (100-ns units) of the next unwritten sample.</summary>
    internal long NextTimestampHns => _nextTimestampHns;

    internal long FramesToHns(long frames) => frames * HnsPerSecond / _sampleRate;

    internal long HnsToFrames(long hns) => hns * _sampleRate / HnsPerSecond;

    /// <summary>Returns the timestamp for a chunk of <paramref name="frames"/> frames and advances the timeline past it.</summary>
    internal long Stamp(long frames)
    {
        long timestamp = _nextTimestampHns;
        _nextTimestampHns += FramesToHns(frames);
        return timestamp;
    }

    /// <summary>
    /// Silence frames to insert so the audio timeline catches up with the wall
    /// clock; 0 while the lag stays within <paramref name="toleranceHns"/>.
    /// The result is capped so one correction never inserts a huge gap; the
    /// remainder is padded on the following calls.
    /// </summary>
    internal long LagCompensationFrames(long elapsedHns, long toleranceHns, long maxFrames)
    {
        long lag = elapsedHns > _nextTimestampHns ? HnsToFrames(elapsedHns - _nextTimestampHns) : 0;
        if (lag <= HnsToFrames(toleranceHns))
        {
            return 0;
        }

        return Math.Min(lag, maxFrames);
    }

    /// <summary>True when the audio timeline runs ahead of the wall clock beyond the tolerance.</summary>
    internal bool IsAhead(long elapsedHns, long toleranceHns)
        => _nextTimestampHns - elapsedHns > toleranceHns;
}

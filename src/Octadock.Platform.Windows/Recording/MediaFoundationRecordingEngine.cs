using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Geometry;
using Octadock.Core.Recording;
using Octadock.Core.Settings;
using Octadock.Platform.Windows.Capture;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Recording;

/// <summary>
/// Screen-recording engine that encodes H.264/MP4 via Media Foundation over a
/// Windows.Graphics.Capture frame stream. Implements the full lifecycle state
/// machine and progress reporting. Microphone and system-audio (WASAPI
/// loopback) tracks are explicit per-recording opt-ins, encoded as AAC at
/// 48 kHz stereo alongside the video stream.
/// </summary>
/// <remarks>
/// Hardware validation note: the Media Foundation sink-writer, sample timing
/// and frame/audio pumps depend on the machine's encoder MFT, GPU, audio
/// devices and writable output path.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class MediaFoundationRecordingEngine : IRecordingEngine, IDisposable
{
    // 100-nanosecond ticks per second (Media Foundation timestamp unit).
    private const long HnsPerSecond = 10_000_000L;

    private readonly IMonitorService _monitors;
    private readonly ILogger<MediaFoundationRecordingEngine> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly object _gate = new();

    // Serializes sink-writer finalize/release across the pump failure path,
    // Stop/Cancel, and Dispose (C-2).
    private readonly object _encoderGate = new();

    private RecordingState _state = RecordingState.Idle;
    private RecordingOptions? _options;
    private CancellationTokenSource? _sessionCts;
    private Task? _pumpTask;
    private Task? _audioPumpTask;
    private IReadOnlyList<AudioTrack>? _audioTracks;
    private uint _microphoneStreamIndex;
    private uint _systemAudioStreamIndex;
    private WgcFrameGrabber? _grabber;
    private nint _sinkWriterPtr;
    private IMFSinkWriter? _sinkWriter;
    private uint _streamIndex;
    private bool _mfStarted;
    private readonly Stopwatch _elapsed = new();
    private PixelSize _frameSize;
    private long _outputFileSize;
    private long _writtenFrameCount;
    private Exception? _pumpFailure;
    private bool _disposed;

    /// <summary>Creates the recording engine.</summary>
    public MediaFoundationRecordingEngine(
        IMonitorService monitors,
        ILogger<MediaFoundationRecordingEngine> logger,
        ILoggerFactory loggerFactory)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public event EventHandler<RecordingProgress>? ProgressChanged;

    /// <inheritdoc />
    public bool IsSupported => OsVersion.BuildNumber >= OsVersion.Build2004;

    /// <inheritdoc />
    public RecordingState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(RecordingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);

        lock (_gate)
        {
            if (_state is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused)
            {
                throw new InvalidOperationException("A recording is already in progress.");
            }

            _options = options;
            _sessionCts = new CancellationTokenSource();
            _outputFileSize = 0;
            _writtenFrameCount = 0;
            _pumpFailure = null;
            _microphoneStreamIndex = 0;
            _systemAudioStreamIndex = 0;
        }

        try
        {
            // Fail fast on disk pressure, before the user waits out the countdown.
            EnsureSufficientDiskSpace(options.OutputPath, ProbeAvailableFreeBytes);

            await RunCountdownAsync(options.CountdownSeconds, cancellationToken).ConfigureAwait(false);

            // Resolve the capture target rectangle.
            (PixelRect region, nint _, DisplayInfo _) = ResolveTarget(options);
            _frameSize = new PixelSize(region.Width, region.Height);

            InitializeEncoder(options, region);

            SetState(RecordingState.Recording);
            _elapsed.Restart();

            // Audio devices are acquired only here — an explicit per-recording
            // opt-in, never ambient — and a device that cannot be acquired
            // fails the whole start instead of silently recording without the
            // requested track.
            IReadOnlyList<AudioTrack> audioTracks = StartAudioTracks(options);
            _audioTracks = audioTracks;

            CancellationToken sessionToken = _sessionCts!.Token;
            _pumpTask = Task.Run(() => FramePumpLoopAsync(options, region, sessionToken), sessionToken);
            if (audioTracks.Count > 0)
            {
                _audioPumpTask = Task.Run(() => AudioPumpLoopAsync(audioTracks, sessionToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            SetState(RecordingState.Canceled);

            // C-3: a cancellation after InitializeEncoder leaves MFStartup and
            // the sink writer live; abandon them and drop the partial file so
            // nothing is leaked or left locked.
            AbandonEncoder();
            CleanupSession();
            TryDeleteFile(options.OutputPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start recording.");
            SetState(RecordingState.Failed);

            // C-3: InitializeEncoder can fail after MFStartup succeeded (or
            // after the sink writer was created); without an abandon here the
            // MFStartup refcount stays unbalanced and the output file stays
            // locked for the process lifetime.
            AbandonEncoder();
            CleanupSession();
            TryDeleteFile(options.OutputPath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
    {
        RecordingOptions options;
        lock (_gate)
        {
            bool failedPumpCanBeCollected =
                _state == RecordingState.Failed && _options is not null && _pumpTask is not null;
            if (_state is not (RecordingState.Recording or RecordingState.Paused) && !failedPumpCanBeCollected)
            {
                throw new InvalidOperationException("No active recording to stop.");
            }

            options = _options!;
        }

        SetState(RecordingState.Finalizing);
        _elapsed.Stop();

        _sessionCts?.Cancel();
        bool pumpsCompleted = await WaitForPumpsAsync("stop", cancellationToken).ConfigureAwait(false);
        if (!pumpsCompleted)
        {
            // C-2: a pump still owns the sink writer. Finalizing (or even
            // releasing) it now is a use-after-free on a live writer thread —
            // deliberately leave the encoder alone, fail the recording, and let
            // Dispose handle whatever remains once the pumps die.
            SetState(RecordingState.Failed);
            CleanupSession();
            throw new TimeoutException(
                "The recording frame or audio pump did not stop; the output file was not finalized.");
        }

        Exception? pumpFailure = Volatile.Read(ref _pumpFailure);
        if (pumpFailure is not null)
        {
            AbandonEncoder();
            SetState(RecordingState.Failed);
            CleanupSession();
            TryDeleteFile(options.OutputPath);
            throw new InvalidOperationException(
                "The recording stopped because the screen or audio stream could not be encoded.",
                pumpFailure);
        }

        long durationMs;
        long fileSize;
        try
        {
            FinalizeEncoder();
            durationMs = _elapsed.ElapsedMilliseconds;
            fileSize = TryGetFileSize(options.OutputPath);
            ValidateCompletedRecording(_writtenFrameCount, fileSize);
            if (!Mp4StructureValidator.IsValidRecording(options.OutputPath, out string structureError))
            {
                throw new InvalidDataException(
                    $"Windows produced an invalid MP4 recording: {structureError}.");
            }
        }
        catch
        {
            SetState(RecordingState.Failed);
            CleanupSession();
            TryDeleteFile(options.OutputPath);
            throw;
        }

        SetState(RecordingState.Completed);
        CleanupSession();

        return new RecordingResult
        {
            OutputPath = options.OutputPath,
            DurationMs = durationMs,
            FrameSize = _frameSize,
            FileSizeBytes = fileSize,
        };
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_gate)
        {
            if (_state == RecordingState.Recording)
            {
                _state = RecordingState.Paused;
                _elapsed.Stop();
            }
        }

        RaiseProgress();
    }

    /// <inheritdoc />
    public void Resume()
    {
        lock (_gate)
        {
            if (_state == RecordingState.Paused)
            {
                _state = RecordingState.Recording;
                _elapsed.Start();
            }
        }

        RaiseProgress();
    }

    /// <inheritdoc />
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        RecordingOptions? options;
        lock (_gate)
        {
            if (_state is RecordingState.Idle or RecordingState.Completed or RecordingState.Canceled)
            {
                return;
            }

            options = _options;
        }

        SetState(RecordingState.Canceled);
        _elapsed.Stop();

        _sessionCts?.Cancel();
        bool pumpsCompleted = await WaitForPumpsAsync("cancel", cancellationToken).ConfigureAwait(false);
        if (!pumpsCompleted)
        {
            // C-2: releasing the writer out from under a live pump thread is a
            // use-after-free; leave it for Dispose once the pump dies.
            CleanupSession();
            return;
        }

        // Abandon the sink writer without finalizing, then delete the partial file.
        AbandonEncoder();
        CleanupSession();

        if (options is not null)
        {
            TryDeleteFile(options.OutputPath);
        }
    }

    private async Task RunCountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            return;
        }

        SetState(RecordingState.Countdown);
        for (int remaining = seconds; remaining > 0; remaining--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RaiseProgressRaw(RecordingState.Countdown, elapsedMs: 0, countdownRemaining: remaining);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
    }

    private (PixelRect Region, nint Hmon, DisplayInfo Monitor) ResolveTarget(RecordingOptions options)
    {
        DisplayInfo monitor;
        if (options.Monitor is { } id && _monitors.FindById(id) is { } found)
        {
            monitor = found;
        }
        else
        {
            monitor = _monitors.GetActiveMonitor();
        }

        PixelRect region = ResolveRecordingRegion(options, monitor.Bounds);

        var center = new POINT(monitor.Bounds.Center.X, monitor.Bounds.Center.Y);
        nint hmon = User32.MonitorFromPoint(center, NativeConstants.MONITOR_DEFAULTTONEAREST);
        return (region, hmon, monitor);
    }

    internal static PixelRect ResolveRecordingRegion(RecordingOptions options, PixelRect monitorBounds)
    {
        ArgumentNullException.ThrowIfNull(options);

        PixelRect region = options.Region?.Normalized() ?? monitorBounds;

        // Encoders require even dimensions; round down width/height to multiples of 2.
        int w = region.Width - (region.Width % 2);
        int h = region.Height - (region.Height % 2);
        return new PixelRect(region.X, region.Y, Math.Max(2, w), Math.Max(2, h));
    }

    private void InitializeEncoder(RecordingOptions options, PixelRect region)
    {
        // Start Media Foundation for this session.
        int hr = MediaFoundation.MFStartup(MediaFoundation.MF_VERSION, MediaFoundation.MFSTARTUP_LITE);
        Marshal.ThrowExceptionForHR(hr);
        _mfStarted = true;

        // Enable hardware transforms: on machines whose registered H.264 encoder
        // is a hardware MFT (NVENC/AMF/QuickSync), software-only negotiation can
        // fail to build an RGB32 → encoder chain (MF_E_INVALIDMEDIATYPE).
        nint attributesPtr = nint.Zero;
        try
        {
            if (MediaFoundation.MFCreateAttributes(out attributesPtr, 1) == 0 && attributesPtr != nint.Zero)
            {
                var attributes = (IMFAttributes)Marshal.GetObjectForIUnknown(attributesPtr);
                try
                {
                    Guid hwKey = MfGuids.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS;
                    attributes.SetUINT32(ref hwKey, 1);
                }
                finally
                {
                    Marshal.ReleaseComObject(attributes);
                }
            }

            hr = MediaFoundation.MFCreateSinkWriterFromURL(options.OutputPath, nint.Zero, attributesPtr, out _sinkWriterPtr);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            if (attributesPtr != nint.Zero)
            {
                Marshal.Release(attributesPtr);
            }
        }

        _sinkWriter = (IMFSinkWriter)Marshal.GetObjectForIUnknown(_sinkWriterPtr);

        uint width = (uint)region.Width;
        uint height = (uint)region.Height;
        uint fps = (uint)Math.Clamp(options.Fps, 1, 240);
        uint bitrate = BitrateFor(options.Quality, width, height, fps);

        // Output (encoded) media type: H.264.
        nint outputType = CreateVideoMediaType(MfGuids.MFVideoFormat_H264, width, height, fps, bitrate);
        try
        {
            hr = _sinkWriter.AddStream(outputType, out _streamIndex);
            ThrowWithContext(hr, "AddStream(H264)", width, height, fps, bitrate);
        }
        finally
        {
            Marshal.Release(outputType);
        }

        // Input (uncompressed) media type: 32-bit BGRA from GDI. Some encoder
        // chains only accept it declared as RGB32, others only as ARGB32 —
        // identical memory layout, so try both before giving up.
        hr = TrySetInputType(MfGuids.MFVideoFormat_RGB32, width, height, fps);
        if (hr < 0)
        {
            _logger.LogDebug("RGB32 input rejected (0x{Hr:X8}); retrying as ARGB32.", hr);
            hr = TrySetInputType(MfGuids.MFVideoFormat_ARGB32, width, height, fps);
        }

        ThrowWithContext(hr, "SetInputMediaType(RGB32/ARGB32)", width, height, fps, bitrate);

        // Optional audio tracks (explicit per-recording opt-in): one AAC stream
        // per requested source. Streams must be added before BeginWriting.
        if (options.IncludeMicrophone)
        {
            _microphoneStreamIndex = AddAudioStream("microphone");
        }

        if (options.IncludeSystemAudio)
        {
            _systemAudioStreamIndex = AddAudioStream("system audio");
        }

        hr = _sinkWriter.BeginWriting();
        ThrowWithContext(hr, "BeginWriting", width, height, fps, bitrate);
    }

    private uint AddAudioStream(string displayName)
    {
        // Output (encoded) media type: AAC at the canonical recording format.
        nint outputType = RecordingAudioFormat.CreateAacOutputMediaType();
        uint streamIndex;
        int hr;
        try
        {
            hr = _sinkWriter!.AddStream(outputType, out streamIndex);
        }
        finally
        {
            Marshal.Release(outputType);
        }

        if (hr < 0)
        {
            throw new IOException($"AddStream(AAC {displayName}) failed with 0x{hr:X8}.", hr);
        }

        // Input (uncompressed) media type: 16-bit PCM from the audio pump.
        nint inputType = RecordingAudioFormat.CreatePcmInputMediaType();
        try
        {
            hr = _sinkWriter.SetInputMediaType(streamIndex, inputType, nint.Zero);
        }
        finally
        {
            Marshal.Release(inputType);
        }

        if (hr < 0)
        {
            throw new IOException($"SetInputMediaType(PCM {displayName}) failed with 0x{hr:X8}.", hr);
        }

        return streamIndex;
    }

    private IReadOnlyList<AudioTrack> StartAudioTracks(RecordingOptions options)
    {
        if (!options.IncludeMicrophone && !options.IncludeSystemAudio)
        {
            return [];
        }

        var tracks = new List<AudioTrack>(2);
        try
        {
            if (options.IncludeMicrophone)
            {
                RecordingAudioSource source = RecordingAudioSource.StartMicrophone(
                    _loggerFactory.CreateLogger<RecordingAudioSource>());
                tracks.Add(new AudioTrack(
                    source,
                    new RecordingAudioClock((int)RecordingAudioFormat.SampleRate),
                    _microphoneStreamIndex));
            }

            if (options.IncludeSystemAudio)
            {
                RecordingAudioSource source = RecordingAudioSource.StartSystemAudio(
                    _loggerFactory.CreateLogger<RecordingAudioSource>());
                tracks.Add(new AudioTrack(
                    source,
                    new RecordingAudioClock((int)RecordingAudioFormat.SampleRate),
                    _systemAudioStreamIndex));
            }

            return tracks;
        }
        catch
        {
            // A failed start must release any device already acquired.
            foreach (AudioTrack track in tracks)
            {
                track.Source.Dispose();
            }

            throw;
        }
    }

    private int TrySetInputType(Guid subtype, uint width, uint height, uint fps)
    {
        nint inputType = CreateVideoMediaType(
            subtype,
            width,
            height,
            fps,
            avgBitrate: 0,
            defaultStride: CalculateRgb32Stride(width));
        try
        {
            return _sinkWriter!.SetInputMediaType(_streamIndex, inputType, nint.Zero);
        }
        finally
        {
            Marshal.Release(inputType);
        }
    }

    private static void ThrowWithContext(int hr, string call, uint width, uint height, uint fps, uint bitrate)
    {
        if (hr < 0)
        {
            throw new IOException(
                $"{call} failed with 0x{hr:X8} for {width}x{height}@{fps}fps, bitrate {bitrate}.",
                hr);
        }
    }

    private static nint CreateVideoMediaType(Guid subtype, uint width, uint height, uint fps, uint avgBitrate, int? defaultStride = null)
    {
        int hr = MediaFoundation.MFCreateMediaType(out nint typePtr);
        Marshal.ThrowExceptionForHR(hr);

        IMFMediaType? mediaType = null;
        try
        {
            mediaType = (IMFMediaType)Marshal.GetObjectForIUnknown(typePtr);

            Guid major = MfGuids.MFMediaType_Video;
            Guid majorKey = MfGuids.MF_MT_MAJOR_TYPE;
            Marshal.ThrowExceptionForHR(mediaType.SetGUID(ref majorKey, ref major));

            Guid subtypeKey = MfGuids.MF_MT_SUBTYPE;
            Marshal.ThrowExceptionForHR(mediaType.SetGUID(ref subtypeKey, ref subtype));

            Guid interlaceKey = MfGuids.MF_MT_INTERLACE_MODE;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref interlaceKey, MfGuids.MFVideoInterlace_Progressive));

            if (avgBitrate > 0)
            {
                Guid bitrateKey = MfGuids.MF_MT_AVG_BITRATE;
                Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref bitrateKey, avgBitrate));
            }

            Guid frameSizeKey = MfGuids.MF_MT_FRAME_SIZE;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT64(ref frameSizeKey, PackUInt64(width, height)));

            Guid frameRateKey = MfGuids.MF_MT_FRAME_RATE;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT64(ref frameRateKey, PackUInt64(fps, 1)));

            Guid parKey = MfGuids.MF_MT_PIXEL_ASPECT_RATIO;
            Marshal.ThrowExceptionForHR(mediaType.SetUINT64(ref parKey, PackUInt64(1, 1)));

            if (defaultStride is { } stride)
            {
                Guid strideKey = MfGuids.MF_MT_DEFAULT_STRIDE;
                Marshal.ThrowExceptionForHR(mediaType.SetUINT32(ref strideKey, unchecked((uint)stride)));
            }

            // The caller owns the returned pointer; keep the raw pointer alive.
            return typePtr;
        }
        catch
        {
            if (typePtr != nint.Zero)
            {
                Marshal.Release(typePtr);
            }

            throw;
        }
        finally
        {
            if (mediaType is not null)
            {
                Marshal.ReleaseComObject(mediaType);
            }
        }
    }

    private async Task FramePumpLoopAsync(RecordingOptions options, PixelRect region, CancellationToken cancellationToken)
    {
        // The current per-frame path uses GDI (see GrabFrame). A WGC grabber is
        // reserved for a future hardware-accelerated pump but not created here.
        int fps = Math.Clamp(options.Fps, 1, 240);
        long frameIntervalHns = HnsPerSecond / fps;
        int frameIntervalMs = CalculateFrameIntervalMs(fps);
        int consecutiveMissingFrames = 0;
        int missingFrameLimit = Math.Max(30, fps * 3);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool paused;
                lock (_gate)
                {
                    paused = _state == RecordingState.Paused;
                }

                if (paused)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var frameTimer = Stopwatch.StartNew();
                byte[]? frame = GrabFrame(options, region);
                if (frame is not null && _sinkWriter is not null)
                {
                    consecutiveMissingFrames = 0;
                    long timestampHns = ElapsedToMediaFoundationTimestamp(_elapsed.Elapsed);
                    WriteVideoSample(frame, timestampHns, frameIntervalHns);
                    Interlocked.Increment(ref _writtenFrameCount);
                }
                else if (++consecutiveMissingFrames >= missingFrameLimit)
                {
                    throw new InvalidOperationException(
                        "Windows did not provide screen frames for three seconds.");
                }

                RaiseProgress();

                long delayMs = frameIntervalMs - frameTimer.ElapsedMilliseconds;
                if (delayMs > 0)
                {
                    await Task.Delay((int)Math.Min(delayMs, 250), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop/cancel path.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The recording frame pump failed.");
            Volatile.Write(ref _pumpFailure, ex);
            SetState(RecordingState.Failed);
        }
    }

    private byte[]? GrabFrame(RecordingOptions options, PixelRect region)
    {
        // The GDI region grab is exact and used as the reliable per-frame path.
        // WgcFrameGrabber is intentionally not used here because it is a
        // single-frame still-capture helper that creates a capture session per
        // grab; a recording-grade WGC path needs a long-lived frame-pool pump.
        try
        {
            byte[] pixels = GdiScreenCapture.CaptureRegion(region, options.IncludeCursor, out int _);
            return pixels.Length > 0 ? pixels : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Frame grab failed for one frame.");
            return null;
        }
    }

    private async Task AudioPumpLoopAsync(IReadOnlyList<AudioTrack> tracks, CancellationToken cancellationToken)
    {
        const int drainFramesPerCycle = (int)RecordingAudioFormat.SampleRate / 25; // 40 ms chunks.
        const long maxPadFramesPerCorrection = RecordingAudioFormat.SampleRate * 2; // 2 s of silence.

        float[] floats = new float[drainFramesPerCycle * RecordingAudioFormat.Channels];
        byte[] pcm = new byte[drainFramesPerCycle * RecordingAudioFormat.Channels * (RecordingAudioFormat.BitsPerSample / 8)];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);

                bool paused;
                lock (_gate)
                {
                    paused = _state == RecordingState.Paused;
                }

                foreach (AudioTrack track in tracks)
                {
                    bool compensatedThisCycle = false;
                    int frames;
                    while ((frames = track.Source.Drain(floats)) > 0)
                    {
                        if (paused)
                        {
                            // Discard audio captured while paused; the wall clock
                            // and the audio clock both stay frozen, so the track
                            // resumes in sync.
                            continue;
                        }

                        long elapsedHns = ElapsedToMediaFoundationTimestamp(_elapsed.Elapsed);
                        if (track.Clock.IsAhead(elapsedHns, RecordingAudioClock.AheadToleranceHns))
                        {
                            // Device clock is running ahead of the wall clock;
                            // drop the chunk so long-run drift stays bounded.
                            continue;
                        }

                        if (!compensatedThisCycle)
                        {
                            compensatedThisCycle = true;
                            long padFrames = track.Clock.LagCompensationFrames(
                                elapsedHns,
                                RecordingAudioClock.LagToleranceHns,
                                maxPadFramesPerCorrection);
                            if (padFrames > 0)
                            {
                                // The audio timeline fell behind the wall clock
                                // (pump stall, buffer overflow drop); bridge the
                                // gap with silence so A/V sync is preserved.
                                byte[] silence = new byte[checked((int)padFrames * 4)];
                                WriteSampleCore(
                                    track.StreamIndex,
                                    silence,
                                    silence.Length,
                                    track.Clock.Stamp(padFrames),
                                    track.Clock.FramesToHns(padFrames));
                            }
                        }

                        int byteCount = RecordingAudioFormat.FloatToPcm16(floats, frames, pcm);
                        WriteSampleCore(
                            track.StreamIndex,
                            pcm,
                            byteCount,
                            track.Clock.Stamp(frames),
                            track.Clock.FramesToHns(frames));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop/cancel path.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The recording audio pump failed.");
            Volatile.Write(ref _pumpFailure, ex);
            SetState(RecordingState.Failed);
        }
    }

    private void WriteVideoSample(byte[] bgra, long timestampHns, long durationHns)
        => WriteSampleCore(_streamIndex, bgra, bgra.Length, timestampHns, durationHns);

    private void WriteSampleCore(uint streamIndex, byte[] data, int byteCount, long timestampHns, long durationHns)
    {
        if (_sinkWriter is null)
        {
            return;
        }

        int hr = MediaFoundation.MFCreateMemoryBuffer((uint)byteCount, out nint bufferPtr);
        ThrowIfFailedOrNull(hr, bufferPtr, "MFCreateMemoryBuffer");

        nint samplePtr = nint.Zero;
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            buffer = (IMFMediaBuffer)Marshal.GetObjectForIUnknown(bufferPtr);
            hr = buffer.Lock(out nint dest, out _, out _);
            ThrowIfFailedOrNull(hr, dest, "IMFMediaBuffer.Lock");

            try
            {
                Marshal.Copy(data, 0, dest, byteCount);
            }
            finally
            {
                buffer.Unlock();
            }

            Marshal.ThrowExceptionForHR(buffer.SetCurrentLength((uint)byteCount));

            hr = MediaFoundation.MFCreateSample(out samplePtr);
            ThrowIfFailedOrNull(hr, samplePtr, "MFCreateSample");

            sample = (IMFSample)Marshal.GetObjectForIUnknown(samplePtr);
            Marshal.ThrowExceptionForHR(sample.AddBuffer(bufferPtr));
            Marshal.ThrowExceptionForHR(sample.SetSampleTime(timestampHns));
            Marshal.ThrowExceptionForHR(sample.SetSampleDuration(durationHns));

            hr = _sinkWriter.WriteSample(streamIndex, samplePtr);
            Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            if (sample is not null)
            {
                Marshal.ReleaseComObject(sample);
            }

            if (buffer is not null)
            {
                Marshal.ReleaseComObject(buffer);
            }

            if (samplePtr != nint.Zero)
            {
                Marshal.Release(samplePtr);
            }

            Marshal.Release(bufferPtr);
        }
    }

    private void FinalizeEncoder()
    {
        lock (_encoderGate)
        {
            Exception? failure = null;
            try
            {
                if (_sinkWriter is not null)
                {
                    int hr = _sinkWriter.Finalize_();
                    Marshal.ThrowExceptionForHR(hr);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                _logger.LogError(ex, "Error finalizing the sink writer.");
            }
            finally
            {
                ReleaseSinkWriter();
            }

            if (failure is not null)
            {
                throw new InvalidOperationException(
                    "Windows could not finalize the MP4 recording.",
                    failure);
            }
        }
    }

    private static void ThrowIfFailedOrNull(int hr, nint value, string operation)
    {
        Marshal.ThrowExceptionForHR(hr);
        if (value == nint.Zero)
        {
            throw new InvalidOperationException($"{operation} returned no object.");
        }
    }

    internal static void ValidateCompletedRecording(long writtenFrameCount, long fileSizeBytes)
    {
        if (writtenFrameCount <= 0)
        {
            throw new InvalidDataException("The recording contains no video frames.");
        }

        if (fileSizeBytes <= 0)
        {
            throw new InvalidDataException("Windows produced an empty recording file.");
        }
    }

    /// <summary>Free-space floor for starting a recording.</summary>
    internal const long MinimumFreeBytesToStart = 256L * 1024 * 1024;

    /// <summary>
    /// Fails the start when the destination drive is under disk pressure. A
    /// probe result of −1 ("unknown") never blocks a recording — mid-recording
    /// disk-full is still caught by the pump failure path.
    /// </summary>
    internal static void EnsureSufficientDiskSpace(string outputPath, Func<string, long> availableFreeBytesProbe)
    {
        long available = availableFreeBytesProbe(outputPath);
        if (available >= 0 && available < MinimumFreeBytesToStart)
        {
            throw new IOException(
                $"Not enough free disk space to start a recording: {available / (1024 * 1024)} MB available; at least {MinimumFreeBytesToStart / (1024 * 1024)} MB required.");
        }
    }

    /// <summary>Best-effort free-space probe for the drive that holds <paramref name="path"/>; −1 when unknown.</summary>
    internal static long ProbeAvailableFreeBytes(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return -1;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            // A probe failure (odd path, unavailable drive) must not block recording.
            return -1;
        }
    }

    /// <summary>
    /// Waits for every running pump (video and, when present, audio) to
    /// genuinely finish; see <see cref="WaitForPumpAsync"/> for the semantics.
    /// </summary>
    private async Task<bool> WaitForPumpsAsync(string operation, CancellationToken cancellationToken)
    {
        bool videoStopped = await WaitForPumpAsync(_pumpTask, operation, cancellationToken).ConfigureAwait(false);
        bool audioStopped = await WaitForPumpAsync(_audioPumpTask, operation, cancellationToken).ConfigureAwait(false);
        return videoStopped && audioStopped;
    }

    /// <summary>
    /// Waits for a pump to genuinely finish. Returns <c>true</c> when the
    /// pump has completed (successfully, faulted, or cancelled) and the encoder
    /// is safe to touch; <c>false</c> when the pump is still alive after the
    /// caller's token fired plus a bounded grace period (C-2: releasing the sink
    /// writer while the pump can still write to it is a use-after-free).
    /// </summary>
    private async Task<bool> WaitForPumpAsync(Task? pump, string operation, CancellationToken cancellationToken)
    {
        if (pump is null)
        {
            return true;
        }

        try
        {
            await pump.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (pump.IsCompleted)
        {
            // The pump itself ended as cancelled — that is true completion.
            return true;
        }
        catch (OperationCanceledException)
        {
            // Only the WAIT was cancelled; the pump still owns the encoder.
            // Give it a bounded grace period to observe the session token.
            Task finished = await Task
                .WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None))
                .ConfigureAwait(false);
            if (finished != pump)
            {
                _logger.LogError(
                    "Frame pump did not stop within the grace period during {Operation}.", operation);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Frame pump ended with an error during {Operation}.", operation);
            return true;
        }
    }

    private void AbandonEncoder()
    {
        // Release without finalizing so no valid moov atom is written.
        ReleaseSinkWriter();
    }

    private void ReleaseSinkWriter()
    {
        // The pump failure path and Stop/Cancel/Dispose can race each other
        // here; the gate makes release idempotent instead of double-freeing.
        lock (_encoderGate)
        {
            if (_sinkWriter is not null)
            {
                Marshal.ReleaseComObject(_sinkWriter);
            }

            _sinkWriter = null;
            if (_sinkWriterPtr != nint.Zero)
            {
                Marshal.Release(_sinkWriterPtr);
                _sinkWriterPtr = nint.Zero;
            }

            if (_mfStarted)
            {
                try
                {
                    MediaFoundation.MFShutdown();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MFShutdown failed.");
                }

                _mfStarted = false;
            }
        }
    }

    private void CleanupSession()
    {
        IReadOnlyList<AudioTrack>? audioTracks;
        lock (_gate)
        {
            _sessionCts?.Dispose();
            _sessionCts = null;
            _pumpTask = null;
            _audioPumpTask = null;
            audioTracks = _audioTracks;
            _audioTracks = null;
        }

        // Release audio devices immediately on every stop/cancel/failure path.
        if (audioTracks is not null)
        {
            foreach (AudioTrack track in audioTracks)
            {
                track.Source.Dispose();
            }
        }

        _grabber?.Dispose();
        _grabber = null;
    }

    private void SetState(RecordingState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        RaiseProgress();
    }

    private void RaiseProgress()
    {
        RecordingState state;
        lock (_gate)
        {
            state = _state;
        }

        RaiseProgressRaw(state, _elapsed.ElapsedMilliseconds, countdownRemaining: null);
    }

    private void RaiseProgressRaw(RecordingState state, long elapsedMs, int? countdownRemaining)
    {
        try
        {
            ProgressChanged?.Invoke(this, new RecordingProgress(state, elapsedMs, countdownRemaining));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "A ProgressChanged handler threw.");
        }
    }

    private static uint BitrateFor(RecordingQuality quality, uint width, uint height, uint fps)
    {
        // Rough bits-per-pixel heuristic scaled by quality; clamped to sane bounds.
        double bpp = quality switch
        {
            RecordingQuality.Low => 0.05,
            RecordingQuality.High => 0.15,
            _ => 0.09,
        };

        double bits = width * (double)height * fps * bpp;
        return (uint)Math.Clamp(bits, 1_000_000, 60_000_000);
    }

    internal static int CalculateRgb32Stride(uint width)
    {
        checked
        {
            return (int)(width * 4U);
        }
    }

    internal static long ElapsedToMediaFoundationTimestamp(TimeSpan elapsed)
        => Math.Max(0, elapsed.Ticks);

    internal static int CalculateFrameIntervalMs(int fps)
        => Math.Max(1, (int)Math.Round(1000.0 / Math.Clamp(fps, 1, 240)));

    private static ulong PackUInt64(uint high, uint low) => ((ulong)high << 32) | low;

    private long TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            _outputFileSize = info.Exists ? info.Length : 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the output file size.");
        }

        return _outputFileSize;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not delete the partial recording file.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        RecordingOptions? options;
        RecordingState state;
        lock (_gate)
        {
            options = _options;
            state = _state;
        }

        try
        {
            _sessionCts?.Cancel();
            _pumpTask?.Wait(TimeSpan.FromSeconds(2));
            _audioPumpTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping the recording pumps on dispose.");
        }

        CleanupSession();
        AbandonEncoder();

        // A session that never finalized (process exit or shutdown mid-write)
        // must not leave a broken MP4 behind that looks like a finished
        // recording; a completed one is never touched.
        if (options is not null && state is not RecordingState.Completed)
        {
            TryDeleteFile(options.OutputPath);
        }
    }

    /// <summary>One encoded audio track: its WASAPI source, drift clock and sink-writer stream.</summary>
    private sealed record AudioTrack(RecordingAudioSource Source, RecordingAudioClock Clock, uint StreamIndex);
}

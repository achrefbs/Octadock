using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.Audio;

/// <summary>
/// Records the default microphone and yields 16 kHz mono float PCM (the STT
/// engines' canonical input). It prefers the normal console/multimedia endpoint
/// before falling back to the communications endpoint, owns the device format
/// conversion (WASAPI capture format → mono → 16 kHz), a bounded five-minute
/// buffer for toggle-mode dictation, and the Windows privacy toggle that blocks
/// desktop apps from the microphone. While recording, a background pump drains
/// the resampler every ~100 ms into the utterance accumulator and raises
/// <see cref="SamplesAvailable"/> so streaming decoders can run live.
/// Disposable because it holds a live WASAPI capture client.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AudioCaptureService : IDictationAudioSource, IDisposable
{
    private const int HardSampleCap = 5 * 60 * 16_000; // 5 minutes at 16 kHz mono.

    private static readonly Role[] CaptureRolePreference =
    [
        Role.Console,
        Role.Multimedia,
        Role.Communications,
    ];

    private readonly ILogger<AudioCaptureService> _logger;
    private readonly object _gate = new();

    private WasapiCapture? _capture;
    private MMDevice? _captureDevice;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampled;
    private List<float>? _accumulated;
    private Task? _pumpTask;
    private CancellationTokenSource? _pumpCts;
    private volatile bool _recording;

    /// <summary>Creates the microphone capture service.</summary>
    public AudioCaptureService(ILogger<AudioCaptureService> logger) => _logger = logger;

    /// <inheritdoc />
    public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;

    /// <summary>
    /// Raised when capture stops abnormally (device unplugged, driver failure,
    /// an exclusive-mode takeover). The partial utterance stays available via
    /// <see cref="Stop"/> — this event exists so the owner can tell the user
    /// the microphone was lost instead of silently transcribing a truncated
    /// utterance.
    /// </summary>
    public event EventHandler<AudioCaptureInterruptedEventArgs>? CaptureInterrupted;

    /// <summary>Peak level 0..1 of the last capture callback, for a level meter.</summary>
    public float LastPeak { get; private set; }

    /// <summary>True when the most recent capture ended because the device was lost.</summary>
    public bool WasInterrupted { get; private set; }

    /// <inheritdoc />
    public string? PreferredDeviceId { get; set; }

    /// <summary>
    /// Starts capturing the default microphone. Throws
    /// <see cref="MicrophoneAccessDeniedException"/> when the OS privacy setting
    /// blocks access, so the caller can deep-link ms-settings:privacy-microphone
    /// instead of failing silently.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_recording)
            {
                return;
            }

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                (MMDevice device, Role role) = ResolveCaptureDevice(enumerator);

                WasInterrupted = false;
                _captureDevice = device;
                _capture = new WasapiCapture(device);
                _buffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMinutes(5), // Toggle-mode ceiling.
                    // Critical: with ReadFully (the NAudio default) the resampler's
                    // Read never returns 0 once the buffer drains — it pads with
                    // silence forever — so drain loops would spin endlessly. Let
                    // Read return short/zero at end-of-buffer instead.
                    ReadFully = false,
                };

                // Device format (often 48 kHz stereo) → 16 kHz mono float.
                ISampleProvider samples = _buffer.ToSampleProvider();
                if (samples.WaveFormat.Channels > 1)
                {
                    samples = new StereoToMonoSampleProvider(samples);
                }

                _resampled = new WdlResamplingSampleProvider(samples, 16_000);
                _accumulated = new List<float>(16_000 * 30);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _recording = true;

                // The pump owns all resampler reads while recording; Stop() joins
                // it before the final drain so the chain stays single-reader.
                _pumpCts = new CancellationTokenSource();
                CancellationToken pumpToken = _pumpCts.Token;
                _pumpTask = Task.Run(() => PumpLoopAsync(pumpToken), CancellationToken.None);

                WaveFormat format = _capture.WaveFormat;
                _logger.LogInformation(
                    "Dictation microphone: {DeviceName} ({Role}, {SampleRate} Hz, {Channels} channel(s), {Bits} bit, {Encoding}).",
                    device.FriendlyName,
                    role,
                    format.SampleRate,
                    format.Channels,
                    format.BitsPerSample,
                    format.Encoding);
            }
            catch (global::System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070005)
            {
                // E_ACCESSDENIED — the Windows microphone privacy toggle.
                _recording = false;
                TearDown();
                throw new MicrophoneAccessDeniedException(ex);
            }
            catch
            {
                _recording = false;
                TearDown();
                throw;
            }
        }
    }

    /// <summary>
    /// Resolves the device to record from: the user's picked endpoint when
    /// <see cref="PreferredDeviceId"/> is set (failing honestly when it is
    /// gone), otherwise the system default by role preference.
    /// </summary>
    private (MMDevice Device, Role Role) ResolveCaptureDevice(MMDeviceEnumerator enumerator)
    {
        if (!string.IsNullOrWhiteSpace(PreferredDeviceId))
        {
            try
            {
                MMDevice selected = enumerator.GetDevice(PreferredDeviceId);
                if (selected.State == DeviceState.Active)
                {
                    return (selected, Role.Console);
                }

                selected.Dispose();
            }
            catch (global::System.Runtime.InteropServices.COMException ex)
            {
                throw SelectedDeviceUnavailable(ex);
            }

            throw SelectedDeviceUnavailable(null);
        }

        return GetPreferredCaptureDevice(enumerator);
    }

    private static MicrophoneDeviceUnavailableException SelectedDeviceUnavailable(Exception? inner)
        => new(
            "The selected microphone is no longer connected. Reconnect it, or choose another " +
            "microphone in Settings → Voice → Dictation.",
            inner);

    /// <summary>Lists the active capture endpoints for the dictation microphone picker.</summary>
    public static IReadOnlyList<MicrophoneDeviceInfo> ListCaptureDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = new List<MicrophoneDeviceInfo>();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new MicrophoneDeviceInfo(device.ID, device.FriendlyName));
                }
            }

            return devices;
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // Enumeration failure must not break Settings; the picker then
            // offers only the system default.
            return [];
        }
    }

    private static (MMDevice Device, Role Role) GetPreferredCaptureDevice(MMDeviceEnumerator enumerator)
    {
        Exception? lastFailure = null;
        foreach (Role role in CaptureRolePreference)
        {
            try
            {
                MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (device.State == DeviceState.Active)
                {
                    return (device, role);
                }

                device.Dispose();
            }
            catch (global::System.Runtime.InteropServices.COMException ex)
                when ((uint)ex.HResult != 0x80070005)
            {
                lastFailure = ex;
            }
            catch (InvalidOperationException ex)
            {
                lastFailure = ex;
            }
        }

        throw new MicrophoneDeviceUnavailableException(
            "No microphone is available. Connect a microphone and check that Windows privacy " +
            "settings allow desktop apps to use it, then dictate again.",
            lastFailure);
    }

    /// <summary>Stops capture and returns everything recorded as one utterance buffer.</summary>
    public AudioBuffer Stop()
    {
        Task? pumpTask;
        CancellationTokenSource? pumpCts;
        lock (_gate)
        {
            if (!_recording && _capture is null && _resampled is null)
            {
                return new AudioBuffer([]);
            }

            bool wasRecording = _recording;
            _recording = false;
            if (wasRecording)
            {
                _capture?.StopRecording();
            }

            pumpTask = _pumpTask;
            pumpCts = _pumpCts;
            _pumpTask = null;
            _pumpCts = null;
        }

        // Join the pump OUTSIDE the gate (it takes the gate for each read), then
        // run the final tail drain ourselves.
        pumpCts?.Cancel();
        try
        {
            pumpTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Pump cancellation; non-fatal.
        }

        pumpCts?.Dispose();
        DrainAvailable(finalDrain: true);

        lock (_gate)
        {
            float[] samples = _accumulated?.ToArray() ?? [];
            _accumulated = null;
            TearDown();
            return new AudioBuffer(samples);
        }
    }

    private async Task PumpLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                DrainAvailable(finalDrain: false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop; Stop() runs the final drain.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dictation audio pump stopped unexpectedly.");
        }
    }

    /// <summary>
    /// Reads whatever the resampler has ready, appends it to the utterance
    /// accumulator (bounded by the 5-minute cap), and raises
    /// <see cref="SamplesAvailable"/> outside the lock.
    /// </summary>
    private void DrainAvailable(bool finalDrain)
    {
        var chunk = new float[16_000];
        while (true)
        {
            int read;
            bool capReached;
            bool sourceEmpty;
            lock (_gate)
            {
                ISampleProvider? resampled = _resampled;
                List<float>? accumulated = _accumulated;
                if (resampled is null || accumulated is null)
                {
                    return;
                }

                read = resampled.Read(chunk, 0, chunk.Length);
                capReached = accumulated.Count >= HardSampleCap;
                if (read > 0 && !capReached)
                {
                    int take = Math.Min(read, HardSampleCap - accumulated.Count);
                    for (int i = 0; i < take; i++)
                    {
                        accumulated.Add(chunk[i]);
                    }

                    if (accumulated.Count >= HardSampleCap)
                    {
                        _logger.LogWarning("Dictation capture hit the 5-minute hard cap; truncating.");
                    }
                }

                sourceEmpty = _buffer is null || _buffer.BufferedBytes == 0;
            }

            if (read > 0)
            {
                SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(chunk.AsMemory(0, read)));
            }

            if (read == 0 || capReached)
            {
                return;
            }

            // While recording, stop once we've caught up so the pump sleeps;
            // in the final drain keep going until the source and resampler tail
            // are both empty.
            if (read < chunk.Length && (!finalDrain || sourceEmpty))
            {
                return;
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        LastPeak = ComputePeak(e.Buffer, e.BytesRecorded, _capture?.WaveFormat);
    }

    /// <summary>
    /// Format-aware peak: WASAPI shared-mode capture delivers 32-bit IEEE float
    /// (the old 16-bit-only math read noise there); exclusive/legacy paths may
    /// still be 16-bit PCM.
    /// </summary>
    private static float ComputePeak(byte[] buffer, int bytes, WaveFormat? format)
    {
        float peak = 0;

        // WASAPI shared mode reports Extensible with a float SubFormat; 32-bit
        // integer capture is rare enough that BitsPerSample==32 is a safe meter
        // heuristic for "interpret as float".
        if (format is { BitsPerSample: 32, Encoding: WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible })
        {
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                float sample = BitConverter.ToSingle(buffer, i);
                peak = Math.Max(peak, Math.Abs(sample));
            }

            return Math.Min(peak, 1f);
        }

        for (int i = 0; i + 1 < bytes; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            peak = Math.Max(peak, Math.Abs(s / 32768f));
        }

        return peak;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        => HandleCaptureStopped(e.Exception);

    /// <summary>
    /// Records an abnormal capture stop (device unplugged, driver failure).
    /// A null exception is a normal stop. Internal so the state transition is
    /// deterministic without a physical device to unplug mid-test.
    /// </summary>
    internal void HandleCaptureStopped(Exception? error)
    {
        if (error is null)
        {
            return;
        }

        _logger.LogWarning(error, "Audio capture stopped unexpectedly (device change?).");
        _recording = false;
        WasInterrupted = true;
        CaptureInterrupted?.Invoke(this, new AudioCaptureInterruptedEventArgs(error));
    }

    private void TearDown()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _captureDevice?.Dispose();
        _captureDevice = null;
        _buffer = null;
        _resampled = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancellationTokenSource? pumpCts;
        lock (_gate)
        {
            _recording = false;
            pumpCts = _pumpCts;
            _pumpCts = null;
            _pumpTask = null;
            _accumulated = null;
            TearDown();
        }

        pumpCts?.Cancel();
        pumpCts?.Dispose();
    }
}

/// <summary>Thrown when Windows privacy settings deny microphone access.</summary>
public sealed class MicrophoneAccessDeniedException(Exception inner)
    : InvalidOperationException(
        "Microphone access is blocked by Windows privacy settings (ms-settings:privacy-microphone).", inner);

/// <summary>
/// Thrown when no usable capture endpoint exists, or the user's selected
/// microphone is no longer connected. The message carries the recovery path.
/// </summary>
public sealed class MicrophoneDeviceUnavailableException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

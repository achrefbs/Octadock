using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.Audio;

/// <summary>
/// Records the default microphone and yields 16 kHz mono float PCM (Whisper's
/// canonical input). It prefers the normal console/multimedia endpoint before
/// falling back to the communications endpoint, owns the device format conversion
/// (WASAPI capture format → mono → 16 kHz), a bounded five-minute buffer for
/// toggle-mode dictation, and the Windows privacy toggle that blocks desktop apps
/// from the microphone. Disposable because it holds a live WASAPI capture client.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AudioCaptureService : IDisposable
{
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
    private volatile bool _recording;

    /// <summary>Creates the microphone capture service.</summary>
    public AudioCaptureService(ILogger<AudioCaptureService> logger) => _logger = logger;

    /// <summary>Peak level 0..1 of the last read, for a HUD level meter.</summary>
    public float LastPeak { get; private set; }

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
                (MMDevice device, Role role) = GetPreferredCaptureDevice(enumerator);

                _captureDevice = device;
                _capture = new WasapiCapture(device);
                _buffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMinutes(5), // Toggle-mode ceiling.
                    // Critical: with ReadFully (the NAudio default) the resampler's
                    // Read never returns 0 once the buffer drains — it pads with
                    // silence forever — so the Stop() drain loop below would spin
                    // endlessly. Let Read return short/zero at end-of-buffer instead.
                    ReadFully = false,
                };

                // Device format (often 48 kHz stereo) → 16 kHz mono float.
                ISampleProvider samples = _buffer.ToSampleProvider();
                if (samples.WaveFormat.Channels > 1)
                {
                    samples = new StereoToMonoSampleProvider(samples);
                }

                _resampled = new WdlResamplingSampleProvider(samples, 16_000);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _recording = true;

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

        throw new InvalidOperationException("No active microphone capture device is available.", lastFailure);
    }

    /// <summary>Stops capture and drains everything recorded into one utterance buffer.</summary>
    public AudioBuffer Stop()
    {
        lock (_gate)
        {
            if (!_recording && _capture is null && _resampled is null)
            {
                return new AudioBuffer([]);
            }

            bool wasRecording = _recording;
            _recording = false;
            var drained = new List<float>(16_000 * 30);

            try
            {
                if (wasRecording)
                {
                    _capture?.StopRecording();
                }

                // Drain the resampler into one utterance buffer. With ReadFully=false
                // the resampler's Read returns short/zero once its own tail state is
                // flushed, so this terminates. The extra guards are belt-and-braces so
                // a stuck source can never hang the UI thread: a hard 5-minute sample
                // cap, and an early break when the source buffer is empty and the last
                // Read came up short (the tail has drained).
                const int hardSampleCap = 5 * 60 * 16_000; // 5 minutes at 16 kHz mono.
                BufferedWaveProvider? source = _buffer;
                ISampleProvider? resampled = _resampled;
                if (resampled is not null)
                {
                    var chunk = new float[16_000];
                    int read;
                    while ((read = resampled.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        drained.AddRange(chunk.AsSpan(0, read).ToArray());

                        if (drained.Count >= hardSampleCap)
                        {
                            _logger.LogWarning("Dictation drain hit the 5-minute hard cap; truncating.");
                            break;
                        }

                        if (read < chunk.Length && (source is null || source.BufferedBytes == 0))
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                TearDown();
            }

            return new AudioBuffer(drained.ToArray());
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Cheap peak for a level meter (a 16-bit assumption is fine for a meter).
        float peak = 0;
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            short s = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            peak = Math.Max(peak, Math.Abs(s / 32768f));
        }

        LastPeak = peak;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // A device unplugged mid-dictation surfaces here; a null exception is a normal stop.
        if (e.Exception is not null)
        {
            _logger.LogWarning(e.Exception, "Audio capture stopped unexpectedly (device change?).");
            _recording = false;
        }
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
        lock (_gate)
        {
            _recording = false;
            TearDown();
        }
    }
}

/// <summary>Thrown when Windows privacy settings deny microphone access.</summary>
public sealed class MicrophoneAccessDeniedException(Exception inner)
    : InvalidOperationException(
        "Microphone access is blocked by Windows privacy settings (ms-settings:privacy-microphone).", inner);

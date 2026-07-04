// Example implementation for the STT roadmap.
// Target location: src/Octadock.Platform.Windows/Audio/
// NuGet: NAudio (2.2.x) — WASAPI capture + resampling.
//
// Records the default communications microphone and yields 16 kHz mono float
// PCM (Whisper's canonical input). Handles: device format conversion, device
// unplugged mid-session, and the Windows privacy toggle that blocks desktop
// apps from the microphone.

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.Audio;

[SupportedOSPlatform("windows")]
public sealed class AudioCaptureService : IDisposable
{
    private readonly ILogger<AudioCaptureService> _logger;
    private readonly object _gate = new();

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampled;
    private volatile bool _recording;

    public AudioCaptureService(ILogger<AudioCaptureService> logger) => _logger = logger;

    /// <summary>Peak level 0..1 of the last read, for the HUD waveform.</summary>
    public float LastPeak { get; private set; }

    /// <summary>
    /// Starts capturing. Throws <see cref="MicrophoneAccessDeniedException"/>
    /// when the OS privacy setting blocks access, so the caller can deep-link
    /// ms-settings:privacy-microphone instead of failing silently.
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
                MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

                _capture = new WasapiCapture(device);
                _buffer = new BufferedWaveProvider(_capture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMinutes(5), // Toggle-mode ceiling.
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
            }
            catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x80070005)
            {
                // E_ACCESSDENIED — the Windows microphone privacy toggle.
                throw new MicrophoneAccessDeniedException(ex);
            }
        }
    }

    /// <summary>Stops capture and drains everything recorded into one utterance buffer.</summary>
    public AudioBuffer Stop()
    {
        lock (_gate)
        {
            if (!_recording)
            {
                return new AudioBuffer([]);
            }

            _recording = false;
            _capture?.StopRecording();

            // Drain the resampler until the buffered provider is empty.
            var drained = new List<float>(16_000 * 30);
            var chunk = new float[16_000];
            int read;
            while ((read = _resampled!.Read(chunk, 0, chunk.Length)) > 0)
            {
                drained.AddRange(chunk.AsSpan(0, read).ToArray());
                if (_buffer!.BufferedBytes == 0)
                {
                    break;
                }
            }

            TearDown();
            return new AudioBuffer(drained.ToArray());
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Cheap peak for the waveform HUD (16-bit assumption is fine for a meter).
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
        // Device unplugged mid-dictation surfaces here; a null exception is a normal stop.
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

        _buffer = null;
        _resampled = null;
    }

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

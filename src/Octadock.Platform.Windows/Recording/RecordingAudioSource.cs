using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Octadock.Platform.Windows.Audio;

namespace Octadock.Platform.Windows.Recording;

/// <summary>
/// One explicit, opt-in WASAPI audio source for a screen recording — the default
/// microphone or the default output device's loopback (system audio). Samples
/// are converted to the canonical recording format
/// (<see cref="RecordingAudioFormat.SampleRate"/> Hz stereo float) and held in a
/// small bounded buffer; the engine's audio pump drains them onto a Media
/// Foundation audio stream. A source is only created when the user explicitly
/// asked for that track, and <see cref="Dispose"/> releases the audio device
/// immediately on stop/cancel/failure. Follows the same WASAPI pattern as
/// dictation's AudioCaptureService, with its own canonical format and lifecycle
/// so dictation is untouched.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class RecordingAudioSource : IDisposable
{
    internal enum SourceKind
    {
        Microphone,
        SystemAudio,
    }

    // Same endpoint preference order dictation uses for the microphone.
    private static readonly Role[] CaptureRolePreference =
    [
        Role.Console,
        Role.Multimedia,
        Role.Communications,
    ];

    private readonly ILogger _logger;
    private readonly SourceKind _kind;
    private readonly object _gate = new();

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _canonical;
    private Exception? _deviceFailure;
    private bool _disposed;

    private RecordingAudioSource(SourceKind kind, ILogger logger)
    {
        _kind = kind;
        _logger = logger;
    }

    /// <summary>Starts capturing the default microphone. Throws <see cref="MicrophoneAccessDeniedException"/> when the Windows privacy toggle blocks desktop apps.</summary>
    internal static RecordingAudioSource StartMicrophone(ILogger logger)
        => Start(SourceKind.Microphone, logger);

    /// <summary>Starts loopback capture of the default audio output device (the sound the machine plays).</summary>
    internal static RecordingAudioSource StartSystemAudio(ILogger logger)
        => Start(SourceKind.SystemAudio, logger);

    private static RecordingAudioSource Start(SourceKind kind, ILogger logger)
    {
        var source = new RecordingAudioSource(kind, logger);
        try
        {
            source.StartCapture();
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private void StartCapture()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = _kind == SourceKind.Microphone
                ? GetPreferredCaptureDevice(enumerator)
                : GetActiveRenderDevice(enumerator);

            _capture = _kind == SourceKind.Microphone
                ? new WasapiCapture(_device)
                : new WasapiLoopbackCapture(_device);

            // Bounded queue: when the encoder pump falls behind, old audio is
            // dropped (the drift clock re-syncs) instead of memory growing.
            _buffer = new BufferedWaveProvider(_capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(10),
                // With ReadFully the resampler pads with silence at end-of-buffer
                // and drain loops never see "caught up"; short reads are required.
                ReadFully = false,
            };

            _canonical = ToCanonical(_buffer);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();

            WaveFormat format = _capture.WaveFormat;
            _logger.LogInformation(
                "Recording audio ({Kind}): {DeviceName} ({SampleRate} Hz, {Channels} channel(s), {Bits} bit, {Encoding}).",
                _kind,
                _device.FriendlyName,
                format.SampleRate,
                format.Channels,
                format.BitsPerSample,
                format.Encoding);
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070005 && _kind == SourceKind.Microphone)
        {
            // E_ACCESSDENIED — the Windows microphone privacy toggle.
            throw new MicrophoneAccessDeniedException(ex);
        }
    }

    private static MMDevice GetPreferredCaptureDevice(MMDeviceEnumerator enumerator)
    {
        Exception? lastFailure = null;
        foreach (Role role in CaptureRolePreference)
        {
            try
            {
                MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (device.State == DeviceState.Active)
                {
                    return device;
                }

                device.Dispose();
            }
            catch (COMException ex) when ((uint)ex.HResult != 0x80070005)
            {
                lastFailure = ex;
            }
            catch (InvalidOperationException ex)
            {
                lastFailure = ex;
            }
        }

        throw new InvalidOperationException(
            "No active microphone is available. Connect a microphone or turn off the microphone audio track.",
            lastFailure);
    }

    private static MMDevice GetActiveRenderDevice(MMDeviceEnumerator enumerator)
    {
        MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (device.State == DeviceState.Active)
        {
            return device;
        }

        string name;
        try
        {
            name = device.FriendlyName;
        }
        catch (COMException)
        {
            name = "default output device";
        }

        device.Dispose();
        throw new InvalidOperationException(
            $"The default audio output device ({name}) is not active, so system audio cannot be recorded.");
    }

    /// <summary>
    /// Converts the device capture format to the canonical recording format:
    /// any channel layout → stereo, any sample rate → 48 kHz.
    /// </summary>
    private static ISampleProvider ToCanonical(BufferedWaveProvider buffer)
    {
        ISampleProvider samples = buffer.ToSampleProvider();
        samples = samples.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(samples),
            (int)RecordingAudioFormat.Channels => samples,
            _ => DownmixToStereo(samples),
        };

        if (samples.WaveFormat.SampleRate != (int)RecordingAudioFormat.SampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, (int)RecordingAudioFormat.SampleRate);
        }

        return samples;
    }

    /// <summary>
    /// Folds a surround layout down to stereo: front-left/right keep their side;
    /// center, LFE and rear/side channels fold into both sides.
    /// </summary>
    private static ISampleProvider DownmixToStereo(ISampleProvider input)
    {
        int channels = input.WaveFormat.Channels;
        var multiplex = new MultiplexingSampleProvider([input], (int)RecordingAudioFormat.Channels);
        for (int channel = 0; channel < channels; channel++)
        {
            if (channel == 0)
            {
                multiplex.ConnectInputToOutput(0, 0);
            }
            else if (channel == 1)
            {
                multiplex.ConnectInputToOutput(1, 1);
            }
            else
            {
                multiplex.ConnectInputToOutput(channel, 0);
                multiplex.ConnectInputToOutput(channel, 1);
            }
        }

        return multiplex;
    }

    /// <summary>
    /// Drains up to <c>destination.Length / 2</c> stereo frames into
    /// <paramref name="interleavedDestination"/> and returns the frame count
    /// (0 when the source has caught up). Throws when the audio device has
    /// failed (for example unplugged mid-recording) so the pump can fail the
    /// session truthfully instead of encoding silence.
    /// </summary>
    internal int Drain(float[] interleavedDestination)
    {
        lock (_gate)
        {
            if (_deviceFailure is not null)
            {
                string device = _kind == SourceKind.Microphone ? "microphone" : "audio output device";
                throw new InvalidOperationException(
                    $"The {device} stopped providing audio during the recording.",
                    _deviceFailure);
            }

            if (_canonical is null)
            {
                return 0;
            }

            int read = _canonical.Read(interleavedDestination, 0, interleavedDestination.Length);
            return read / (int)RecordingAudioFormat.Channels;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _buffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // A null exception is a normal stop (Stop/Dispose); anything else means
        // the device went away mid-recording.
        if (e.Exception is not null)
        {
            _logger.LogWarning(e.Exception, "Recording audio ({Kind}) stopped unexpectedly (device change?).", _kind);
            lock (_gate)
            {
                _deviceFailure ??= e.Exception;
            }
        }
    }

    /// <summary>Releases the WASAPI device immediately.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                try
                {
                    _capture.StopRecording();
                }
                catch (COMException ex)
                {
                    _logger.LogDebug(ex, "Stopping recording audio ({Kind}) failed.", _kind);
                }

                _capture.Dispose();
                _capture = null;
            }

            _device?.Dispose();
            _device = null;
            _buffer = null;
            _canonical = null;
        }
    }
}

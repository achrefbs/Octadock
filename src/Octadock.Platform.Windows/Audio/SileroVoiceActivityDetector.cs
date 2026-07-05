using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using SherpaOnnx;

namespace Octadock.Platform.Windows.Audio;

/// <summary>
/// Silero VAD v4 over the sherpa-onnx runtime (the same native library the
/// Parakeet engine uses). The ~630 KB model ships as an embedded resource and
/// is extracted to the models directory on first use, so VAD needs no
/// download. All native access is serialized behind one lock because the
/// audio pump feeds samples while the decode loop pops segments.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SileroVoiceActivityDetector : IVoiceActivityDetector, IDisposable
{
    private const string ResourceName = "Octadock.Platform.Windows.Assets.silero_vad.onnx";
    private const long ModelBytes = 643_854;
    private const int SampleRate = 16_000;

    private readonly IStoragePaths _paths;
    private readonly ILogger<SileroVoiceActivityDetector> _logger;
    private readonly object _gate = new();
    private VoiceActivityDetector? _vad;
    private bool? _available;
    private bool _speechActive;

    /// <summary>Creates the detector (native load is deferred to first use).</summary>
    public SileroVoiceActivityDetector(IStoragePaths paths, ILogger<SileroVoiceActivityDetector> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                return EnsureDetector() is not null;
            }
        }
    }

    /// <inheritdoc />
    public bool IsSpeechActive
    {
        get
        {
            lock (_gate)
            {
                return _speechActive;
            }
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_gate)
        {
            _speechActive = false;
            EnsureDetector()?.Reset();
        }
    }

    /// <inheritdoc />
    public void Accept(ReadOnlyMemory<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            VoiceActivityDetector? vad = EnsureDetector();
            if (vad is null)
            {
                return;
            }

            vad.AcceptWaveform(samples.ToArray());
            _speechActive = vad.IsSpeechDetected();
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_gate)
        {
            _speechActive = false;
            EnsureDetector()?.Flush();
        }
    }

    /// <inheritdoc />
    public bool TryPopSegment(out VadSpeechSegment segment)
    {
        lock (_gate)
        {
            VoiceActivityDetector? vad = EnsureDetector();
            if (vad is null || vad.IsEmpty())
            {
                segment = default;
                return false;
            }

            SpeechSegment native = vad.Front();
            vad.Pop();
            segment = new VadSpeechSegment(native.Start, native.Samples);
            return true;
        }
    }

    private VoiceActivityDetector? EnsureDetector()
    {
        if (_available is false)
        {
            return null;
        }

        if (_vad is not null)
        {
            return _vad;
        }

        try
        {
            string modelPath = ExtractModel();
            var config = new VadModelConfig();
            config.SileroVad.Model = modelPath;
            config.SileroVad.Threshold = 0.5f;
            config.SileroVad.MinSilenceDuration = 0.6f;
            config.SileroVad.MinSpeechDuration = 0.25f;
            config.SileroVad.WindowSize = 512;
            // Force-close monologue segments so the finalize tail (and thus
            // stop-to-text latency) stays bounded no matter how long the user talks.
            config.SileroVad.MaxSpeechDuration = 10f;
            config.SampleRate = SampleRate;
            config.NumThreads = 1;
            config.Debug = 0;

            _vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 120);
            _available = true;
            _logger.LogInformation("Silero VAD ready ({Model}).", modelPath);
        }
        catch (Exception ex) when (
            ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException
                or TypeInitializationException or IOException or InvalidOperationException)
        {
            _available = false;
            _logger.LogWarning(ex, "Silero VAD unavailable; dictation runs without live partials.");
        }

        return _vad;
    }

    /// <summary>Extracts the embedded model next to the speech models (idempotent).</summary>
    private string ExtractModel()
    {
        string path = Path.Combine(_paths.RootDirectory, "models", "silero_vad.onnx");
        var info = new FileInfo(path);
        if (info.Exists && info.Length == ModelBytes)
        {
            return path;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded VAD model '{ResourceName}' is missing.");

        string temp = path + ".partial";
        using (FileStream destination = File.Create(temp))
        {
            resource.CopyTo(destination);
        }

        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _vad?.Dispose();
            _vad = null;
        }
    }
}

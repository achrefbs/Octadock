using Octadock.Core.Abstractions;

namespace Octadock.Core.Speech;

/// <summary>Stable and volatile halves of the live transcript for the pill.</summary>
public sealed class PartialTranscriptEventArgs(string stable, string volatilePart) : EventArgs
{
    /// <summary>Text of VAD-closed segments; decoded once, never changes.</summary>
    public string Stable { get; } = stable;

    /// <summary>Best-effort text of the still-open segment; replaced on every re-decode.</summary>
    public string Volatile { get; } = volatilePart;
}

/// <summary>
/// Simulated streaming over an offline recognizer: audio is fed in as it is
/// captured, a VAD splits it into segments, and each *closed* segment is
/// decoded exactly once (its text is then stable forever) while the still-open
/// tail is re-decoded on every <see cref="TickAsync"/> to give a live,
/// volatile preview. <see cref="FinalizeAsync"/> then only has to decode the
/// short open tail — that is what makes stop-to-text feel instant regardless
/// of how long the utterance ran.
/// Deliberately passive (no timers, no threads): the owner pumps
/// <see cref="Accept"/> from the audio source and calls <see cref="TickAsync"/>
/// on its own cadence, which keeps every path unit-testable.
/// </summary>
public sealed class SimulatedStreamingSession : IDisposable
{
    private const int SampleRate = 16_000;
    private const int MinTailSamplesToDecode = SampleRate * 3 / 10; // 0.3 s

    private readonly IStreamingSpeechToTextProvider _provider;
    private readonly IVoiceActivityDetector _vad;
    private readonly SttOptions _options;
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private readonly object _lifetimeGate = new();
    private readonly List<float> _samples = [];

    private int _stableEndSample;
    private bool _tailHasSpeech;
    private bool _hadSpeech;
    private int _lastSpeechSample;
    private string _stableText = string.Empty;
    private string _volatileText = string.Empty;
    private string _lastRaisedStable = string.Empty;
    private string _lastRaisedVolatile = string.Empty;
    private int _activeOperations;
    private bool _disposed;
    private bool _decodeGateDisposed;

    /// <summary>Creates a session for one utterance; the VAD must already be Reset().</summary>
    public SimulatedStreamingSession(
        IStreamingSpeechToTextProvider provider,
        IVoiceActivityDetector vad,
        SttOptions options)
    {
        _provider = provider;
        _vad = vad;
        _options = options;
    }

    /// <summary>Raised (on a decode thread) whenever the stable or volatile text changes.</summary>
    public event EventHandler<PartialTranscriptEventArgs>? PartialChanged;

    /// <summary>True while the latest fed audio sounds like speech (drives the pill dot).</summary>
    public bool IsSpeechActive => _vad.IsSpeechActive;

    /// <summary>True once any speech has been detected in the utterance.</summary>
    public bool HadSpeech => _hadSpeech;

    /// <summary>Total audio fed so far.</summary>
    public TimeSpan AudioDuration => TimeSpan.FromSeconds(TotalSamples / (double)SampleRate);

    /// <summary>Silence duration since the last speech (or since start when none), for auto-stop.</summary>
    public TimeSpan TrailingSilence
    {
        get
        {
            lock (_samples)
            {
                if (_vad.IsSpeechActive)
                {
                    return TimeSpan.Zero;
                }

                return TimeSpan.FromSeconds((_samples.Count - _lastSpeechSample) / (double)SampleRate);
            }
        }
    }

    private int TotalSamples
    {
        get
        {
            lock (_samples)
            {
                return _samples.Count;
            }
        }
    }

    /// <summary>Feeds newly captured 16 kHz mono samples (audio-pump thread).</summary>
    public void Accept(ReadOnlyMemory<float> samples)
    {
        EnterOperation();
        try
        {
            if (samples.IsEmpty)
            {
                return;
            }

            _vad.Accept(samples);
            lock (_samples)
            {
                _samples.AddRange(samples.Span);
                if (_vad.IsSpeechActive)
                {
                    _hadSpeech = true;
                    _tailHasSpeech = true;
                    _lastSpeechSample = _samples.Count;
                }
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Decodes any newly closed segments (once each) and re-decodes the open
    /// tail. Skips silently when a previous tick is still decoding, so a slow
    /// decode never queues up a backlog.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        EnterOperation();
        try
        {
            if (!await _decodeGate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                await DecodeClosedSegmentsAsync(cancellationToken).ConfigureAwait(false);
                await DecodeOpenTailAsync(cancellationToken).ConfigureAwait(false);
                RaisePartialIfChanged();
            }
            finally
            {
                _decodeGate.Release();
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Ends the utterance: flushes the VAD (closing the open segment), decodes
    /// only what was never decoded as stable, and returns the joined transcript
    /// with dictionary replacements applied.
    /// </summary>
    public async Task<SttResult> FinalizeAsync(CancellationToken cancellationToken)
    {
        EnterOperation();
        try
        {
            await _decodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _vad.Flush();
                await DecodeClosedSegmentsAsync(cancellationToken).ConfigureAwait(false);

                // After a flush every speech region is a closed segment, so any
                // remaining tail is silence — nothing left to decode.
                string transcript = TranscriptDictionary.Apply(_stableText, _options.Replacements);
                return new SttResult(transcript, _options.Language, AudioDuration);
            }
            finally
            {
                _decodeGate.Release();
            }
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task DecodeClosedSegmentsAsync(CancellationToken cancellationToken)
    {
        while (_vad.TryPopSegment(out VadSpeechSegment segment))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Samples.Length == 0)
            {
                continue;
            }

            string text = (await _provider.TranscribeSegmentAsync(
                    new AudioBuffer(segment.Samples),
                    _options,
                    cancellationToken)
                .ConfigureAwait(false)).Trim();

            if (text.Length > 0)
            {
                // Append-only join: the stable text is maintained incrementally
                // (never re-joined per tick), and the formatter stitches any
                // punctuation the VAD boundary split onto its own segment.
                _stableText = TranscriptFormatter.Join([_stableText, text]);
            }

            int segmentEnd = segment.StartSample + segment.Samples.Length;
            lock (_samples)
            {
                _stableEndSample = Math.Max(_stableEndSample, segmentEnd);
                _tailHasSpeech = _lastSpeechSample > _stableEndSample;
            }

            _volatileText = string.Empty;
        }
    }

    private async Task DecodeOpenTailAsync(CancellationToken cancellationToken)
    {
        float[] tail;
        lock (_samples)
        {
            if (!_tailHasSpeech || _samples.Count - _stableEndSample < MinTailSamplesToDecode)
            {
                return;
            }

            tail = new float[_samples.Count - _stableEndSample];
            _samples.CopyTo(_stableEndSample, tail, 0, tail.Length);
        }

        string text = (await _provider.TranscribeSegmentAsync(
                new AudioBuffer(tail), _options, cancellationToken)
            .ConfigureAwait(false)).Trim();
        _volatileText = text;
    }

    private void RaisePartialIfChanged()
    {
        string stable = TranscriptDictionary.Apply(_stableText, _options.Replacements);
        string volatilePart = TranscriptDictionary.Apply(_volatileText, _options.Replacements);
        if (stable == _lastRaisedStable && volatilePart == _lastRaisedVolatile)
        {
            return;
        }

        _lastRaisedStable = stable;
        _lastRaisedVolatile = volatilePart;
        PartialChanged?.Invoke(this, new PartialTranscriptEventArgs(stable, volatilePart));
    }

    /// <summary>Stops new work and releases the decode gate after in-flight operations finish.</summary>
    public void Dispose()
    {
        bool disposeGate = false;
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_activeOperations == 0)
            {
                _decodeGateDisposed = true;
                disposeGate = true;
            }
        }

        if (disposeGate)
        {
            _decodeGate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void EnterOperation()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeOperations++;
        }
    }

    private void ExitOperation()
    {
        bool disposeGate = false;
        lock (_lifetimeGate)
        {
            _activeOperations--;
            if (_disposed && _activeOperations == 0 && !_decodeGateDisposed)
            {
                _decodeGateDisposed = true;
                disposeGate = true;
            }
        }

        if (disposeGate)
        {
            _decodeGate.Dispose();
        }
    }
}

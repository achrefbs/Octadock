using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Speech;
using Xunit;

namespace Octadock.Core.Tests.Speech;

public sealed class SimulatedStreamingSessionTests
{
    private const int Rate = 16_000;

    private readonly FakeVad _vad = new();
    private readonly FakeStreamingProvider _provider = new();

    private SimulatedStreamingSession CreateSession(SttOptions? options = null)
        => new(_provider, _vad, options ?? new SttOptions());

    [Fact]
    public async Task Closed_segments_decode_exactly_once_and_stay_stable()
    {
        using SimulatedStreamingSession session = CreateSession();
        var partials = new List<PartialTranscriptEventArgs>();
        session.PartialChanged += (_, e) => partials.Add(e);

        // One second of speech that the VAD closes into a segment.
        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate, value: 1f));
        _vad.IsSpeechActive = false;
        _vad.CloseSegment(startSample: 0, Samples(Rate, value: 1f));

        await session.TickAsync(CancellationToken.None);
        await session.TickAsync(CancellationToken.None);
        await session.TickAsync(CancellationToken.None);

        // The closed segment was decoded once, not re-decoded per tick.
        _provider.CallsFor(1f).Should().Be(1);
        partials.Should().ContainSingle();
        partials[0].Stable.Should().Be("seg-1");
        partials[0].Volatile.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_tail_redecodes_every_tick_and_reports_volatile_text()
    {
        using SimulatedStreamingSession session = CreateSession();
        var partials = new List<PartialTranscriptEventArgs>();
        session.PartialChanged += (_, e) => partials.Add(e);

        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate, value: 2f));
        await session.TickAsync(CancellationToken.None);

        session.Accept(Samples(Rate, value: 2f));
        await session.TickAsync(CancellationToken.None);

        // No closed segment yet: both decodes hit the growing open tail.
        _provider.CallsFor(2f).Should().Be(2);
        _provider.LastLengthFor(2f).Should().Be(2 * Rate);
        partials.Should().HaveCount(1); // Same volatile text both times → one event.
        partials[0].Stable.Should().BeEmpty();
        partials[0].Volatile.Should().Be("seg-2");
    }

    [Fact]
    public async Task Finalize_decodes_only_the_flushed_tail_and_joins_with_dictionary()
    {
        var options = new SttOptions
        {
            Replacements = [new KeyValuePair<string, string>("seg-1 seg-2", "joined!")],
        };
        using SimulatedStreamingSession session = CreateSession(options);

        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate, value: 1f));
        _vad.IsSpeechActive = false;
        _vad.CloseSegment(0, Samples(Rate, value: 1f));
        await session.TickAsync(CancellationToken.None);
        _provider.CallsFor(1f).Should().Be(1);

        // More speech that only closes when Finalize flushes the VAD.
        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate, value: 2f));
        _vad.OnFlush = () => _vad.CloseSegment(Rate, Samples(Rate, value: 2f));

        SttResult result = await session.FinalizeAsync(CancellationToken.None);

        result.Text.Should().Be("joined!");
        result.AudioDuration.Should().Be(TimeSpan.FromSeconds(2));
        _provider.CallsFor(1f).Should().Be(1, "stable segments must not be re-decoded at finalize");
        _provider.CallsFor(2f).Should().Be(1);
    }

    [Fact]
    public async Task Finalize_without_any_speech_returns_empty()
    {
        using SimulatedStreamingSession session = CreateSession();
        session.Accept(Samples(Rate, value: 0f));

        SttResult result = await session.FinalizeAsync(CancellationToken.None);

        result.IsEmpty.Should().BeTrue();
        _provider.TotalCalls.Should().Be(0);
    }

    [Fact]
    public void Trailing_silence_grows_after_speech_ends()
    {
        using SimulatedStreamingSession session = CreateSession();
        session.HadSpeech.Should().BeFalse();

        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate, value: 1f));
        session.HadSpeech.Should().BeTrue();
        session.TrailingSilence.Should().Be(TimeSpan.Zero);

        _vad.IsSpeechActive = false;
        session.Accept(Samples(2 * Rate, value: 0f));

        session.TrailingSilence.Should().Be(TimeSpan.FromSeconds(2));
        session.AudioDuration.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Silence_only_audio_is_never_decoded()
    {
        using SimulatedStreamingSession session = CreateSession();

        session.Accept(Samples(Rate, value: 3f));
        await session.TickAsync(CancellationToken.None);

        _provider.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task Tail_under_the_minimum_length_is_not_decoded_yet()
    {
        using SimulatedStreamingSession session = CreateSession();

        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate / 10, value: 3f)); // 0.1 s < 0.3 s minimum.
        await session.TickAsync(CancellationToken.None);

        _provider.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_rejects_further_decodes()
    {
        SimulatedStreamingSession session = CreateSession();

        session.Dispose();
        session.Dispose();

        Func<Task> tick = () => session.TickAsync(CancellationToken.None);
        await tick.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_during_decode_defers_gate_cleanup_until_the_decode_finishes()
    {
        var session = CreateSession();
        _provider.BlockSegments = true;
        _vad.IsSpeechActive = true;
        session.Accept(Samples(Rate, value: 4f));

        Task tick = session.TickAsync(CancellationToken.None);
        await _provider.SegmentEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        session.Dispose();
        _provider.AllowSegment.TrySetResult();

        await tick.WaitAsync(TimeSpan.FromSeconds(2));
        Func<Task> nextTick = () => session.TickAsync(CancellationToken.None);
        await nextTick.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static float[] Samples(int count, float value)
    {
        var samples = new float[count];
        Array.Fill(samples, value);
        return samples;
    }

    // ---- Fakes ----

    private sealed class FakeVad : IVoiceActivityDetector
    {
        private readonly Queue<VadSpeechSegment> _segments = new();

        public bool IsAvailable => true;

        public bool IsSpeechActive { get; set; }

        public Action? OnFlush { get; set; }

        public void CloseSegment(int startSample, float[] samples)
            => _segments.Enqueue(new VadSpeechSegment(startSample, samples));

        public void Reset()
        {
            _segments.Clear();
            IsSpeechActive = false;
        }

        public void Accept(ReadOnlyMemory<float> samples)
        {
        }

        public void Flush()
        {
            IsSpeechActive = false;
            OnFlush?.Invoke();
        }

        public bool TryPopSegment(out VadSpeechSegment segment)
            => _segments.TryDequeue(out segment);
    }

    /// <summary>
    /// Returns "seg-N" where N is the first sample's value, so tests can tell
    /// exactly which audio region each decode covered.
    /// </summary>
    private sealed class FakeStreamingProvider : IStreamingSpeechToTextProvider
    {
        private readonly Dictionary<float, int> _calls = [];
        private readonly Dictionary<float, int> _lastLength = [];

        public string Id => "fake-streaming";

        public bool IsAvailable => true;

        public int TotalCalls { get; private set; }

        public int CallsFor(float marker) => _calls.GetValueOrDefault(marker);

        public int LastLengthFor(float marker) => _lastLength.GetValueOrDefault(marker);

        public bool BlockSegments { get; set; }

        public TaskCompletionSource SegmentEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSegment { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> TranscribeSegmentAsync(
            AudioBuffer segment, SttOptions options, CancellationToken cancellationToken)
        {
            float marker = segment.Samples[0];
            TotalCalls++;
            _calls[marker] = _calls.GetValueOrDefault(marker) + 1;
            _lastLength[marker] = segment.Samples.Length;
            SegmentEntered.TrySetResult();
            if (BlockSegments)
            {
                await AllowSegment.Task.ConfigureAwait(false);
            }

            return $"seg-{marker:0}";
        }

        public Task<SttResult> TranscribeAsync(AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
            => throw new NotSupportedException("Streaming tests must not hit the offline path.");
    }
}

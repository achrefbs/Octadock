using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class DictationControllerTests
{
    private readonly FakeAudioSource _audio = new();
    private readonly FakeSttProvider _provider = new("whisper");
    private readonly FakeClipboardService _clipboard = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeSettingsService _settings = new();
    private readonly FakeModelDownloadConsent _consent = new();

    private DictationController CreateController(params ISpeechToTextProvider[] extraProviders)
        => CreateControllerWith([_provider, .. extraProviders]);

    /// <summary>Builds a controller over exactly these providers (no implicit default whisper fake).</summary>
    private DictationController CreateControllerWith(
        ISpeechToTextProvider[] providers, IVoiceActivityDetector? vad = null)
        => new(
            new FakeProviderFactory(providers),
            _audio,
            _clipboard,
            _notifications,
            new FakeMonitorService(),
            _settings,
            _consent,
            new Octadock.App.Tests.Fakes.AllowAllLicenseGate(),
            NullLogger<DictationController>.Instance,
            vad);

    [Fact]
    public async Task Toggle_starts_then_stops_and_places_transcript_on_clipboard()
    {
        _settings.SetSpeech(s => s with { Provider = "whisper", InsertionMode = "clipboard" });
        _provider.NextResult = new SttResult("hello world", "en", TimeSpan.FromSeconds(2));
        _audio.NextBuffer = new AudioBuffer(new float[16_000]);
        DictationController controller = CreateController();

        await controller.ToggleAsync();
        controller.IsListening.Should().BeTrue();
        _audio.Started.Should().Be(1);

        await controller.ToggleAsync();
        controller.IsListening.Should().BeFalse();
        _audio.Stopped.Should().Be(1);
        _clipboard.LastText.Should().Be("hello world");
    }

    [Fact]
    public async Task Empty_transcription_notifies_without_writing_clipboard()
    {
        _settings.SetSpeech(s => s with { Provider = "whisper", InsertionMode = "clipboard" });
        _provider.NextResult = new SttResult(string.Empty, null, TimeSpan.Zero);
        DictationController controller = CreateController();

        await controller.ToggleAsync();
        await controller.ToggleAsync();

        _clipboard.LastText.Should().BeNull();
        _notifications.Messages.Should().Contain(m => m.Contains("No speech detected"));
    }

    [Fact]
    public async Task Unavailable_non_model_backed_provider_blocks_with_notification()
    {
        var cloud = new FakeSttProvider("openai") { IsAvailable = false };
        _settings.SetSpeech(s => s with { Provider = "openai", InsertionMode = "clipboard" });
        DictationController controller = CreateController(cloud);

        DictationOperationResult result = await controller.ToggleWithResultAsync();

        result.Status.Should().Be(DictationOperationStatus.Declined);
        controller.IsListening.Should().BeFalse();
        _audio.Started.Should().Be(0);
        _notifications.Titles.Should().Contain("Speech provider unavailable");
    }

    [Fact]
    public async Task Model_backed_provider_downloads_missing_model_before_capture()
    {
        var local = new FakeModelBackedProvider("parakeet") { ModelOnDisk = false };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateController(local);

        await controller.ToggleAsync();

        local.EnsureCalls.Should().Be(1);
        controller.IsListening.Should().BeTrue();
        _audio.Started.Should().Be(1);
    }

    [Fact]
    public async Task Second_toggle_cancels_model_preparation_without_starting_microphone()
    {
        var local = new FakeModelBackedProvider("parakeet")
        {
            ModelOnDisk = false,
            BlockEnsure = true,
        };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([local]);

        Task<DictationOperationResult> starting = controller.ToggleWithResultAsync();
        await local.EnsureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        controller.IsPreparing.Should().BeTrue();

        DictationOperationResult cancelRequest = await controller.ToggleWithResultAsync();
        DictationOperationResult startResult = await starting.WaitAsync(TimeSpan.FromSeconds(2));

        cancelRequest.Status.Should().Be(DictationOperationStatus.Cancelled);
        startResult.Status.Should().Be(DictationOperationStatus.Cancelled);
        controller.IsPreparing.Should().BeFalse();
        controller.IsListening.Should().BeFalse();
        _audio.Started.Should().Be(0);
        _audio.Stopped.Should().Be(0);
    }

    [Fact]
    public async Task Second_toggle_cancels_provider_warm_up_without_starting_microphone()
    {
        var local = new FakeModelBackedProvider("parakeet")
        {
            ModelOnDisk = true,
            BlockPrepare = true,
        };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([local]);

        Task<DictationOperationResult> starting = controller.ToggleWithResultAsync();
        await local.PrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        controller.IsPreparing.Should().BeTrue();

        DictationOperationResult cancelRequest = await controller.ToggleWithResultAsync();
        DictationOperationResult startResult = await starting.WaitAsync(TimeSpan.FromSeconds(2));

        cancelRequest.Status.Should().Be(DictationOperationStatus.Cancelled);
        startResult.Status.Should().Be(DictationOperationStatus.Cancelled);
        local.PrepareCalls.Should().Be(1);
        controller.IsPreparing.Should().BeFalse();
        controller.IsListening.Should().BeFalse();
        _audio.Started.Should().Be(0);
    }

    [Fact]
    public async Task Discard_cancels_model_preparation_without_waiting_for_toggle_gate()
    {
        var local = new FakeModelBackedProvider("parakeet")
        {
            ModelOnDisk = false,
            BlockEnsure = true,
        };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([local]);

        Task<DictationOperationResult> starting = controller.ToggleWithResultAsync();
        await local.EnsureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await controller.DiscardAsync();
        DictationOperationResult result = await starting.WaitAsync(TimeSpan.FromSeconds(2));

        result.Status.Should().Be(DictationOperationStatus.Cancelled);
        controller.IsPreparing.Should().BeFalse();
        controller.IsListening.Should().BeFalse();
        _audio.Started.Should().Be(0);
    }

    [Fact]
    public async Task Discard_racing_microphone_start_closes_the_microphone_and_never_publishes_listening()
    {
        _settings.SetSpeech(s => s with { Provider = "whisper", InsertionMode = "clipboard" });
        _audio.BlockStart = true;
        DictationController controller = CreateController();

        Task<DictationOperationResult> starting = Task.Run(() => controller.ToggleWithResultAsync());
        await _audio.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await controller.DiscardAsync();
        _audio.AllowStart.TrySetResult();
        DictationOperationResult result = await starting.WaitAsync(TimeSpan.FromSeconds(2));

        result.Status.Should().Be(DictationOperationStatus.Cancelled);
        controller.IsPreparing.Should().BeFalse();
        controller.IsListening.Should().BeFalse();
        _audio.Started.Should().Be(1);
        _audio.Stopped.Should().Be(1, "a cancelled synchronous device start must be unwound");
    }

    [Fact]
    public async Task Model_backed_provider_with_model_on_disk_skips_download()
    {
        var local = new FakeModelBackedProvider("parakeet") { ModelOnDisk = true };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateController(local);

        await controller.ToggleAsync();

        local.EnsureCalls.Should().Be(0);
        controller.IsListening.Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_provider_id_blocks_with_notification()
    {
        _settings.SetSpeech(s => s with { Provider = "does-not-exist" });
        DictationController controller = CreateController();

        await controller.ToggleAsync();

        controller.IsListening.Should().BeFalse();
        _notifications.Titles.Should().Contain("Speech provider unavailable");
    }

    [Fact]
    public async Task Missing_parakeet_model_dictates_with_whisper_and_downloads_in_background()
    {
        var parakeet = new FakeModelBackedProvider("parakeet") { ModelOnDisk = false };
        var whisper = new FakeModelBackedProvider("whisper")
        {
            ModelOnDisk = true,
            NextResult = new SttResult("stopgap works", "en", TimeSpan.FromSeconds(1)),
        };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([parakeet, whisper]);

        await controller.ToggleAsync();
        controller.IsListening.Should().BeTrue();

        await controller.ToggleAsync();

        // The utterance ran on Whisper; Parakeet fetched in the background.
        whisper.LastOptions.Should().NotBeNull();
        parakeet.LastOptions.Should().BeNull();
        await WaitForAsync(() => parakeet.EnsureCalls == 1);
        _clipboard.LastText.Should().Be("stopgap works");
    }

    [Fact]
    public async Task Missing_parakeet_model_without_whisper_downloads_before_capture()
    {
        var parakeet = new FakeModelBackedProvider("parakeet") { ModelOnDisk = false };
        var whisper = new FakeModelBackedProvider("whisper") { ModelOnDisk = false };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([parakeet, whisper]);

        await controller.ToggleAsync();

        parakeet.EnsureCalls.Should().Be(1);
        controller.IsListening.Should().BeTrue();
        _audio.Started.Should().Be(1);
    }

    [Fact]
    public async Task Declining_model_download_consent_blocks_the_fetch_and_does_not_start()
    {
        _consent.Granted = false;
        var parakeet = new FakeModelBackedProvider("parakeet") { ModelOnDisk = false };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([parakeet]);

        await controller.ToggleAsync();

        _consent.Calls.Should().Be(1, "consent must be requested before any fetch");
        parakeet.EnsureCalls.Should().Be(0, "a declined download must not fetch the model");
        controller.IsListening.Should().BeFalse();
        _audio.Started.Should().Be(0);
        _notifications.Titles.Should().Contain("Dictation needs a model");
    }

    [Fact]
    public async Task Unsupported_explicit_language_routes_the_utterance_to_whisper()
    {
        var parakeet = new FakeLanguageScopedProvider("parakeet", supported: ["en", "de"]);
        var whisper = new FakeSttProvider("whisper")
        {
            NextResult = new SttResult("konnichiwa", "ja", TimeSpan.FromSeconds(1)),
        };
        _settings.SetSpeech(s => s with
        {
            Provider = "parakeet",
            Language = "ja",
            InsertionMode = "clipboard",
        });
        DictationController controller = CreateControllerWith([parakeet, whisper]);

        await controller.ToggleAsync();
        await controller.ToggleAsync();

        whisper.LastOptions.Should().NotBeNull();
        parakeet.LastOptions.Should().BeNull();
        _clipboard.LastText.Should().Be("konnichiwa");
    }

    [Fact]
    public async Task Supported_explicit_language_stays_on_the_selected_provider()
    {
        var parakeet = new FakeLanguageScopedProvider("parakeet", supported: ["en", "de"])
        {
            NextResult = new SttResult("hallo", "de", TimeSpan.FromSeconds(1)),
        };
        _settings.SetSpeech(s => s with
        {
            Provider = "parakeet",
            Language = "de",
            InsertionMode = "clipboard",
        });
        DictationController controller = CreateController(parakeet);

        await controller.ToggleAsync();
        await controller.ToggleAsync();

        parakeet.LastOptions.Should().NotBeNull();
        _clipboard.LastText.Should().Be("hallo");
    }

    [Fact]
    public async Task Streaming_provider_with_vad_finalizes_from_segments_not_the_offline_path()
    {
        var streaming = new FakeStreamingSttProvider("parakeet");
        var vad = new FakeControllerVad();
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        _audio.NextBuffer = new AudioBuffer(Enumerable.Repeat(1f, 16_000).ToArray());
        DictationController controller = CreateControllerWith([streaming], vad);

        await controller.ToggleAsync();
        controller.IsListening.Should().BeTrue();
        vad.ResetCalls.Should().Be(1);

        await controller.ToggleAsync();

        // Stop()'s final drain fed the session; Flush closed the segment and
        // finalize decoded it — the batch TranscribeAsync path stayed cold.
        streaming.SegmentCalls.Should().BeGreaterThan(0);
        streaming.BatchCalls.Should().Be(0);
        _clipboard.LastText.Should().Be("streamed text");
    }

    [Fact]
    public async Task Discard_waits_for_an_inflight_streaming_decode_before_disposal()
    {
        var streaming = new FakeStreamingSttProvider("parakeet") { BlockSegments = true };
        var vad = new FakeControllerVad();
        _settings.SetSpeech(s => s with
        {
            Provider = "parakeet",
            InsertionMode = "clipboard",
            LivePartials = true,
        });
        DictationController controller = CreateControllerWith([streaming], vad);

        await controller.ToggleAsync();
        _audio.Emit(Enumerable.Repeat(1f, 16_000).ToArray());
        await streaming.SegmentEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Task discard = controller.DiscardAsync();
        try
        {
            await Task.Delay(50);
            discard.IsCompleted.Should().BeFalse(
                "teardown must await the decode loop before disposing its semaphore");
        }
        finally
        {
            streaming.AllowSegment.TrySetResult();
        }

        await discard.WaitAsync(TimeSpan.FromSeconds(2));
        controller.IsListening.Should().BeFalse();
        _audio.Stopped.Should().Be(1);

        streaming.BlockSegments = false;
        await controller.ToggleAsync();
        controller.IsListening.Should().BeTrue("a cleanly disposed session must not poison the next one");
        await controller.DiscardAsync();
    }

    [Fact]
    public async Task Streaming_falls_back_to_batch_when_vad_is_unavailable()
    {
        var streaming = new FakeStreamingSttProvider("parakeet");
        var vad = new FakeControllerVad { IsAvailable = false };
        _settings.SetSpeech(s => s with { Provider = "parakeet", InsertionMode = "clipboard" });
        DictationController controller = CreateControllerWith([streaming], vad);

        await controller.ToggleAsync();
        await controller.ToggleAsync();

        streaming.SegmentCalls.Should().Be(0);
        streaming.BatchCalls.Should().Be(1);
    }

    [Fact]
    public async Task Already_cancelled_stop_still_stops_audio_before_reporting_cancelled()
    {
        _settings.SetSpeech(s => s with { Provider = "whisper", InsertionMode = "clipboard" });
        DictationController controller = CreateController();
        await controller.ToggleAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        DictationOperationResult result = await controller.ToggleWithResultAsync(cancelled.Token);

        result.Status.Should().Be(DictationOperationStatus.Cancelled);
        controller.IsListening.Should().BeFalse();
        _audio.Stopped.Should().Be(1, "cleanup must run even when Task.Run would otherwise be pre-cancelled");
        _clipboard.LastText.Should().BeNull();
        _provider.LastOptions.Should().BeNull("transcription should not begin after cancellation");
    }

    [Fact]
    public async Task Streaming_failure_preserves_best_live_partial_on_clipboard()
    {
        var streaming = new FakeStreamingSttProvider("parakeet")
        {
            FailAfterSegmentCalls = 1,
        };
        var vad = new FakeControllerVad();
        _settings.SetSpeech(s => s with
        {
            Provider = "parakeet",
            InsertionMode = "clipboard",
            LivePartials = true,
        });
        DictationController controller = CreateControllerWith([streaming], vad);

        await controller.ToggleAsync();
        _audio.Emit(Enumerable.Repeat(1f, 16_000).ToArray());
        await WaitForAsync(() => streaming.SegmentCalls >= 1);

        DictationOperationResult result = await controller.ToggleWithResultAsync();

        result.Status.Should().Be(DictationOperationStatus.Failed);
        controller.LastRecoveredTranscript.Should().Be("streamed text");
        _clipboard.LastText.Should().Be("streamed text");
        _notifications.Messages.Should().Contain(message => message.Contains("partial transcript", StringComparison.OrdinalIgnoreCase));
        _audio.Stopped.Should().Be(1);
    }

    [Theory]
    [InlineData(41u, 41u, "dictated text", "dictated text", true)]
    [InlineData(41u, 42u, "dictated text", "dictated text", false)]
    [InlineData(0u, 0u, "dictated text", "new user copy", false)]
    [InlineData(0u, 0u, "dictated text", null, false)]
    public void Clipboard_restore_only_runs_when_octadock_write_is_still_current(
        uint expectedSequence,
        uint currentSequence,
        string expectedText,
        string? currentText,
        bool expected)
    {
        DictationController.ShouldRestoreClipboard(
                expectedSequence,
                currentSequence,
                expectedText,
                currentText)
            .Should().Be(expected);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }

    [Fact]
    public async Task Custom_dictionary_replacements_reach_the_provider()
    {
        _settings.SetSpeech(s => s with
        {
            Provider = "whisper",
            InsertionMode = "clipboard",
            CustomDictionary = "big arrow => ==>",
        });
        _provider.NextResult = new SttResult("x", null, TimeSpan.FromSeconds(1));
        DictationController controller = CreateController();

        await controller.ToggleAsync();
        await controller.ToggleAsync();

        _provider.LastOptions.Should().NotBeNull();
        _provider.LastOptions!.Replacements.Should().Contain(
            new KeyValuePair<string, string>("big arrow", "==>"));
        // The starter dictionary stays underneath user entries.
        _provider.LastOptions.Replacements.Should().Contain(
            new KeyValuePair<string, string>("arrow function", "=>"));
    }

    // ---- Fakes ----

    private sealed class FakeAudioSource : IDictationAudioSource
    {
        public int Started { get; private set; }

        public int Stopped { get; private set; }

        public bool BlockStart { get; set; }

        public TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowStart { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AudioBuffer NextBuffer { get; set; } = new([]);

        public float LastPeak => 0;

        public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;

        public void Start()
        {
            Started++;
            StartEntered.TrySetResult();
            if (BlockStart)
            {
                AllowStart.Task.GetAwaiter().GetResult();
            }
        }

        public AudioBuffer Stop()
        {
            Stopped++;
            SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(NextBuffer.Samples));
            return NextBuffer;
        }

        public void Emit(float[] samples)
            => SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(samples));
    }

    private class FakeSttProvider(string id) : ISpeechToTextProvider
    {
        public string Id { get; } = id;

        public bool IsAvailable { get; set; } = true;

        public SttResult NextResult { get; set; } = new("ok", null, TimeSpan.FromSeconds(1));

        public SttOptions? LastOptions { get; private set; }

        public Task<SttResult> TranscribeAsync(AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
        {
            LastOptions = options;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeLanguageScopedProvider(string id, string[] supported)
        : FakeSttProvider(id), ILanguageScopedSpeechProvider
    {
        public bool SupportsLanguage(string? language)
            => string.IsNullOrWhiteSpace(language)
               || supported.Contains(language.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeStreamingSttProvider(string id) : IStreamingSpeechToTextProvider
    {
        public string Id { get; } = id;

        public bool IsAvailable => true;

        public int SegmentCalls { get; private set; }

        public int BatchCalls { get; private set; }

        public int FailAfterSegmentCalls { get; set; } = int.MaxValue;

        public bool BlockSegments { get; set; }

        public TaskCompletionSource SegmentEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowSegment { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<string> TranscribeSegmentAsync(
            AudioBuffer segment, SttOptions options, CancellationToken cancellationToken)
        {
            SegmentCalls++;
            SegmentEntered.TrySetResult();
            if (BlockSegments)
            {
                await AllowSegment.Task.ConfigureAwait(false);
            }

            if (SegmentCalls > FailAfterSegmentCalls)
            {
                throw new InvalidOperationException("streaming transcription failed");
            }

            return "streamed text";
        }

        public Task<SttResult> TranscribeAsync(AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
        {
            BatchCalls++;
            return Task.FromResult(new SttResult("batch text", null, audio.Duration));
        }
    }

    /// <summary>Marks everything as speech and closes one whole-utterance segment on flush.</summary>
    private sealed class FakeControllerVad : IVoiceActivityDetector
    {
        private readonly List<float> _buffered = [];
        private VadSpeechSegment? _closed;

        public bool IsAvailable { get; set; } = true;

        public bool IsSpeechActive { get; private set; }

        public int ResetCalls { get; private set; }

        public void Reset()
        {
            ResetCalls++;
            _buffered.Clear();
            _closed = null;
            IsSpeechActive = false;
        }

        public void Accept(ReadOnlyMemory<float> samples)
        {
            IsSpeechActive = true;
            _buffered.AddRange(samples.ToArray());
        }

        public void Flush()
        {
            IsSpeechActive = false;
            if (_buffered.Count > 0)
            {
                _closed = new VadSpeechSegment(0, [.. _buffered]);
                _buffered.Clear();
            }
        }

        public bool TryPopSegment(out VadSpeechSegment segment)
        {
            if (_closed is { } ready)
            {
                _closed = null;
                segment = ready;
                return true;
            }

            segment = default;
            return false;
        }
    }

    private sealed class FakeModelBackedProvider(string id) :
        FakeSttProvider(id),
        IModelBackedSpeechProvider,
        IPreparableSpeechProvider
    {
        public bool ModelOnDisk { get; set; }

        public int EnsureCalls { get; private set; }

        public bool BlockEnsure { get; set; }

        public bool BlockPrepare { get; set; }

        public int PrepareCalls { get; private set; }

        public TaskCompletionSource EnsureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PrepareStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsModelAvailable(string? model) => ModelOnDisk;

        public long ModelDownloadBytes(string? model) => 100 * 1024 * 1024;

        public async Task EnsureModelAsync(
            string? model,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            EnsureCalls++;
            EnsureStarted.TrySetResult();
            if (BlockEnsure)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ModelOnDisk = true;
            progress?.Report(1.0);
        }

        public async Task PrepareAsync(string? model, CancellationToken cancellationToken)
        {
            PrepareCalls++;
            PrepareStarted.TrySetResult();
            if (BlockPrepare)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public void DeleteModel(string? model) => ModelOnDisk = false;
    }

    private sealed class FakeModelDownloadConsent : IModelDownloadConsent
    {
        /// <summary>Result returned by the gate; default granted so existing flows proceed.</summary>
        public bool Granted { get; set; } = true;

        public int Calls { get; private set; }

        public Task<bool> EnsureConsentAsync(
            ModelDownloadConsentRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Granted);
        }
    }

    private sealed class FakeProviderFactory(IReadOnlyList<ISpeechToTextProvider> providers) : ISpeechToTextProviderFactory
    {
        public ISpeechToTextProvider? Resolve(string providerId)
            => providers.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<SpeechProviderDescription> Describe()
            => providers.Select(p => new SpeechProviderDescription(p.Id, p.Id, p.IsAvailable)).ToArray();
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? LastText { get; private set; }

        public bool ContainsImage() => false;

        public void SetImage(EncodedImage image)
        {
        }

        public void SetImageFromFile(string filePath)
        {
        }

        public void SetFileDropList(IEnumerable<string> filePaths)
        {
        }

        public void SetText(string text) => LastText = text;

        public string? TryGetText() => LastText;

        public EncodedImage? TryGetImage() => null;
    }

    private sealed class FakeNotificationService : INotificationService
    {
        public List<string> Titles { get; } = [];

        public List<string> Messages { get; } = [];

        public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info, Action? clickAction = null)
        {
            Titles.Add(title);
            Messages.Add(message);
        }
    }

    private sealed class FakeMonitorService : IMonitorService
    {
        private static readonly DisplayInfo Monitor = new(
            new MonitorId(@"\\.\DISPLAY1"),
            0,
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040),
            1.0,
            IsPrimary: true,
            DeviceName: @"\\.\DISPLAY1");

        public PixelRect VirtualDesktopBounds => Monitor.Bounds;

        public event EventHandler? MonitorsChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public IReadOnlyList<DisplayInfo> GetMonitors() => [Monitor];

        public DisplayInfo GetPrimary() => Monitor;

        public DisplayInfo GetActiveMonitor() => Monitor;

        public DisplayInfo? FindById(MonitorId id) => Monitor;

        public DisplayInfo? Resolve(string? deviceName) => Monitor;

        public DisplayInfo GetMonitorFromPoint(PixelPoint point) => Monitor;
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public OctadockSettings Current { get; private set; } = OctadockSettings.Defaults;

        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public void SetSpeech(Func<SpeechSettings, SpeechSettings> mutate)
            => Current = Current with { Speech = mutate(Current.Speech) };

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, new SettingsChangedEventArgs(settings));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<OctadockSettings, OctadockSettings> mutate, CancellationToken cancellationToken = default)
            => SaveAsync(mutate(Current), cancellationToken);
    }
}

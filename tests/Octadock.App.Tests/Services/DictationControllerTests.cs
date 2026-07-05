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

    private DictationController CreateController(params ISpeechToTextProvider[] extraProviders)
        => CreateControllerWith([_provider, .. extraProviders]);

    /// <summary>Builds a controller over exactly these providers (no implicit default whisper fake).</summary>
    private DictationController CreateControllerWith(ISpeechToTextProvider[] providers)
        => new(
            new FakeProviderFactory(providers),
            _audio,
            _clipboard,
            _notifications,
            new FakeMonitorService(),
            _settings,
            NullLogger<DictationController>.Instance);

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

        await controller.ToggleAsync();

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

        public AudioBuffer NextBuffer { get; set; } = new([]);

        public float LastPeak => 0;

        public event EventHandler<AudioSamplesEventArgs>? SamplesAvailable;

        public void Start() => Started++;

        public AudioBuffer Stop()
        {
            Stopped++;
            SamplesAvailable?.Invoke(this, new AudioSamplesEventArgs(NextBuffer.Samples));
            return NextBuffer;
        }
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

    private sealed class FakeModelBackedProvider(string id) : FakeSttProvider(id), IModelBackedSpeechProvider
    {
        public bool ModelOnDisk { get; set; }

        public int EnsureCalls { get; private set; }

        public bool IsModelAvailable(string? model) => ModelOnDisk;

        public long ModelDownloadBytes(string? model) => 100 * 1024 * 1024;

        public Task EnsureModelAsync(string? model, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            EnsureCalls++;
            ModelOnDisk = true;
            progress?.Report(1.0);
            return Task.CompletedTask;
        }

        public void DeleteModel(string? model) => ModelOnDisk = false;
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

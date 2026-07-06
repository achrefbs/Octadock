using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Octadock.Core.Tests.Fakes;
using Xunit;

namespace Octadock.Core.Tests.Services;

/// <summary>
/// Verifies the one-time model-download consent policy (WS7, R6): consent is
/// asked once, remembered when granted, and never inferred — a decline does not
/// persist, so no download can proceed without an explicit yes.
/// </summary>
public class ModelDownloadConsentServiceTests
{
    private static readonly ModelDownloadConsentRequest Request =
        new("The Parakeet speech model", 672_000_000);

    [Fact]
    public async Task EnsureConsentAsync_returns_true_without_prompting_when_already_consented()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.SpeechModelDownloadConsented] = "true",
        });
        var settings = new SettingsService(store);
        await settings.LoadAsync();
        var prompt = new RecordingPrompt(answer: false); // would decline if it were asked
        var service = new ModelDownloadConsentService(settings, prompt);

        bool result = await service.EnsureConsentAsync(Request);

        result.Should().BeTrue();
        prompt.CallCount.Should().Be(0, "granted consent must never re-prompt");
    }

    [Fact]
    public async Task EnsureConsentAsync_prompts_persists_and_returns_true_when_accepted()
    {
        var store = new InMemorySettingsStore();
        var settings = new SettingsService(store);
        await settings.LoadAsync();
        var prompt = new RecordingPrompt(answer: true);
        var service = new ModelDownloadConsentService(settings, prompt);

        bool result = await service.EnsureConsentAsync(Request);

        result.Should().BeTrue();
        prompt.CallCount.Should().Be(1);
        settings.Current.Speech.ModelDownloadConsented.Should().BeTrue();
        store.Snapshot[SettingKeys.SpeechModelDownloadConsented].Should().Be("true");
    }

    [Fact]
    public async Task EnsureConsentAsync_returns_false_and_does_not_persist_when_declined()
    {
        var store = new InMemorySettingsStore();
        var settings = new SettingsService(store);
        await settings.LoadAsync();
        var prompt = new RecordingPrompt(answer: false);
        var service = new ModelDownloadConsentService(settings, prompt);

        bool result = await service.EnsureConsentAsync(Request);

        result.Should().BeFalse();
        prompt.CallCount.Should().Be(1);
        settings.Current.Speech.ModelDownloadConsented.Should().BeFalse();
        store.Snapshot.ContainsKey(SettingKeys.SpeechModelDownloadConsented)
            .Should().BeFalse("declining consent must not be remembered");
    }

    [Fact]
    public async Task EnsureConsentAsync_asks_again_after_a_previous_decline()
    {
        var store = new InMemorySettingsStore();
        var settings = new SettingsService(store);
        await settings.LoadAsync();
        var prompt = new RecordingPrompt(answer: false);
        var service = new ModelDownloadConsentService(settings, prompt);

        await service.EnsureConsentAsync(Request);
        await service.EnsureConsentAsync(Request);

        prompt.CallCount.Should().Be(2, "a prior decline is not remembered, so the next attempt re-asks");
    }

    private sealed class RecordingPrompt : IModelDownloadConsentPrompt
    {
        private readonly bool _answer;

        public RecordingPrompt(bool answer) => _answer = answer;

        public int CallCount { get; private set; }

        public Task<bool> RequestAsync(
            ModelDownloadConsentRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_answer);
        }
    }
}

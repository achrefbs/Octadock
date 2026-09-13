using FluentAssertions;
using Octadock.App.Ai;

using Octadock.Core.Ai;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class CliAiTextActionServiceTests
{
    [Fact]
    public void Review_is_the_exact_redacted_payload_and_does_not_retain_the_raw_secret()
    {
        const string secret = "sk-proj-abcdefghijklmnopqrstuvwxyz123456";
        var runner = new FakeRunner();
        var service = new CliAiTextActionService(new TextSecretDetector(), runner);

        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.Summarize,
            Text = $"Release notes. API key: {secret}",
            SourceName = "notes.md",
            ProviderId = AiCliProviderIds.Claude,
            RedactSecrets = true,
        });

        review.ProviderId.Should().Be(AiCliProviderIds.Claude);
        review.ProviderDisplayName.Should().Be("Claude");
        review.DetectedSecretCount.Should().Be(1);
        review.SecretsRedacted.Should().BeTrue();
        review.OutboundText.Should().Contain("Action: Summarize")
            .And.Contain("[REDACTED:OPENAI_API_KEY]")
            .And.NotContain(secret);
        review.OutboundCharacterCount.Should().Be(review.OutboundText.Length);
    }

    [Fact]
    public void Review_redacts_suffix_form_secret_key_before_external_send()
    {
        const string secret = "synthetic-cli-secret-010";
        var service = new CliAiTextActionService(
            new TextSecretDetector(),
            new FakeRunner());

        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.Explain,
            Text = $"AWS_SECRET_ACCESS_KEY = \"{secret}\"",
            ProviderId = AiCliProviderIds.Codex,
            RedactSecrets = true,
        });

        review.DetectedSecretCount.Should().Be(1);
        review.SecretsRedacted.Should().BeTrue();
        review.OutboundText.Should().Contain("[REDACTED:NAMED_SECRET]")
            .And.NotContain(secret);
    }

    [Fact]
    public void Review_redacts_the_complete_quoted_value_from_a_bracketed_assignment()
    {
        const string secret = "synthetic bracket secret;with punctuation";
        var service = new CliAiTextActionService(
            new TextSecretDetector(),
            new FakeRunner());

        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.Explain,
            Text = $"config[\"api_key\"] = \"{secret}\"",
            ProviderId = AiCliProviderIds.Codex,
            RedactSecrets = true,
        });

        review.DetectedSecretCount.Should().Be(1);
        review.SecretsRedacted.Should().BeTrue();
        review.OutboundText.Should().Contain("[REDACTED:NAMED_SECRET]")
            .And.NotContain(secret)
            .And.NotContain("with punctuation");
    }

    [Fact]
    public void Redaction_can_be_disabled_but_detection_stays_visible()
    {
        const string secret = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        var service = new CliAiTextActionService(
            new TextSecretDetector(),
            new FakeRunner());

        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.Explain,
            Text = secret,
            ProviderId = AiCliProviderIds.Codex,
            RedactSecrets = false,
        });

        review.DetectedSecretCount.Should().Be(1);
        review.SecretsRedacted.Should().BeFalse();
        review.OutboundText.Should().Contain(secret);
    }

    [Fact]
    public async Task Execute_uses_only_the_selected_provider_and_exact_reviewed_text()
    {
        var runner = new FakeRunner { Output = "  concise result  " };
        var service = new CliAiTextActionService(new TextSecretDetector(), runner);
        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.CleanRewrite,
            Text = "fix this sentence",
            ProviderId = AiCliProviderIds.Claude,
        });

        AiTextActionResult result = await service.ExecuteReviewedAsync(review);

        runner.Calls.Should().ContainSingle();
        runner.Calls[0].ProviderId.Should().Be(AiCliProviderIds.Claude);
        runner.Calls[0].OutboundText.Should().BeSameAs(review.OutboundText);
        result.Text.Should().Be("concise result");
        result.ProviderId.Should().Be(AiCliProviderIds.Claude);
    }

    [Fact]
    public async Task Execute_refuses_an_unavailable_selected_provider_without_fallback()
    {
        var runner = new FakeRunner
        {
            ProviderList =
            [
                new(AiCliProviderIds.Codex, "Codex", "Codex destination", false, "Codex is unavailable."),
                new(AiCliProviderIds.Claude, "Claude", "Claude destination", true),
            ],
        };
        var service = new CliAiTextActionService(new TextSecretDetector(), runner);
        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.Explain,
            Text = "text",
            ProviderId = AiCliProviderIds.Codex,
        });

        Func<Task> execute = () => service.ExecuteReviewedAsync(review);

        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("Codex is unavailable.");
        runner.Calls.Should().BeEmpty("Claude must never be used as a hidden fallback");
    }



    private sealed class FakeRunner : IAiCliRunner
    {
        public IReadOnlyList<AiCliProviderDescriptor> ProviderList { get; set; } =
        [
            new(AiCliProviderIds.Codex, "Codex", "Codex destination", true),
            new(AiCliProviderIds.Claude, "Claude", "Claude destination", true),
        ];

        public string Output { get; set; } = "result";

        public List<(string ProviderId, string OutboundText)> Calls { get; } = [];

        public IReadOnlyList<AiCliProviderDescriptor> Providers => ProviderList;

        public Task<string> RunAsync(
            string providerId,
            string outboundText,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((providerId, outboundText));
            return Task.FromResult(Output);
        }
    }


}

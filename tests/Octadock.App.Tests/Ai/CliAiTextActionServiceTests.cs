using FluentAssertions;
using Octadock.App.Ai;
using Octadock.App.Tests.Fakes;
using Octadock.Core.Ai;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class CliAiTextActionServiceTests
{
    [Fact]
    public void Review_is_the_exact_redacted_payload_and_does_not_retain_the_raw_secret()
    {
        const string secret = "sk-proj-abcdefghijklmnopqrstuvwxyz123456";
        var runner = new FakeRunner();
        var service = new CliAiTextActionService(new TextSecretDetector(), runner, new AllowAllLicenseGate());

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
    public void Redaction_can_be_disabled_but_detection_stays_visible()
    {
        const string secret = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        var service = new CliAiTextActionService(
            new TextSecretDetector(),
            new FakeRunner(),
            new AllowAllLicenseGate());

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
        var service = new CliAiTextActionService(new TextSecretDetector(), runner, new AllowAllLicenseGate());
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
        var service = new CliAiTextActionService(new TextSecretDetector(), runner, new AllowAllLicenseGate());
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

    [Fact]
    public async Task Execute_honors_the_paid_feature_gate_before_starting_a_cli()
    {
        var runner = new FakeRunner();
        var service = new CliAiTextActionService(new TextSecretDetector(), runner, new DenyLicenseGate());
        AiOutboundReview review = service.Review(new AiTextActionRequest
        {
            Action = AiTextActionKind.Explain,
            Text = "text",
            ProviderId = AiCliProviderIds.Codex,
        });

        Func<Task> execute = () => service.ExecuteReviewedAsync(review);

        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("*trial has ended*");
        runner.Calls.Should().BeEmpty();
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

    private sealed class DenyLicenseGate : ILicenseGate
    {
        public LicenseState State { get; } = new(LicenseMode.TrialExpired, null, null, false, false, "expired");

        public bool AllowsFullUse => false;

        public event EventHandler<LicenseState>? Refused { add { } remove { } }

        public bool Allow(GatedFeature feature) => false;
    }
}

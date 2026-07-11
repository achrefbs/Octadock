using FluentAssertions;
using Octadock.Core.Ai;
using Xunit;

namespace Octadock.Core.Tests.Ai;

public sealed class TextSecretDetectorTests
{
    private readonly TextSecretDetector _detector = new();

    [Fact]
    public void Detects_and_redacts_common_provider_tokens_without_returning_the_values()
    {
        const string openAi = "sk-proj-abcdefghijklmnopqrstuvwxyz123456";
        const string github = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        string input = $"OPENAI_API_KEY={openAi}\nGITHUB_TOKEN={github}";

        TextSecretScanResult result = _detector.Scan(input);

        result.Findings.Should().HaveCount(2);
        result.Findings.Select(finding => finding.Kind).Should()
            .BeEquivalentTo(["OPENAI_API_KEY", "GITHUB_TOKEN"]);
        result.RedactedText.Should().NotContain(openAi).And.NotContain(github);
        result.RedactedText.Should().Contain("[REDACTED:OPENAI_API_KEY]")
            .And.Contain("[REDACTED:GITHUB_TOKEN]");
    }

    [Fact]
    public void Named_secret_redacts_only_the_value_and_preserves_context()
    {
        TextSecretScanResult result = _detector.Scan("database password: correct-horse-battery-staple");

        result.Findings.Should().ContainSingle().Which.Kind.Should().Be("NAMED_SECRET");
        result.RedactedText.Should().Be("database password: [REDACTED:NAMED_SECRET]");
    }

    [Fact]
    public void Specific_token_wins_when_a_named_assignment_overlaps_it()
    {
        const string token = "sk-ant-abcdefghijklmnopqrstuvwxyz123456";

        TextSecretScanResult result = _detector.Scan($"api_key={token}");

        result.Findings.Should().ContainSingle();
        result.Findings[0].Kind.Should().Be("ANTHROPIC_API_KEY");
        result.RedactedText.Should().Be("api_key=[REDACTED:ANTHROPIC_API_KEY]");
    }

    [Fact]
    public void Private_key_block_is_one_finding()
    {
        const string key = "-----BEGIN PRIVATE KEY-----\nabc123secretmaterial\n-----END PRIVATE KEY-----";

        TextSecretScanResult result = _detector.Scan($"before\n{key}\nafter");

        result.Findings.Should().ContainSingle().Which.Kind.Should().Be("PRIVATE_KEY");
        result.RedactedText.Should().Be("before\n[REDACTED:PRIVATE_KEY]\nafter");
    }

    [Fact]
    public void Ordinary_text_is_unchanged()
    {
        const string input = "A design note with no credentials.";

        TextSecretScanResult result = _detector.Scan(input);

        result.Findings.Should().BeEmpty();
        result.RedactedText.Should().Be(input);
    }
}

using System.Diagnostics;
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

    [Theory]
    [InlineData("dotenv", "OCTADOCK_API_KEY=synthetic-env-secret-001", "synthetic-env-secret-001")]
    [InlineData("export", "export ACME_ACCESS_TOKEN='synthetic-export-secret-002'", "synthetic-export-secret-002")]
    [InlineData("PowerShell", "$env:ACME_AUTH_TOKEN = \"synthetic-powershell-secret-003\"", "synthetic-powershell-secret-003")]
    [InlineData("JSON", "{\"integrations_github_client_secret\":\"synthetic-json-secret-004\"}", "synthetic-json-secret-004")]
    [InlineData("YAML", "octadock.password: 'synthetic-yaml-secret-005'", "synthetic-yaml-secret-005")]
    [InlineData("TOML", "\"octadock.api_key\" = \"synthetic-toml-secret-006\"", "synthetic-toml-secret-006")]
    [InlineData("quoted key", "'tenant_secret': \"synthetic-quoted-secret-007\"", "synthetic-quoted-secret-007")]
    [InlineData("multiline", "OCTADOCK_API_KEY =\n\"synthetic-multiline-secret-008\"", "synthetic-multiline-secret-008")]
    [InlineData("quoted whitespace", "password: \"correct horse battery staple\"", "correct horse battery staple")]
    [InlineData("quoted punctuation", "client_secret='abc123;rest,still-secret'", "abc123;rest,still-secret")]
    [InlineData("JavaScript bracket key", "config[\"api_key\"] = \"synthetic-bracket-secret-009\"", "synthetic-bracket-secret-009")]
    [InlineData("Python bracket key", "os.environ['AUTH_TOKEN'] = 'synthetic-python-secret-010'", "synthetic-python-secret-010")]
    [InlineData("spaced bracket key", "config[ \"api_key\" ] = \"synthetic-spaced-bracket-secret-011\"", "synthetic-spaced-bracket-secret-011")]
    [InlineData("escaped quoted value", "password: \"synthetic-escaped-\\\"quote secret\"", "synthetic-escaped-\\\"quote secret")]
    [InlineData("AWS suffix key", "AWS_SECRET_ACCESS_KEY=\"synthetic-aws-secret-012\"", "synthetic-aws-secret-012")]
    [InlineData("Django suffix key", "DJANGO_SECRET_KEY='synthetic-django-secret-013'", "synthetic-django-secret-013")]
    [InlineData("private key assignment", "SSH_PRIVATE_KEY=synthetic-private-secret-014", "synthetic-private-secret-014")]
    public void Detects_and_redacts_namespaced_assignment_corpus(
        string syntax,
        string input,
        string secret)
    {
        TextSecretScanResult result = _detector.Scan(input);

        result.Findings.Should().ContainSingle($"the {syntax} assignment contains one synthetic secret")
            .Which.Kind.Should().Be("NAMED_SECRET");
        result.RedactedText.Should().Be(
            input.Replace(secret, "[REDACTED:NAMED_SECRET]", StringComparison.Ordinal),
            "the complete secret value must be removed rather than only its first token");
    }

    [Theory]
    [InlineData("OCTADOCK_API_KEY_NAME=development")]
    [InlineData("OCTADOCK_PASSWORD_HINT=use-a-vault")]
    [InlineData("{\"client_secret_enabled\":true}")]
    [InlineData("namespace.secretary = meeting-owner")]
    [InlineData("config[\"api_key_name\"] = \"development\"")]
    public void Does_not_treat_secret_like_configuration_names_as_assignments(string input)
    {
        TextSecretScanResult result = _detector.Scan(input);

        result.Findings.Should().BeEmpty();
        result.RedactedText.Should().Be(input);
    }

    [Fact]
    public void Max_length_namespaced_non_match_completes_within_a_linear_budget()
    {
        const string suffix = "notasecret";
        int repeatCount = (AiTextActionLimits.MaxInputCharacters - suffix.Length) / 2;
        string input = string.Concat(Enumerable.Repeat("A_", repeatCount)) + suffix;
        var stopwatch = Stopwatch.StartNew();

        TextSecretScanResult result = _detector.Scan(input);

        stopwatch.Stop();
        result.Findings.Should().BeEmpty();
        result.RedactedText.Should().Be(input);
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "a reviewed maximum-length non-match must not cause regex backtracking exhaustion");
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

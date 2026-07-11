using System.Text;
using System.Text.RegularExpressions;

namespace Octadock.Core.Ai;

/// <summary>A finding that exposes location and category, never the secret value.</summary>
public sealed record DetectedTextSecret(string Kind, int Start, int Length);

/// <summary>The safe scan result: locations plus a redacted copy, without retaining the original.</summary>
public sealed record TextSecretScanResult(
    IReadOnlyList<DetectedTextSecret> Findings,
    string RedactedText);

/// <summary>Detects common credentials in text before an explicit external send.</summary>
public interface ITextSecretDetector
{
    TextSecretScanResult Scan(string text);
}

/// <summary>
/// Deterministic local detector for common API tokens, private keys, bearer/JWT
/// credentials, and named secret assignments. It intentionally favors a small,
/// explainable rule set over opaque entropy scoring.
/// </summary>
public sealed class TextSecretDetector : ITextSecretDetector
{
    private static readonly DetectionRule[] Rules =
    [
        new("PRIVATE_KEY", new Regex(
            @"-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----[\s\S]*?-----END (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("ANTHROPIC_API_KEY", new Regex(
            @"\bsk-ant-[A-Za-z0-9_-]{16,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("OPENAI_API_KEY", new Regex(
            @"\bsk-(?:proj-)?[A-Za-z0-9_-]{16,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("GITHUB_TOKEN", new Regex(
            @"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("AWS_ACCESS_KEY", new Regex(
            @"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("SLACK_TOKEN", new Regex(
            @"\bxox[baprs]-[A-Za-z0-9-]{10,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("STRIPE_SECRET_KEY", new Regex(
            @"\b(?:sk|rk)_live_[A-Za-z0-9]{12,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("GOOGLE_API_KEY", new Regex(
            @"\bAIza[0-9A-Za-z_-]{20,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("JWT", new Regex(
            @"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        new("BEARER_TOKEN", new Regex(
            @"(?i)\bBearer\s+(?<secret>[A-Za-z0-9._~+/=-]{12,})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant), "secret"),
        new("NAMED_SECRET", new Regex(
            @"(?im)\b(?:api[_-]?key|access[_-]?token|auth[_-]?token|client[_-]?secret|password|passwd|secret)\b\s*[:=]\s*[\""']?(?<secret>[^\s\""';,]{6,})",
            RegexOptions.Compiled | RegexOptions.CultureInvariant), "secret"),
    ];

    /// <inheritdoc />
    public TextSecretScanResult Scan(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return new TextSecretScanResult(Array.Empty<DetectedTextSecret>(), string.Empty);
        }

        var candidates = new List<DetectedTextSecret>();
        foreach (DetectionRule rule in Rules)
        {
            foreach (Match match in rule.Pattern.Matches(text))
            {
                Group target = rule.GroupName is null ? match : match.Groups[rule.GroupName];
                if (target.Success && target.Length > 0)
                {
                    candidates.Add(new DetectedTextSecret(rule.Kind, target.Index, target.Length));
                }
            }
        }

        List<DetectedTextSecret> findings = RemoveOverlaps(candidates);
        return new TextSecretScanResult(findings, Redact(text, findings));
    }

    private static List<DetectedTextSecret> RemoveOverlaps(List<DetectedTextSecret> candidates)
    {
        var accepted = new List<DetectedTextSecret>();
        foreach (DetectedTextSecret candidate in candidates
                     .OrderBy(finding => finding.Start)
                     .ThenByDescending(finding => finding.Length))
        {
            int end = candidate.Start + candidate.Length;
            if (accepted.Any(existing =>
                    candidate.Start < existing.Start + existing.Length && end > existing.Start))
            {
                continue;
            }

            accepted.Add(candidate);
        }

        return accepted.OrderBy(finding => finding.Start).ToList();
    }

    private static string Redact(string text, IReadOnlyList<DetectedTextSecret> findings)
    {
        if (findings.Count == 0)
        {
            return text;
        }

        var redacted = new StringBuilder(text.Length);
        int cursor = 0;
        foreach (DetectedTextSecret finding in findings)
        {
            redacted.Append(text, cursor, finding.Start - cursor);
            redacted.Append("[REDACTED:").Append(finding.Kind).Append(']');
            cursor = finding.Start + finding.Length;
        }

        redacted.Append(text, cursor, text.Length - cursor);
        return redacted.ToString();
    }

    private sealed record DetectionRule(string Kind, Regex Pattern, string? GroupName = null);
}

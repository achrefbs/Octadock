using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Octadock.WorkflowIntelligence.Internal;

internal static partial class EvidenceExtractor
{
    internal const string Profile = "deterministic-ledger-v2";

    internal static EvidenceLedger Extract(ReconstructedTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        var evidence = new List<EvidenceItem>();
        var deduplicated = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        int sourceWords = 0;

        foreach (ContentSegment segment in turn.Segments)
        {
            sourceWords += CountWords(segment.Text);

            // Raw tool calls/results are infrastructure, not a user-facing result.
            // Error segments remain eligible, and assistant-authored summaries of
            // tool outcomes are still captured from normal assistant prose.
            if (segment.Kind is SegmentKind.ToolInvocation or SegmentKind.ToolResult)
            {
                continue;
            }

            foreach (StatementSlice slice in SplitStatements(segment))
            {
                if (!IsMeaningful(slice.Text))
                {
                    continue;
                }
                EvidenceCategory category = Classify(segment, slice.Text);
                EvidenceImportance importance = Rank(segment, slice.Text, category);
                string normalized = NormalizeStatement(slice.Text);
                string dedupeKey = $"{category}\n{NormalizeForComparison(normalized)}";
                SourceAnchor anchor = segment.Anchor with
                {
                    CharacterStart = slice.Start,
                    CharacterLength = slice.Length,
                    ContentSha256 = HashText(normalized),
                };
                if (deduplicated.TryGetValue(dedupeKey, out int existingIndex))
                {
                    EvidenceItem existing = evidence[existingIndex];
                    evidence[existingIndex] = existing with
                    {
                        Importance = Max(existing.Importance, importance),
                        SourceAnchors = existing.SourceAnchors.Append(anchor).Distinct().ToArray(),
                        Confidence = Math.Max(existing.Confidence, segment.Confidence),
                    };
                    continue;
                }

                string? contradictionGroup = CorrectionMarkerRegex().IsMatch(normalized)
                    ? $"correction:{FirstEntity(normalized) ?? HashText(normalized)[..12]}"
                    : null;
                var item = new EvidenceItem(
                    StableGuid($"{turn.IdentityHash}\n{category}\n{NormalizeForComparison(normalized)}"),
                    turn.DocumentId,
                    category,
                    normalized,
                    importance,
                    segment.Role,
                    segment.Kind,
                    [anchor],
                    segment.Confidence,
                    contradictionGroup,
                    null,
                    ExtractEntities(normalized));
                deduplicated.Add(dedupeKey, evidence.Count);
                evidence.Add(item);
            }
        }

        if (turn.Segments.Count > 0 && evidence.Count == 0)
        {
            warnings.Add("No meaningful evidence could be extracted from the reconstructed segments.");
        }
        return new EvidenceLedger(
            turn.DocumentId,
            Profile,
            evidence,
            turn.Segments.Count,
            sourceWords,
            warnings);
    }

    internal static int CountWords(string text)
        => WordRegex().Matches(text).Count;

    private static IEnumerable<StatementSlice> SplitStatements(ContentSegment segment)
    {
        string text = segment.Text;
        if (segment.Kind is SegmentKind.CodeBlock or SegmentKind.ToolInvocation or SegmentKind.ToolResult or SegmentKind.Error)
        {
            foreach (Match line in NonEmptyLineRegex().Matches(text))
            {
                yield return new StatementSlice(line.Value.Trim(), line.Index, line.Length);
            }
            yield break;
        }

        foreach (Match lineMatch in NonEmptyLineRegex().Matches(text))
        {
            string line = lineMatch.Value;
            int markerLength = MarkdownMarkerRegex().Match(line).Length;
            string content = line[markerLength..].Trim();
            int contentStart = lineMatch.Index + markerLength +
                               (line[markerLength..].Length - line[markerLength..].TrimStart().Length);
            MatchCollection sentences = SentenceRegex().Matches(content);
            if (sentences.Count <= 1)
            {
                yield return new StatementSlice(content, contentStart, content.Length);
                continue;
            }

            foreach (Match sentence in sentences)
            {
                string value = sentence.Value.Trim();
                if (value.Length > 0)
                {
                    int leading = sentence.Value.Length - sentence.Value.TrimStart().Length;
                    yield return new StatementSlice(value, contentStart + sentence.Index + leading, value.Length);
                }
            }
        }
    }

    private static EvidenceCategory Classify(ContentSegment segment, string statement)
    {
        if (PrivacyRegex().IsMatch(statement)) return EvidenceCategory.PrivacySecurityBoundary;
        if (segment.Kind == SegmentKind.Error || ErrorRegex().IsMatch(statement)) return EvidenceCategory.ErrorFailure;
        if (ContradictionRegex().IsMatch(statement)) return EvidenceCategory.DisagreementContradiction;
        if (ConstraintRegex().IsMatch(statement)) return EvidenceCategory.ConstraintNonGoal;
        if (RequirementRegex().IsMatch(statement)) return EvidenceCategory.Requirement;
        if (UnresolvedRegex().IsMatch(statement)) return EvidenceCategory.UnresolvedQuestion;
        if (RiskRegex().IsMatch(statement)) return EvidenceCategory.RiskWarning;
        if (AssumptionRegex().IsMatch(statement)) return EvidenceCategory.Assumption;
        if (CompletedActionRegex().IsMatch(statement)) return EvidenceCategory.CompletedAction;
        if (NextActionRegex().IsMatch(statement)) return EvidenceCategory.ActionNextStep;
        if (DecisionRegex().IsMatch(statement)) return EvidenceCategory.Decision;
        if (OutcomeRegex().IsMatch(statement)) return EvidenceCategory.OutcomeConclusion;
        if (FileReferenceRegex().IsMatch(statement)) return EvidenceCategory.FileCodeReference;
        if (NumberRegex().IsMatch(statement)) return EvidenceCategory.NumberDateVersionThreshold;
        if (ExternalDependencyRegex().IsMatch(statement)) return EvidenceCategory.ExternalDependency;
        if (segment.Role == SegmentRole.User) return EvidenceCategory.UserRequest;
        return EvidenceCategory.Fact;
    }

    private static EvidenceImportance Rank(
        ContentSegment segment,
        string statement,
        EvidenceCategory category)
    {
        if (CriticalModalityRegex().IsMatch(statement))
        {
            return EvidenceImportance.High;
        }
        if (category is EvidenceCategory.PrivacySecurityBoundary or
            EvidenceCategory.ErrorFailure or
            EvidenceCategory.ConstraintNonGoal or
            EvidenceCategory.Requirement)
        {
            return EvidenceImportance.High;
        }
        if (category is EvidenceCategory.Decision or
            EvidenceCategory.CompletedAction or
            EvidenceCategory.ActionNextStep or
            EvidenceCategory.RiskWarning or
            EvidenceCategory.UnresolvedQuestion or
            EvidenceCategory.Assumption or
            EvidenceCategory.DisagreementContradiction or
            EvidenceCategory.FileCodeReference or
            EvidenceCategory.NumberDateVersionThreshold or
            EvidenceCategory.ExternalDependency or
            EvidenceCategory.OutcomeConclusion)
        {
            return EvidenceImportance.Medium;
        }
        return category == EvidenceCategory.UserRequest
            ? EvidenceImportance.Medium
            : EvidenceImportance.Low;
    }

    private static string[] ExtractEntities(string statement)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in FileReferenceRegex().Matches(statement)) values.Add(match.Value);
        foreach (Match match in NumberRegex().Matches(statement)) values.Add(match.Value);
        return values.Take(24).ToArray();
    }

    private static string? FirstEntity(string statement)
    {
        string[] entities = ExtractEntities(statement);
        return entities.Length == 0 ? null : entities[0];
    }

    private static bool IsMeaningful(string value)
        => value.Length >= 4 && WordRegex().Matches(value).Count > 0;

    private static string NormalizeStatement(string value)
        => WhitespaceRegex().Replace(value.Trim(), " ");

    private static string NormalizeForComparison(string value)
        => value.Trim().TrimEnd('.', '!', '?').ToUpperInvariant();

    private static EvidenceImportance Max(EvidenceImportance left, EvidenceImportance right)
        => (EvidenceImportance)Math.Max((int)left, (int)right);

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Guid StableGuid(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record StatementSlice(string Text, int Start, int Length);

    [GeneratedRegex(@"\b[\p{L}\p{N}_-]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"[^\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonEmptyLineRegex();

    [GeneratedRegex(@"^\s*(?:#{1,6}\s+|[-*+]\s+|\d+[.)]\s+|>\s*)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownMarkerRegex();

    [GeneratedRegex(@".+?(?:[.!?](?=\s+[A-Z0-9`#])|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\b(?:do not|don['’]t|never|must not|cannot|can['’]t|without|only|expected to fail|not verified|not supported|out of scope|must|required)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CriticalModalityRegex();

    [GeneratedRegex(@"\b(?:do not|don['’]t|never|must not|cannot|can['’]t|without|only|out of scope|non-goal|leave .* unchanged|avoid)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintRegex();

    [GeneratedRegex(@"\b(?:must|required|requirement|needs? to|has to|acceptance criteria|should guarantee)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequirementRegex();

    [GeneratedRegex(@"(?:\?|\b(?:unknown|unclear|unresolved|open question|not sure|needs? investigation)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnresolvedRegex();

    [GeneratedRegex(@"\b(?:risk|warning|danger|caution|could fail|may fail|fragile|regression)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RiskRegex();

    [GeneratedRegex(@"\b(?:assume|assumption|probably|likely|appears to|seems to|unverified|expected to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssumptionRegex();

    [GeneratedRegex(@"\b(?:implemented|built|fixed|created|removed|updated|changed|completed|finished|passed|verified|tested|added|wrote|shipped|resolved)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompletedActionRegex();

    [GeneratedRegex(@"\b(?:next|todo|remaining|should|need to|plan to|recommend|follow[- ]?up|will implement|action item)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NextActionRegex();

    [GeneratedRegex(@"\b(?:decided|decision|choose|chosen|selected|we will|use .* instead|architecture is|approach is)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DecisionRegex();

    [GeneratedRegex(@"\b(?:result|outcome|conclusion|in summary|overall|succeeded|works|ready|done)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OutcomeRegex();

    [GeneratedRegex("""(?:[A-Za-z]:[\\/][^\s`"'<>|]+|(?:^|\s)(?:\.?\.?[\\/])?[\w.-]+(?:[\\/][\w.@+() -]+)+\.[A-Za-z0-9]{1,10}|\b[\w.-]+\.(?:cs|xaml|ts|tsx|js|jsx|py|rs|go|java|json|jsonl|md|yml|yaml|toml|sql|ps1|csproj)\b)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileReferenceRegex();

    [GeneratedRegex(@"\b(?:v?\d+(?:\.\d+){0,3}|\d+(?:\.\d+)?%|\d{4}-\d{2}-\d{2}|\d+\s*(?:ms|seconds?|minutes?|hours?|days?|kb|mb|gb))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\b(?:api|service|provider|dependency|package|library|server|network|cloud|github|nuget|npm)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalDependencyRegex();

    [GeneratedRegex(@"\b(?:privacy|security|secret|credential|token|password|consent|encryption|egress|external processing|personal data|pii)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PrivacyRegex();

    [GeneratedRegex(@"\b(?:error|failed|failure|exception|crash|invalid|timeout|timed out|did not pass|not working)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorRegex();

    [GeneratedRegex(@"\b(?:actually|instead|correction|contradiction|disagree|not .+ but|supersedes|replaced by)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ContradictionRegex();

    [GeneratedRegex(@"^(?:actually|instead|correction)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CorrectionMarkerRegex();
}

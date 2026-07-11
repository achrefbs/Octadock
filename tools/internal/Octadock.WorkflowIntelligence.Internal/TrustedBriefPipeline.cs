using System.Diagnostics;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class TrustedBriefPipeline
{
    internal static BriefGenerationResult Generate(ReconstructedTurn turn, bool force = false)
        => GenerateAsync(turn, new DeterministicBriefRanker(), force).GetAwaiter().GetResult();

    internal static async Task<BriefGenerationResult> GenerateAsync(
        ReconstructedTurn turn,
        IBriefRanker ranker,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(ranker);
        var stopwatch = Stopwatch.StartNew();
        EvidenceLedger ledger = EvidenceExtractor.Extract(turn);
        BriefEligibility eligibility = EvaluateEligibility(turn, ledger, force);
        if (!eligibility.Eligible)
        {
            stopwatch.Stop();
            return new BriefGenerationResult(eligibility, ledger, null, null, stopwatch.Elapsed);
        }

        BriefLayout layout = await ranker.RankAsync(turn, ledger, cancellationToken).ConfigureAwait(false);
        TrustedBrief draft = TrustedBriefComposer.Compose(turn, ledger, layout, ranker.Profile);
        BriefVerificationResult verification = TrustedBriefVerifier.Verify(ledger, draft);
        BriefStatus status = verification.CriticalFailure
            ? BriefStatus.Withheld
            : verification.Passed && turn.Boundary.Complete && turn.Boundary.Confidence >= 0.95
                ? BriefStatus.Trusted
                : BriefStatus.CoverageWarning;
        var warnings = draft.Warnings
            .Concat(verification.Warnings)
            .Concat(status == BriefStatus.CoverageWarning
                ? ["This result is not an unqualified Trusted Brief; inspect the disclosed coverage or extraction limitation."]
                : [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        TrustedBrief brief = draft with
        {
            Status = status,
            Confidence = status == BriefStatus.Trusted ? Math.Min(draft.Confidence, 0.99) : Math.Min(draft.Confidence, 0.74),
            Warnings = warnings,
        };
        stopwatch.Stop();
        return new BriefGenerationResult(eligibility, ledger, brief, verification, stopwatch.Elapsed);
    }

    private static BriefEligibility EvaluateEligibility(
        ReconstructedTurn turn,
        EvidenceLedger ledger,
        bool force)
    {
        int codeWords = turn.Segments
            .Where(segment => segment.Kind is SegmentKind.CodeBlock or SegmentKind.ToolInvocation or SegmentKind.ToolResult)
            .Sum(segment => EvidenceExtractor.CountWords(segment.Text));
        double codeRatio = ledger.SourceWords == 0 ? 0 : (double)codeWords / ledger.SourceWords;
        if (ledger.Items.Count == 0)
        {
            return new BriefEligibility(false, "No source-linked evidence was extracted.", ledger.SourceWords, codeRatio, force);
        }
        if (force)
        {
            return new BriefEligibility(true, "Forced internal evaluation.", ledger.SourceWords, codeRatio, true);
        }
        if (ledger.SourceWords < 300)
        {
            return new BriefEligibility(false, "The turn is short enough that another summary would add clutter.", ledger.SourceWords, codeRatio, false);
        }
        if (codeRatio >= 0.8 && ledger.Items.Count(item => item.Importance >= EvidenceImportance.High) < 2)
        {
            return new BriefEligibility(false, "The turn is primarily verbatim code/tool output with too little high-value evidence to compress safely.", ledger.SourceWords, codeRatio, false);
        }
        return new BriefEligibility(true, "Long or evidence-dense assistant turn.", ledger.SourceWords, codeRatio, false);
    }
}

internal static class TrustedBriefComposer
{
    internal const string Profile = "verified-evidence-layout-v2";

    internal static TrustedBrief Compose(ReconstructedTurn turn, EvidenceLedger ledger)
    {
        var ranker = new DeterministicBriefRanker();
        BriefLayout layout = ranker.RankAsync(turn, ledger).GetAwaiter().GetResult();
        return Compose(turn, ledger, layout, ranker.Profile);
    }

    internal static TrustedBrief Compose(
        ReconstructedTurn turn,
        EvidenceLedger ledger,
        BriefLayout layout,
        string rankerProfile)
    {
        if (ledger.Items.Count == 0)
        {
            throw new InvalidOperationException("A brief cannot be composed from an empty evidence ledger.");
        }

        Dictionary<Guid, EvidenceItem> byId = ledger.Items.ToDictionary(item => item.Id);
        if (!byId.TryGetValue(layout.HeadlineEvidenceId, out EvidenceItem? headlineEvidence))
        {
            throw new InvalidOperationException("The brief ranker returned an unknown headline evidence ID.");
        }

        var used = new HashSet<Guid> { headlineEvidence.Id };
        BriefItem headline = ToBriefItem(headlineEvidence);
        BriefItem[] mainPoints = Resolve(layout.MainPoints, byId, used, 5);
        BriefItem[] decisions = Resolve(layout.Decisions, byId, used, 8);
        BriefItem[] actions = Resolve(layout.Actions, byId, used, 10);
        BriefItem[] risks = Resolve(layout.RisksAndUnknowns, byId, used, 10);
        BriefItem[] references = Resolve(layout.FilesAndReferences, byId, used, 12);
        // Resolve uncertainty before the catch-all mandatory section so an item
        // that is both important and uncertain is disclosed as uncertainty.
        BriefItem[] mustNotMiss = Resolve(layout.MustNotMiss, byId, used, 12);

        EvidenceItem[] uncoveredHigh = ledger.Items
            .Where(item => item.Importance >= EvidenceImportance.High && !used.Contains(item.Id))
            .ToArray();
        if (uncoveredHigh.Length > 0)
        {
            mustNotMiss = mustNotMiss.Concat(uncoveredHigh.Select(ToBriefItem)).ToArray();
            foreach (EvidenceItem item in uncoveredHigh) used.Add(item.Id);
        }

        int briefWords = AllItems(headline, mainPoints, mustNotMiss, decisions, actions, risks, references)
            .DistinctBy(item => item.EvidenceId)
            .Sum(item => EvidenceExtractor.CountWords(item.Text));
        double coverage = ledger.Items.Count == 0 ? 0 : (double)used.Count / ledger.Items.Count;
        var warnings = turn.Boundary.Warnings.Concat(ledger.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        return new TrustedBrief(
            Guid.NewGuid(),
            turn.DocumentId,
            BriefStatus.CoverageWarning,
            headline,
            ReadMinutes(ledger.SourceWords),
            ReadMinutes(briefWords),
            mainPoints,
            mustNotMiss,
            decisions,
            actions,
            risks,
            references,
            coverage,
            turn.Boundary.Complete,
            Math.Min(turn.Boundary.Confidence, 0.99),
            warnings,
            $"{Profile}+{rankerProfile}",
            DateTimeOffset.UtcNow);
    }

    private static BriefItem[] Resolve(
        IReadOnlyList<Guid> requestedIds,
        Dictionary<Guid, EvidenceItem> evidence,
        HashSet<Guid> used,
        int maximum)
    {
        Guid[] unknown = requestedIds.Where(id => !evidence.ContainsKey(id)).Distinct().ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"The brief ranker returned {unknown.Length} unknown evidence ID(s).");
        }

        EvidenceItem[] selected = requestedIds
            .Distinct()
            .Where(id => !used.Contains(id))
            .Take(maximum)
            .Select(id => evidence[id])
            .ToArray();
        foreach (EvidenceItem item in selected) used.Add(item.Id);
        return selected.Select(ToBriefItem).ToArray();
    }

    private static BriefItem ToBriefItem(EvidenceItem item)
        => new(item.Id, item.Category, item.NormalizedStatement, item.Importance, item.SourceAnchors);

    private static IEnumerable<BriefItem> AllItems(
        BriefItem headline,
        params IReadOnlyList<BriefItem>[] sections)
        => new[] { headline }.Concat(sections.SelectMany(section => section));

    private static double ReadMinutes(int words)
        => Math.Round(words / 220d, 1, MidpointRounding.AwayFromZero);
}

internal static class TrustedBriefVerifier
{
    internal const string Profile = "deterministic-faithfulness-verifier-v1";

    internal static BriefVerificationResult Verify(EvidenceLedger ledger, TrustedBrief brief)
    {
        Dictionary<Guid, EvidenceItem> evidence = ledger.Items.ToDictionary(item => item.Id);
        BriefItem[] displayed = Enumerate(brief).ToArray();
        Guid[] unsupported = displayed
            .Where(item => !evidence.TryGetValue(item.EvidenceId, out EvidenceItem? source) ||
                           !string.Equals(item.Text, source.NormalizedStatement, StringComparison.Ordinal) ||
                           item.SourceAnchors.Count == 0)
            .Select(item => item.EvidenceId)
            .Distinct()
            .ToArray();
        HashSet<Guid> represented = displayed.Select(item => item.EvidenceId).ToHashSet();
        EvidenceItem[] high = ledger.Items.Where(item => item.Importance >= EvidenceImportance.High).ToArray();
        Guid[] omitted = high.Where(item => !represented.Contains(item.Id)).Select(item => item.Id).ToArray();
        HashSet<Guid> riskIds = brief.RisksAndUnknowns.Select(item => item.EvidenceId).ToHashSet();
        Guid[] misplacedUncertainty = ledger.Items
            .Where(item => item.Category is EvidenceCategory.Assumption or EvidenceCategory.UnresolvedQuestion)
            .Where(item => represented.Contains(item.Id) && !riskIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToArray();
        bool criticalFailure = high.Any(item => omitted.Contains(item.Id) && item.Importance == EvidenceImportance.Critical) ||
                               displayed.Any(item => unsupported.Contains(item.EvidenceId) && item.Importance >= EvidenceImportance.High);
        double highRecall = high.Length == 0 ? 1 : (double)(high.Length - omitted.Length) / high.Length;
        double coverage = ledger.Items.Count == 0 ? 0 : (double)represented.Count(id => evidence.ContainsKey(id)) / ledger.Items.Count;
        var warnings = new List<string>();
        if (unsupported.Length > 0) warnings.Add($"{unsupported.Length} displayed claim(s) lack exact ledger support.");
        if (omitted.Length > 0) warnings.Add($"{omitted.Length} high-importance evidence item(s) were omitted.");
        if (misplacedUncertainty.Length > 0) warnings.Add($"{misplacedUncertainty.Length} uncertain item(s) were presented outside Risks and unknowns.");
        bool passed = unsupported.Length == 0 && omitted.Length == 0 && misplacedUncertainty.Length == 0;
        return new BriefVerificationResult(
            passed,
            criticalFailure,
            highRecall,
            coverage,
            omitted,
            unsupported,
            misplacedUncertainty,
            warnings,
            Profile);
    }

    private static IEnumerable<BriefItem> Enumerate(TrustedBrief brief)
        => new[] { brief.Headline }
            .Concat(brief.MainPoints)
            .Concat(brief.MustNotMiss)
            .Concat(brief.Decisions)
            .Concat(brief.Actions)
            .Concat(brief.RisksAndUnknowns)
            .Concat(brief.FilesAndReferences);
}

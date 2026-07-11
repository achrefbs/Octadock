using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class TrustedBriefPipelineTests
{
    [Fact]
    public async Task Pipeline_promotes_negations_keeps_uncertainty_visible_and_anchors_every_claim()
    {
        ReconstructedTurn turn = await BuildClaudeTurnAsync(
            """
            Implemented the parser in src/Parser.cs.
            Do not change the public application or its screenshots.
            The integration test is expected to fail until API v2.1 is available.
            Assumption: the service likely returns cached data.
            Next, run 25 adversarial cases.
            """,
            complete: true);

        BriefGenerationResult result = TrustedBriefPipeline.Generate(turn, force: true);

        result.Brief.Should().NotBeNull();
        result.Brief!.Status.Should().Be(BriefStatus.Trusted);
        result.Verification!.Passed.Should().BeTrue();
        result.Verification.LedgerHighImportanceRecall.Should().Be(1);
        EvidenceItem constraint = result.Ledger.Items.Single(item => item.NormalizedStatement.StartsWith("Do not", StringComparison.Ordinal));
        constraint.Importance.Should().Be(EvidenceImportance.High);
        result.Brief.MustNotMiss.Should().Contain(item => item.EvidenceId == constraint.Id);
        result.Brief.RisksAndUnknowns.Should().Contain(item => item.Text.Contains("expected to fail", StringComparison.Ordinal));
        result.Brief.RisksAndUnknowns.Should().Contain(item => item.Text.StartsWith("Assumption", StringComparison.Ordinal));
        Enumerate(result.Brief).Should().OnlyContain(item => item.SourceAnchors.Count > 0);
    }

    [Fact]
    public async Task Independent_verifier_detects_a_seeded_high_importance_omission()
    {
        ReconstructedTurn turn = await BuildClaudeTurnAsync(
            "Implemented the parser. Do not modify the public API. The tests passed.",
            complete: true);
        EvidenceLedger ledger = EvidenceExtractor.Extract(turn);
        TrustedBrief draft = TrustedBriefComposer.Compose(turn, ledger);
        EvidenceItem target = ledger.Items.Single(item => item.NormalizedStatement.StartsWith("Do not modify", StringComparison.Ordinal));
        BriefItem replacementHeadline = Enumerate(draft).First(item => item.EvidenceId != target.Id);
        TrustedBrief corrupted = draft with
        {
            Headline = draft.Headline.EvidenceId == target.Id
                ? replacementHeadline
                : draft.Headline,
            MainPoints = draft.MainPoints.Where(item => item.EvidenceId != target.Id).ToArray(),
            MustNotMiss = draft.MustNotMiss.Where(item => item.EvidenceId != target.Id).ToArray(),
            Decisions = draft.Decisions.Where(item => item.EvidenceId != target.Id).ToArray(),
            Actions = draft.Actions.Where(item => item.EvidenceId != target.Id).ToArray(),
            RisksAndUnknowns = draft.RisksAndUnknowns.Where(item => item.EvidenceId != target.Id).ToArray(),
            FilesAndReferences = draft.FilesAndReferences.Where(item => item.EvidenceId != target.Id).ToArray(),
        };

        BriefVerificationResult verification = TrustedBriefVerifier.Verify(ledger, corrupted);

        verification.Passed.Should().BeFalse();
        verification.OmittedHighImportanceEvidenceIds.Should().Contain(target.Id);
        verification.LedgerHighImportanceRecall.Should().BeLessThan(1);
    }

    [Fact]
    public async Task Incomplete_turn_is_downgraded_even_when_ledger_verification_passes()
    {
        ReconstructedTurn turn = await BuildClaudeTurnAsync(
            "Implemented the parser. Do not modify the public API.",
            complete: false);

        BriefGenerationResult result = TrustedBriefPipeline.Generate(turn, force: true);

        result.Verification!.Passed.Should().BeTrue();
        result.Brief!.Status.Should().Be(BriefStatus.CoverageWarning);
        result.Brief.Warnings.Should().Contain(warning => warning.Contains("not an unqualified Trusted Brief", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Short_turn_is_suppressed_without_forcing_a_new_summary()
    {
        ReconstructedTurn turn = await BuildClaudeTurnAsync("Done. The test passed.", complete: true);

        BriefGenerationResult result = TrustedBriefPipeline.Generate(turn);

        result.Eligibility.Eligible.Should().BeFalse();
        result.Brief.Should().BeNull();
        result.Eligibility.Reason.Should().Contain("short enough");
    }

    [Fact]
    public async Task Source_anchor_policy_revalidates_original_bytes_and_downgrades_after_mutation()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "anchored.jsonl");
        string user = "{\"type\":\"user\",\"uuid\":\"u1\",\"sessionId\":\"s1\",\"message\":{\"role\":\"user\",\"content\":\"Keep the public API unchanged.\"}}";
        string assistant = "{\"type\":\"assistant\",\"uuid\":\"a1\",\"sessionId\":\"s1\",\"stop_reason\":\"end_turn\",\"message\":{\"role\":\"assistant\",\"content\":\"Implemented the parser. Do not modify the public API.\"}}";
        await File.WriteAllLinesAsync(file, [user, assistant]);
        ReconstructedTurn turn = (await TurnReconstructor.ReconstructLatestAsync(ProviderKind.Claude, file)).Turn!;
        TrustedBrief brief = TrustedBriefPipeline.Generate(turn, force: true).Brief!;

        SourceAnchorValidationResult before = await SourceAnchorValidator.ValidateAsync(file, turn, brief);
        before.SuccessRate.Should().Be(1);

        await File.WriteAllLinesAsync(file, [user, assistant.Replace("parser", "reader", StringComparison.Ordinal)]);
        SourceAnchorValidationResult after = await SourceAnchorValidator.ValidateAsync(file, turn, brief);
        TrustedBrief policyChecked = SourceAnchorValidator.ApplyPolicy(brief, after);

        after.SuccessRate.Should().BeLessThan(1);
        policyChecked.Status.Should().NotBe(BriefStatus.Trusted);
    }

    [Fact]
    public async Task Model_layout_can_only_select_existing_immutable_evidence_ids()
    {
        ReconstructedTurn turn = await BuildClaudeTurnAsync(
            "Implemented the capture fix. Do not change the public API. Next, run the regression suite.",
            complete: true);
        EvidenceLedger ledger = EvidenceExtractor.Extract(turn);
        Guid headline = ledger.Items[0].Id;
        string valid = $$"""
            {
              "headlineEvidenceId": "{{headline}}",
              "mainPoints": [],
              "mustNotMiss": [],
              "decisions": [],
              "actions": [],
              "risksAndUnknowns": [],
              "filesAndReferences": []
            }
            """;

        BriefLayout parsed = CliBriefRanker.ParseLayout(valid, ledger.Items);
        parsed.HeadlineEvidenceId.Should().Be(headline);

        string invalid = valid.Replace(headline.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
        Action act = () => CliBriefRanker.ParseLayout(invalid, ledger.Items);
        act.Should().Throw<InvalidOperationException>().WithMessage("*outside*candidate set*");
    }

    private static async Task<ReconstructedTurn> BuildClaudeTurnAsync(string assistantText, bool complete)
    {
        var directory = new TestDirectory();
        try
        {
            string file = Path.Combine(directory.Path, "session.jsonl");
            var lines = new List<string>
            {
                "{\"type\":\"user\",\"uuid\":\"u1\",\"sessionId\":\"s1\",\"message\":{\"role\":\"user\",\"content\":\"Give me the implementation result without breaking existing behavior.\"}}",
            };
            string escaped = System.Text.Json.JsonSerializer.Serialize(assistantText);
            lines.Add($"{{\"type\":\"assistant\",\"uuid\":\"a1\",\"sessionId\":\"s1\",{(complete ? "\"stop_reason\":\"end_turn\"," : string.Empty)}\"message\":{{\"role\":\"assistant\",\"content\":{escaped}}}}}");
            await File.WriteAllLinesAsync(file, lines);
            return (await TurnReconstructor.ReconstructLatestAsync(ProviderKind.Claude, file)).Turn!;
        }
        finally
        {
            directory.Dispose();
        }
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

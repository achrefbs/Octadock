using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class BriefEvaluationRunner
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    internal static async Task<BriefEvaluationReport> EvaluateAsync(
        ProviderKind provider,
        string root,
        int maximumFiles,
        bool force,
        IBriefRanker? ranker = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFiles);
        ranker ??= new DeterministicBriefRanker();
        string fullRoot = Path.GetFullPath(root);
        string[] files = Directory.Exists(fullRoot)
            ? Directory.EnumerateFiles(fullRoot, "*.jsonl", Enumeration)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(maximumFiles)
                .Select(file => file.FullName)
                .ToArray()
            : [];
        int reconstructed = 0;
        int complete = 0;
        int eligible = 0;
        int suppressed = 0;
        int trusted = 0;
        int coverageWarnings = 0;
        int withheld = 0;
        int parseErrors = 0;
        int highEvidence = 0;
        int omittedHigh = 0;
        int unsupported = 0;
        int anchorsChecked = 0;
        int anchorFailures = 0;
        var latencies = new List<double>(files.Length);
        var compressionRatios = new List<double>(files.Length);
        var warnings = new List<string>();

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                TurnReconstructionResult reconstruction = await TurnReconstructor.ReconstructLatestAsync(
                    provider,
                    file,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                parseErrors += reconstruction.ParseErrors;
                if (reconstruction.Turn is not { } turn)
                {
                    continue;
                }
                reconstructed++;
                if (turn.Boundary.Complete) complete++;
                BriefGenerationResult result = await TrustedBriefPipeline.GenerateAsync(
                    turn,
                    ranker,
                    force,
                    cancellationToken).ConfigureAwait(false);
                latencies.Add(reconstruction.Duration.TotalMilliseconds + result.Duration.TotalMilliseconds);
                highEvidence += result.Ledger.Items.Count(item => item.Importance >= EvidenceImportance.High);
                if (!result.Eligibility.Eligible)
                {
                    suppressed++;
                    continue;
                }
                eligible++;
                if (result.Brief is { } brief)
                {
                    SourceAnchorValidationResult anchorValidation = await SourceAnchorValidator.ValidateAsync(
                        file,
                        turn,
                        brief,
                        cancellationToken).ConfigureAwait(false);
                    brief = SourceAnchorValidator.ApplyPolicy(brief, anchorValidation);
                    switch (brief.Status)
                    {
                        case BriefStatus.Trusted: trusted++; break;
                        case BriefStatus.CoverageWarning: coverageWarnings++; break;
                        case BriefStatus.Withheld: withheld++; break;
                    }
                    int briefWords = EnumerateBriefItems(brief)
                        .DistinctBy(item => item.EvidenceId)
                        .Sum(item => EvidenceExtractor.CountWords(item.Text));
                    if (briefWords > 0)
                    {
                        compressionRatios.Add((double)result.Ledger.SourceWords / briefWords);
                    }
                    anchorsChecked += anchorValidation.AnchorsChecked;
                    anchorFailures += anchorValidation.AnchorsChecked - anchorValidation.AnchorsValid;
                }
                if (result.Verification is { } verification)
                {
                    omittedHigh += verification.OmittedHighImportanceEvidenceIds.Count;
                    unsupported += verification.UnsupportedEvidenceIds.Count;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                warnings.Add($"One {provider} file could not be evaluated: {exception.GetType().Name}.");
            }
        }

        latencies.Sort();
        double recall = highEvidence == 0 ? 1 : (double)(highEvidence - omittedHigh) / highEvidence;
        return new BriefEvaluationReport(
            provider,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullRoot.ToUpperInvariant()))),
            maximumFiles,
            files.Length,
            reconstructed,
            complete,
            eligible,
            suppressed,
            trusted,
            coverageWarnings,
            withheld,
            parseErrors,
            highEvidence,
            omittedHigh,
            unsupported,
            anchorsChecked,
            anchorFailures,
            anchorsChecked == 0 ? 0 : (double)(anchorsChecked - anchorFailures) / anchorsChecked,
            recall,
            compressionRatios.Count == 0 ? 0 : compressionRatios.Average(),
            Percentile(latencies, 0.5),
            Percentile(latencies, 0.95),
            latencies.Count == 0 ? 0 : latencies[^1],
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<BriefItem> EnumerateBriefItems(TrustedBrief brief)
        => new[] { brief.Headline }
            .Concat(brief.MainPoints)
            .Concat(brief.MustNotMiss)
            .Concat(brief.Decisions)
            .Concat(brief.Actions)
            .Concat(brief.RisksAndUnknowns)
            .Concat(brief.FilesAndReferences);

    private static double Percentile(IReadOnlyList<double> ordered, double percentile)
    {
        if (ordered.Count == 0) return 0;
        int index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }
}

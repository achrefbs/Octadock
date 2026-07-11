using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Octadock.WorkflowIntelligence.Internal;

internal static partial class SourceAnchorValidator
{
    internal static TrustedBrief ApplyPolicy(
        TrustedBrief brief,
        SourceAnchorValidationResult validation)
    {
        if (validation.AnchorsChecked > 0 && validation.AnchorsValid == validation.AnchorsChecked)
        {
            return brief;
        }
        BriefStatus status = validation.AnchorsValid == 0 ? BriefStatus.Withheld : BriefStatus.CoverageWarning;
        return brief with
        {
            Status = status,
            Confidence = Math.Min(brief.Confidence, status == BriefStatus.Withheld ? 0.25 : 0.6),
            Warnings = brief.Warnings
                .Concat(validation.Warnings)
                .Append("One or more displayed claims cannot navigate back to unchanged source evidence.")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        };
    }

    internal static async Task<SourceAnchorValidationResult> ValidateAsync(
        string path,
        ReconstructedTurn turn,
        TrustedBrief brief,
        CancellationToken cancellationToken = default)
    {
        SourceAnchor[] anchors = EnumerateBriefItems(brief)
            .SelectMany(item => item.SourceAnchors)
            .Distinct()
            .ToArray();
        var segments = turn.Segments
            .GroupBy(segment => SegmentKey(segment.Anchor))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var recordValidity = new Dictionary<string, bool>(StringComparer.Ordinal);
        int recordFailures = 0;
        int rangeFailures = 0;
        var warnings = new List<string>();
        string fullPath = Path.GetFullPath(path);
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

        foreach (SourceAnchor anchor in anchors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string recordKey = $"{anchor.ByteStart}:{anchor.ByteEndExclusive}:{anchor.RecordSha256}";
            if (!recordValidity.TryGetValue(recordKey, out bool recordValid))
            {
                recordValid = await ValidateRecordHashAsync(stream, anchor, cancellationToken).ConfigureAwait(false);
                recordValidity.Add(recordKey, recordValid);
            }
            if (!recordValid)
            {
                recordFailures++;
                continue;
            }

            if (!segments.TryGetValue(SegmentKey(anchor), out ContentSegment? segment) ||
                !string.Equals(segment.NormalizedHash, anchor.SegmentContentSha256, StringComparison.Ordinal) ||
                anchor.CharacterStart < 0 ||
                anchor.CharacterLength < 0 ||
                anchor.CharacterStart + anchor.CharacterLength > segment.Text.Length)
            {
                rangeFailures++;
                continue;
            }
            string anchoredText = segment.Text.Substring(anchor.CharacterStart, anchor.CharacterLength);
            string normalized = WhitespaceRegex().Replace(anchoredText.Trim(), " ");
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
            if (!string.Equals(hash, anchor.ContentSha256, StringComparison.Ordinal))
            {
                rangeFailures++;
            }
        }

        int valid = anchors.Length - recordFailures - rangeFailures;
        if (recordFailures > 0) warnings.Add($"{recordFailures} anchor(s) no longer match the original JSONL record bytes.");
        if (rangeFailures > 0) warnings.Add($"{rangeFailures} anchor(s) no longer resolve to the displayed evidence text.");
        return new SourceAnchorValidationResult(
            anchors.Length,
            Math.Max(0, valid),
            recordFailures,
            rangeFailures,
            anchors.Length == 0 ? 0 : (double)Math.Max(0, valid) / anchors.Length,
            warnings);
    }

    private static async Task<bool> ValidateRecordHashAsync(
        FileStream stream,
        SourceAnchor anchor,
        CancellationToken cancellationToken)
    {
        long length = anchor.ByteEndExclusive - anchor.ByteStart;
        if (length < 0 || length > 32L * 1024 * 1024 || anchor.ByteEndExclusive > stream.Length)
        {
            return false;
        }
        byte[] buffer = new byte[(int)length];
        stream.Seek(anchor.ByteStart, SeekOrigin.Begin);
        int read = 0;
        while (read < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0) return false;
            read += count;
        }
        string hash = Convert.ToHexString(SHA256.HashData(buffer));
        return string.Equals(hash, anchor.RecordSha256, StringComparison.Ordinal);
    }

    private static string SegmentKey(SourceAnchor anchor)
        => $"{anchor.RecordOrdinal}\n{anchor.JsonPointer}\n{anchor.SegmentContentSha256}";

    private static IEnumerable<BriefItem> EnumerateBriefItems(TrustedBrief brief)
        => new[] { brief.Headline }
            .Concat(brief.MainPoints)
            .Concat(brief.MustNotMiss)
            .Concat(brief.Decisions)
            .Concat(brief.Actions)
            .Concat(brief.RisksAndUnknowns)
            .Concat(brief.FilesAndReferences);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

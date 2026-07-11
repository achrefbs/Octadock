namespace Octadock.WorkflowIntelligence.Internal;

internal enum SegmentKind
{
    Unknown = 0,
    UserMessage,
    AssistantMessage,
    Heading,
    Paragraph,
    ListItem,
    CodeBlock,
    ToolInvocation,
    ToolResult,
    Warning,
    Error,
    Status,
    FileReference,
}

internal enum SegmentRole
{
    Unknown = 0,
    User,
    Assistant,
    Tool,
    System,
}

internal enum EvidenceCategory
{
    OutcomeConclusion = 0,
    UserRequest,
    Requirement,
    ConstraintNonGoal,
    Decision,
    ActionNextStep,
    CompletedAction,
    RiskWarning,
    ErrorFailure,
    UnresolvedQuestion,
    Assumption,
    FileCodeReference,
    NumberDateVersionThreshold,
    ExternalDependency,
    PrivacySecurityBoundary,
    DisagreementContradiction,
    Fact,
}

internal enum EvidenceImportance
{
    Low = 0,
    Medium,
    High,
    Critical,
}

internal enum BriefStatus
{
    Suppressed = 0,
    CoverageWarning,
    Trusted,
    Withheld,
}

internal sealed record SourceAnchor(
    ProviderKind Provider,
    string FilePathSha256,
    long RecordOrdinal,
    long ByteStart,
    long ByteEndExclusive,
    string? RecordId,
    string JsonPointer,
    int CharacterStart,
    int CharacterLength,
    string RecordSha256,
    string SegmentContentSha256,
    string ContentSha256);

internal sealed record ContentSegment(
    Guid Id,
    Guid DocumentId,
    int Ordinal,
    SegmentKind Kind,
    SegmentRole Role,
    string Text,
    string NormalizedHash,
    SourceAnchor Anchor,
    double Confidence,
    IReadOnlyDictionary<string, string> Attributes);

internal sealed record BoundaryResult(
    Guid DocumentId,
    string LogicalUnitKind,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool Complete,
    double Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> MissingCapabilities,
    IReadOnlyList<string> Warnings);

internal sealed record ReconstructedTurn(
    Guid DocumentId,
    ProviderKind Provider,
    string IdentityHash,
    string FilePathSha256,
    string? SessionId,
    string? TurnId,
    string? PromptId,
    string? WorkingDirectory,
    string? GitBranch,
    string? Model,
    BoundaryResult Boundary,
    IReadOnlyList<ContentSegment> Segments,
    int RecordsParsed,
    int ParseErrors,
    long SourceBytesRead,
    TimeSpan ReconstructionDuration,
    IReadOnlyList<string> Warnings);

internal sealed record TurnReconstructionResult(
    ReconstructedTurn? Turn,
    int TurnsObserved,
    int CompleteTurnsObserved,
    int RecordsParsed,
    int ParseErrors,
    long SourceBytesRead,
    TimeSpan Duration,
    IReadOnlyList<string> Warnings);

internal sealed record EvidenceItem(
    Guid Id,
    Guid DocumentId,
    EvidenceCategory Category,
    string NormalizedStatement,
    EvidenceImportance Importance,
    SegmentRole SourceRole,
    SegmentKind SourceKind,
    IReadOnlyList<SourceAnchor> SourceAnchors,
    double Confidence,
    string? ContradictionGroup,
    Guid? SupersedesItemId,
    IReadOnlyList<string> EntityReferences);

/// <summary>
/// A model may arrange immutable evidence IDs, but it never writes the brief's
/// claims. The deterministic composer resolves every ID back to exact ledger text.
/// </summary>
internal sealed record BriefLayout(
    Guid HeadlineEvidenceId,
    IReadOnlyList<Guid> MainPoints,
    IReadOnlyList<Guid> MustNotMiss,
    IReadOnlyList<Guid> Decisions,
    IReadOnlyList<Guid> Actions,
    IReadOnlyList<Guid> RisksAndUnknowns,
    IReadOnlyList<Guid> FilesAndReferences);

internal interface IBriefRanker
{
    string Profile { get; }

    Task<BriefLayout> RankAsync(
        ReconstructedTurn turn,
        EvidenceLedger ledger,
        CancellationToken cancellationToken = default);
}

internal sealed record EvidenceLedger(
    Guid DocumentId,
    string ExtractorProfile,
    IReadOnlyList<EvidenceItem> Items,
    int SourceSegments,
    int SourceWords,
    IReadOnlyList<string> Warnings);

internal sealed record BriefItem(
    Guid EvidenceId,
    EvidenceCategory Category,
    string Text,
    EvidenceImportance Importance,
    IReadOnlyList<SourceAnchor> SourceAnchors);

internal sealed record TrustedBrief(
    Guid Id,
    Guid DocumentId,
    BriefStatus Status,
    BriefItem Headline,
    double EstimatedOriginalReadMinutes,
    double EstimatedBriefReadMinutes,
    IReadOnlyList<BriefItem> MainPoints,
    IReadOnlyList<BriefItem> MustNotMiss,
    IReadOnlyList<BriefItem> Decisions,
    IReadOnlyList<BriefItem> Actions,
    IReadOnlyList<BriefItem> RisksAndUnknowns,
    IReadOnlyList<BriefItem> FilesAndReferences,
    double SourceCoverage,
    bool ExtractionComplete,
    double Confidence,
    IReadOnlyList<string> Warnings,
    string ComposerProfile,
    DateTimeOffset CreatedAt);

internal sealed record BriefVerificationResult(
    bool Passed,
    bool CriticalFailure,
    double LedgerHighImportanceRecall,
    double EvidenceCoverage,
    IReadOnlyList<Guid> OmittedHighImportanceEvidenceIds,
    IReadOnlyList<Guid> UnsupportedEvidenceIds,
    IReadOnlyList<Guid> MisplacedUncertaintyEvidenceIds,
    IReadOnlyList<string> Warnings,
    string VerifierProfile);

internal sealed record BriefEligibility(
    bool Eligible,
    string Reason,
    int SourceWords,
    double CodeRatio,
    bool Forced);

internal sealed record BriefGenerationResult(
    BriefEligibility Eligibility,
    EvidenceLedger Ledger,
    TrustedBrief? Brief,
    BriefVerificationResult? Verification,
    TimeSpan Duration);

internal sealed record SourceAnchorValidationResult(
    int AnchorsChecked,
    int AnchorsValid,
    int RecordHashFailures,
    int SegmentRangeFailures,
    double SuccessRate,
    IReadOnlyList<string> Warnings);

internal sealed record StoredTrustedBrief(
    string Schema,
    ProviderKind Provider,
    string IdentityHash,
    string? TurnId,
    TrustedBrief Brief,
    BriefVerificationResult Verification,
    SourceAnchorValidationResult SourceAnchorValidation,
    DateTimeOffset StoredAt);

internal sealed record BriefEvaluationReport(
    ProviderKind Provider,
    string RootPathSha256,
    int FilesRequested,
    int FilesEvaluated,
    int TurnsReconstructed,
    int CompleteTurns,
    int EligibleTurns,
    int SuppressedTurns,
    int TrustedBriefs,
    int CoverageWarnings,
    int WithheldBriefs,
    int ParseErrors,
    int HighImportanceEvidence,
    int OmittedHighImportanceEvidence,
    int UnsupportedClaims,
    int SourceAnchorsChecked,
    int SourceAnchorFailures,
    double SourceNavigationSuccessRate,
    double LedgerHighImportanceRecall,
    double MeanCompressionRatio,
    double P50LatencyMilliseconds,
    double P95LatencyMilliseconds,
    double MaximumLatencyMilliseconds,
    IReadOnlyList<string> Warnings);

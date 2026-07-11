using System.Collections.ObjectModel;

namespace Octadock.Core.Ai;

/// <summary>The provider-neutral source types understood by an Agent Packet.</summary>
public enum AgentPacketSourceKind
{
    Text = 0,
    Image,
    File,
    Context,
    Capture,
    VisualDiff,
}

/// <summary>Where an Agent Packet source originated.</summary>
public enum AgentPacketProvenanceKind
{
    UserProvided = 0,
    Clipboard,
    Ocr,
    Dictation,
    ScreenCapture,
    ContextPackage,
    FileSystem,
    VisualVerification,
}

/// <summary>Stable role names used by asset-bearing source items.</summary>
public static class AgentPacketAssetRoles
{
    public const string Image = "image";
    public const string File = "file";
    public const string Capture = "capture";
    public const string Before = "before";
    public const string After = "after";
    public const string Diff = "diff";
}

/// <summary>Hard limits applied before a reviewed packet can be created.</summary>
public static class AgentPacketLimits
{
    public const int MaxSources = 32;
    public const int MaxAcceptanceCriteria = 32;
    public const int MaxAssetsPerSource = 3;
    public const int MaxSourceCharacters = 120_000;
    public const int MaxTotalSourceCharacters = 480_000;
    public const int MaxOutboundCharacters = 750_000;
    public const int MaxIdentifierCharacters = 64;
    public const int MaxLabelCharacters = 160;
    public const int MaxTitleCharacters = 200;
    public const int MaxGoalCharacters = 8_000;
    public const int MaxCriterionCharacters = 1_000;
    public const int MaxVerificationHintCharacters = 1_000;
    public const int MaxAssetPathCharacters = 240;
    public const int MaxProvenanceFieldCharacters = 240;
    public const int MaxPixelDimension = 100_000;
}

/// <summary>Trusted task metadata supplied by the user or an Octadock workflow.</summary>
public sealed record AgentPacketMetadata
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Goal { get; init; }

    /// <remarks>
    /// The builder normalizes this value to UTC. It never inserts the current
    /// time, which keeps output deterministic and reviewable.
    /// </remarks>
    public required DateTimeOffset CreatedAt { get; init; }

    public string? ProjectName { get; init; }

    public string? TargetApplication { get; init; }

    public string? Environment { get; init; }
}

/// <summary>Safe provenance metadata for one source item.</summary>
public sealed record AgentPacketProvenance
{
    public required AgentPacketProvenanceKind Kind { get; init; }

    public string? ApplicationName { get; init; }

    public string? WindowTitle { get; init; }

    public string? Reference { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }
}

/// <summary>
/// A relative, export-bundle asset reference. The builder rejects absolute,
/// URI, and traversal paths so local machine paths cannot leak accidentally.
/// </summary>
public sealed record AgentPacketAssetReference
{
    public required string Role { get; init; }

    public required string RelativePath { get; init; }

    public string? MediaType { get; init; }

    public string? Sha256 { get; init; }

    public int? PixelWidth { get; init; }

    public int? PixelHeight { get; init; }
}

/// <summary>
/// One source supplied to an agent. <see cref="TextContent"/> is always treated
/// as untrusted data, including OCR and visual-diff summaries.
/// </summary>
public sealed record AgentPacketSourceItem
{
    public required string Id { get; init; }

    public required AgentPacketSourceKind Kind { get; init; }

    public required string Label { get; init; }

    public required AgentPacketProvenance Provenance { get; init; }

    public string? TextContent { get; init; }

    public IReadOnlyList<AgentPacketAssetReference> Assets { get; init; } =
        Array.Empty<AgentPacketAssetReference>();

    public static AgentPacketSourceItem CreateText(
        string id,
        string label,
        string text,
        AgentPacketProvenance provenance) =>
        CreateTextual(AgentPacketSourceKind.Text, id, label, text, provenance);

    public static AgentPacketSourceItem CreateContext(
        string id,
        string label,
        string text,
        AgentPacketProvenance provenance) =>
        CreateTextual(AgentPacketSourceKind.Context, id, label, text, provenance);

    public static AgentPacketSourceItem CreateImage(
        string id,
        string label,
        string relativePath,
        AgentPacketProvenance provenance,
        string? extractedText = null,
        string? mediaType = "image/png") =>
        CreateAsset(
            AgentPacketSourceKind.Image,
            id,
            label,
            AgentPacketAssetRoles.Image,
            relativePath,
            provenance,
            extractedText,
            mediaType);

    public static AgentPacketSourceItem CreateFile(
        string id,
        string label,
        string relativePath,
        AgentPacketProvenance provenance,
        string? extractedText = null,
        string? mediaType = null) =>
        CreateAsset(
            AgentPacketSourceKind.File,
            id,
            label,
            AgentPacketAssetRoles.File,
            relativePath,
            provenance,
            extractedText,
            mediaType);

    public static AgentPacketSourceItem CreateCapture(
        string id,
        string label,
        string relativePath,
        AgentPacketProvenance provenance,
        string? ocrText = null,
        string? mediaType = "image/png") =>
        CreateAsset(
            AgentPacketSourceKind.Capture,
            id,
            label,
            AgentPacketAssetRoles.Capture,
            relativePath,
            provenance,
            ocrText,
            mediaType);

    public static AgentPacketSourceItem CreateVisualDiff(
        string id,
        string label,
        string beforeRelativePath,
        string afterRelativePath,
        AgentPacketProvenance provenance,
        string? diffRelativePath = null,
        string? summary = null)
    {
        var assets = new List<AgentPacketAssetReference>
        {
            new()
            {
                Role = AgentPacketAssetRoles.Before,
                RelativePath = beforeRelativePath,
                MediaType = "image/png",
            },
            new()
            {
                Role = AgentPacketAssetRoles.After,
                RelativePath = afterRelativePath,
                MediaType = "image/png",
            },
        };

        if (diffRelativePath is not null)
        {
            assets.Add(new AgentPacketAssetReference
            {
                Role = AgentPacketAssetRoles.Diff,
                RelativePath = diffRelativePath,
                MediaType = "image/png",
            });
        }

        return new AgentPacketSourceItem
        {
            Id = id,
            Kind = AgentPacketSourceKind.VisualDiff,
            Label = label,
            Provenance = provenance,
            TextContent = summary,
            Assets = new ReadOnlyCollection<AgentPacketAssetReference>(assets),
        };
    }

    private static AgentPacketSourceItem CreateTextual(
        AgentPacketSourceKind kind,
        string id,
        string label,
        string text,
        AgentPacketProvenance provenance) =>
        new()
        {
            Id = id,
            Kind = kind,
            Label = label,
            Provenance = provenance,
            TextContent = text,
        };

    private static AgentPacketSourceItem CreateAsset(
        AgentPacketSourceKind kind,
        string id,
        string label,
        string role,
        string relativePath,
        AgentPacketProvenance provenance,
        string? extractedText,
        string? mediaType) =>
        new()
        {
            Id = id,
            Kind = kind,
            Label = label,
            Provenance = provenance,
            TextContent = extractedText,
            Assets = Array.AsReadOnly(
            [
                new AgentPacketAssetReference
                {
                    Role = role,
                    RelativePath = relativePath,
                    MediaType = mediaType,
                },
            ]),
        };
}

/// <summary>A concrete, independently verifiable success condition.</summary>
public sealed record AgentPacketAcceptanceCriterion
{
    public required string Id { get; init; }

    public required string Description { get; init; }

    public bool IsRequired { get; init; } = true;

    public string? VerificationHint { get; init; }
}

/// <summary>Raw local input used only long enough to build a reviewed packet.</summary>
public sealed record AgentPacketBuildRequest
{
    public required AgentPacketMetadata Metadata { get; init; }

    public required IReadOnlyList<AgentPacketSourceItem> Sources { get; init; }

    public required IReadOnlyList<AgentPacketAcceptanceCriterion> AcceptanceCriteria { get; init; }

    public bool RedactSecrets { get; init; } = true;
}

/// <summary>A count for one secret category. It never contains the secret value.</summary>
public sealed record AgentPacketSecretCount(string Kind, int Count);

/// <summary>
/// A normalized asset reference safe to include in the reviewed output bundle.
/// </summary>
public sealed record ReviewedAgentPacketAsset(
    string Role,
    string RelativePath,
    string? MediaType,
    string? Sha256,
    int? PixelWidth,
    int? PixelHeight);

/// <summary>
/// One normalized reviewed source. When redaction was requested, TextContent
/// contains only the detector's redacted copy and never the original text.
/// </summary>
public sealed record ReviewedAgentPacketSource
{
    public required string Id { get; init; }

    public required AgentPacketSourceKind Kind { get; init; }

    public required string Label { get; init; }

    public required AgentPacketProvenance Provenance { get; init; }

    public string? TextContent { get; init; }

    public required IReadOnlyList<ReviewedAgentPacketAsset> Assets { get; init; }

    public required IReadOnlyList<AgentPacketSecretCount> SecretCounts { get; init; }

    public int DetectedSecretCount => SecretCounts.Sum(item => item.Count);
}

/// <summary>
/// Immutable provider-neutral review. <see cref="OutboundMarkdown"/> is the
/// exact agent input; <see cref="ManifestJson"/> is the deterministic,
/// machine-readable equivalent for bundles and integrations.
/// </summary>
public sealed record ReviewedAgentPacket
{
    public const int CurrentSchemaVersion = 2;

    public required AgentPacketMetadata Metadata { get; init; }

    public required IReadOnlyList<ReviewedAgentPacketSource> Sources { get; init; }

    public required IReadOnlyList<AgentPacketAcceptanceCriterion> AcceptanceCriteria { get; init; }

    public required string OutboundMarkdown { get; init; }

    public required string ManifestJson { get; init; }

    public required string OutboundSha256 { get; init; }

    public required string ManifestSha256 { get; init; }

    public required IReadOnlyList<AgentPacketSecretCount> SecretCounts { get; init; }

    /// <summary>True when secret detection and replacement covered every included text field.</summary>
    public bool TextSecretsRedacted { get; init; }

    /// <summary>True when the packet contains image or binary bytes that were not content-scanned.</summary>
    public bool ContainsUnscannedAssets { get; init; }

    /// <summary>True only when every included content class is covered by redaction.</summary>
    public bool AllIncludedContentRedacted => TextSecretsRedacted && !ContainsUnscannedAssets;

    /// <summary>
    /// Compatibility alias with safe packet-wide semantics. Prefer
    /// <see cref="TextSecretsRedacted"/> for UI that explicitly discusses text.
    /// </summary>
    public bool SecretsRedacted => AllIncludedContentRedacted;

    public int SourceCharacterCount { get; init; }

    public int OutboundCharacterCount => OutboundMarkdown.Length;

    public int DetectedSecretCount => SecretCounts.Sum(item => item.Count);
}

/// <summary>Provider-neutral Agent Packet review service for application integration.</summary>
public interface IAgentPacketBuilder
{
    ReviewedAgentPacket Build(AgentPacketBuildRequest request);
}

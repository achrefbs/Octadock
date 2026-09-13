using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;

namespace Octadock.App.Ai;

/// <summary>Runs one exact reviewed prompt through one explicitly selected local CLI.</summary>
public interface IAiCliRunner
{
    IReadOnlyList<AiCliProviderDescriptor> Providers { get; }

    Task<string> RunAsync(
        string providerId,
        string outboundText,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds reviewed payloads and delegates execution to the selected CLI only. It
/// never retains source/result text, chooses a fallback provider, or reads API keys.
/// </summary>
public sealed class CliAiTextActionService : IAiTextActionService
{
    private readonly ITextSecretDetector _secretDetector;
    private readonly IAiCliRunner _runner;

    public CliAiTextActionService(
        ITextSecretDetector secretDetector,
        IAiCliRunner runner)
    {
        _secretDetector = secretDetector ?? throw new ArgumentNullException(nameof(secretDetector));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc />
    public IReadOnlyList<AiCliProviderDescriptor> Providers => _runner.Providers;

    /// <inheritdoc />
    public AiOutboundReview Review(AiTextActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AiCliProviderDescriptor provider = Providers.FirstOrDefault(item =>
                string.Equals(item.Id, request.ProviderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown AI CLI provider '{request.ProviderId}'.", nameof(request));

        string exactPrompt = AiTextActionPromptBuilder.Build(request.Action, request.Text, request.SourceName);
        TextSecretScanResult scan = _secretDetector.Scan(exactPrompt);
        string outbound = request.RedactSecrets ? scan.RedactedText : exactPrompt;

        return new AiOutboundReview
        {
            Action = request.Action,
            ProviderId = provider.Id,
            ProviderDisplayName = provider.DisplayName,
            DestinationDisclosure = provider.DestinationDisclosure,
            OutboundText = outbound,
            SourceCharacterCount = request.Text.Length,
            DetectedSecretCount = scan.Findings.Count,
            SecretsRedacted = request.RedactSecrets,
        };
    }

    /// <inheritdoc />
    public async Task<AiTextActionResult> ExecuteReviewedAsync(
        AiOutboundReview review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);


        AiCliProviderDescriptor provider = Providers.FirstOrDefault(item =>
                string.Equals(item.Id, review.ProviderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"AI CLI provider '{review.ProviderId}' is no longer configured.");
        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(
                provider.UnavailableReason ?? $"{provider.DisplayName} CLI was not found on PATH.");
        }

        if (string.IsNullOrWhiteSpace(review.OutboundText))
        {
            throw new InvalidOperationException("The reviewed outbound preview is empty.");
        }

        string output = await _runner.RunAsync(provider.Id, review.OutboundText, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"{provider.DisplayName} returned an empty result.");
        }

        return new AiTextActionResult(output.Trim(), provider.Id, review.Action);
    }
}

/// <summary>Local export destination; never discovers or starts external agent processes.</summary>
public sealed class CliAiRunner : IAiCliRunner
{
    internal static IReadOnlyList<AiCliProviderDescriptor> LocalDestinations { get; } =
    [new("local-export", "Local export", "Files on this PC", false, "Use Copy packet or Save bundle.")];
    public IReadOnlyList<AiCliProviderDescriptor> Providers => LocalDestinations;
    public Task<string> RunAsync(string providerId, string outboundText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Octadock only prepares local exports. Remote execution was removed.");
    }
}

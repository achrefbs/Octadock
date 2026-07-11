using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Licensing;

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
    private readonly ILicenseGate _licenseGate;

    public CliAiTextActionService(
        ITextSecretDetector secretDetector,
        IAiCliRunner runner,
        ILicenseGate licenseGate)
    {
        _secretDetector = secretDetector ?? throw new ArgumentNullException(nameof(secretDetector));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));
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
        if (!_licenseGate.Allow(GatedFeature.AiActions))
        {
            throw new InvalidOperationException(
                "Your Octadock trial has ended. Enter a license key in Settings → Account before sending an AI action.");
        }

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

/// <summary>Process runner for user-installed Codex and Claude CLIs.</summary>
public sealed partial class CliAiRunner : IAiCliRunner
{
    private static readonly TimeSpan CliTimeout = TimeSpan.FromMinutes(2);
    private static readonly Regex AnsiRegex = CreateAnsiRegex();

    private readonly IStoragePaths _paths;
    private readonly ILogger<CliAiRunner> _logger;
    private readonly IAgentCliProcessInvoker _processInvoker;
    private readonly Lazy<IReadOnlyList<AiCliProviderDescriptor>> _providers;

    public CliAiRunner(IStoragePaths paths, ILogger<CliAiRunner> logger)
        : this(
            paths,
            logger,
            new SystemAgentCliProcessInvoker(CliTimeout, maxOutputCharacters: 500_000))
    {
    }

    internal CliAiRunner(
        IStoragePaths paths,
        ILogger<CliAiRunner> logger,
        IAgentCliProcessInvoker processInvoker)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processInvoker = processInvoker ?? throw new ArgumentNullException(nameof(processInvoker));
        _providers = new Lazy<IReadOnlyList<AiCliProviderDescriptor>>(() =>
        [
            DescribeProvider(
                AiCliProviderIds.Codex,
                "Codex",
                "the remote service configured by your installed Codex CLI"),
            DescribeProvider(
                AiCliProviderIds.Claude,
                "Claude",
                "the remote service configured by your installed Claude CLI"),
        ]);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiCliProviderDescriptor> Providers => _providers.Value;

    /// <inheritdoc />
    public async Task<string> RunAsync(
        string providerId,
        string outboundText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(outboundText);

        string normalized = providerId.Trim().ToLowerInvariant();
        IReadOnlyList<string> arguments = ArgumentsFor(normalized);

        if (!CanRunCommand(normalized))
        {
            throw new InvalidOperationException($"{DisplayName(normalized)} CLI was not found on PATH.");
        }

        try
        {
            CliRunResult result = await RunCliAsync(normalized, arguments, outboundText, cancellationToken)
                .ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{DisplayName(normalized)} CLI exited with code {result.ExitCode}. Run it once in a terminal to verify sign-in and configuration.");
            }

            string output = AnsiRegex.Replace(result.Stdout, string.Empty).Trim();
            if (output.Length > 500_000)
            {
                throw new InvalidOperationException($"{DisplayName(normalized)} returned more text than Octadock can display safely.");
            }

            return output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogProviderRunFailed(_logger, ex, normalized);
            throw;
        }
    }

    internal static IReadOnlyList<string> ArgumentsFor(string providerId)
        => providerId switch
        {
            AiCliProviderIds.Codex =>
            [
                "-a",
                "never",
                "exec",
                "--ephemeral",
                "--skip-git-repo-check",
                "--sandbox",
                "read-only",
                "--ignore-rules",
                "--color",
                "never",
                "-",
            ],
            AiCliProviderIds.Claude =>
            [
                "-p",
                "--output-format",
                "text",
                "--permission-mode",
                "dontAsk",
                "--tools",
                string.Empty,
                "--no-session-persistence",
                "--safe-mode",
            ],
            _ => throw new ArgumentException($"Unsupported AI CLI provider '{providerId}'.", nameof(providerId)),
        };

    private async Task<CliRunResult> RunCliAsync(
        string command,
        IReadOnlyList<string> arguments,
        string stdin,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        Directory.CreateDirectory(_paths.TempExportsDirectory);
        AgentCliProcessResult process = await _processInvoker.RunAsync(
            new AgentCliInvocation(command, _paths.TempExportsDirectory, arguments, stdin),
            cancellationToken).ConfigureAwait(false);
        return new CliRunResult(process.ExitCode, process.StandardOutput);
    }

    private static AiCliProviderDescriptor DescribeProvider(string id, string name, string destination)
    {
        bool available = CanRunCommand(id);
        return new AiCliProviderDescriptor(
            id,
            name,
            destination,
            available,
            available ? null : $"{name} CLI was not found on PATH.");
    }

    private static string DisplayName(string providerId)
        => string.Equals(providerId, AiCliProviderIds.Claude, StringComparison.OrdinalIgnoreCase)
            ? "Claude"
            : "Codex";

    private static bool CanRunCommand(string command)
        => SystemAgentCliProcessInvoker.ResolveExecutable(command) is not null;

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex CreateAnsiRegex();

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Selected AI CLI provider {Provider} failed.")]
    private static partial void LogProviderRunFailed(ILogger logger, Exception exception, string provider);

    private sealed record CliRunResult(int ExitCode, string Stdout);
}

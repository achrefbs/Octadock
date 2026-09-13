using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Octadock.Core.Ai;

namespace Octadock.App.Ai;

/// <summary>
/// One exact, user-reviewed Agent Packet handoff. <see cref="ExactPrompt"/> is
/// sent character-for-character to the selected CLI; the runner never adds hidden context.
/// </summary>
public sealed record AgentAnalyzeRequest
{
    public required string ProviderId { get; init; }

    /// <summary>The local directory containing TASK.md, manifest.json, and packet assets.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The complete outbound prompt already shown to and approved by the user.</summary>
    public required string ExactPrompt { get; init; }

    /// <summary>Local packet images the selected agent may inspect.</summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = [];
}

/// <summary>Runs a reviewed Agent Packet through one explicitly selected local CLI.</summary>
public interface IAgentCliRunner
{
    IReadOnlyList<AiCliProviderDescriptor> Providers { get; }

    Task<string> AnalyzeAsync(
        AgentAnalyzeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Compatibility boundary for the local export workspace. No provider is executed.</summary>
public sealed class AgentCliRunner : IAgentCliRunner
{
    public IReadOnlyList<AiCliProviderDescriptor> Providers => CliAiRunner.LocalDestinations;
    public Task<string> AnalyzeAsync(AgentAnalyzeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Use Copy packet or Save bundle to export locally. Remote execution was removed.");
    }
}

using System.IO;
using Octadock.Core.Models;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>Minimal snapshot of one OS process used by the classifiers.</summary>
public sealed record AiSessionProcessSnapshot(
    int Pid,
    string ProcessName,
    string? ExecutablePath,
    string? MainWindowTitle,
    DateTimeOffset? StartedAt,
    int? ParentPid,
    string? CommandLine);

/// <summary>One process snapshot with normalized fields, handed to classifiers.</summary>
public sealed record AiSessionProcessContext
{
    public required AiSessionProcessSnapshot Snapshot { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    /// <summary>Process name without ".exe".</summary>
    public required string ProcessName { get; init; }

    public required string? ExecutablePath { get; init; }

    /// <summary>File name of the executable (e.g. "node.exe"), empty when unknown.</summary>
    public required string FileName { get; init; }

    public required string? CommandLine { get; init; }

    public static AiSessionProcessContext Create(AiSessionProcessSnapshot snapshot, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string? executablePath = AiSessionTextSanitizer.Clean(snapshot.ExecutablePath);
        string fileName = string.Empty;
        if (executablePath is not null)
        {
            try
            {
                fileName = Path.GetFileName(executablePath);
            }
            catch (ArgumentException)
            {
                fileName = string.Empty;
            }
        }

        return new AiSessionProcessContext
        {
            Snapshot = snapshot,
            ObservedAt = observedAt,
            ProcessName = AiSessionTextSanitizer.NormalizeProcessName(snapshot.ProcessName),
            ExecutablePath = executablePath,
            FileName = fileName,
            CommandLine = AiSessionTextSanitizer.Clean(snapshot.CommandLine),
        };
    }

    public bool CommandLineContains(string fragment)
        => CommandLine?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;

    public bool ExecutablePathContains(string fragment)
        => ExecutablePath?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;

    public bool FileNameIs(string expected)
        => string.Equals(FileName, expected, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Verdict from one classifier: an accepted evidence record, or an explicit
/// rejection with a diagnostic reason. Null from a classifier means
/// "not my provider family, ask the next classifier".
/// </summary>
public sealed record AiSessionProcessClassification
{
    public AiSessionEvidence? Evidence { get; init; }

    public string? RejectionDetector { get; init; }

    public string? RejectionReason { get; init; }

    public bool IsAccepted => Evidence is not null;

    public static AiSessionProcessClassification Accept(AiSessionEvidence evidence)
        => new() { Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence)) };

    public static AiSessionProcessClassification Reject(string detector, string reason)
        => new() { RejectionDetector = detector, RejectionReason = reason };
}

/// <summary>Classifies processes belonging to one provider family.</summary>
public interface IAiSessionProcessClassifier
{
    /// <summary>Stable id used in diagnostics.</summary>
    string Id { get; }

    /// <summary>Accept/Reject verdict, or null when the process is not this family's.</summary>
    AiSessionProcessClassification? Classify(AiSessionProcessContext context);
}

/// <summary>Confidence bands shared by all collectors and classifiers.</summary>
public static class AiSessionConfidence
{
    /// <summary>Below this an observation is dropped instead of shown.</summary>
    public const double MinimumToShow = 0.6;

    /// <summary>Provider-branded binary at a known install location.</summary>
    public const double Certain = 0.95;

    /// <summary>Provider module/state clearly identified but indirect (node worker, state file).</summary>
    public const double Strong = 0.85;

    /// <summary>Recognizable agent CLI by name, or fresh provider state alone.</summary>
    public const double Moderate = 0.7;

    /// <summary>Real signal, but should be confirmed by provider state when available.</summary>
    public const double NeedsCorroboration = 0.65;
}

/// <summary>Shared helper predicates for infrastructure processes that are never sessions.</summary>
public static class AiSessionProcessNoise
{
    /// <summary>Electron/Chromium helper processes (renderer, gpu, utility, ...).</summary>
    public static bool IsElectronHelper(AiSessionProcessContext context)
        => context.CommandLineContains("--type=");

    /// <summary>Language servers and other stdio IDE backends.</summary>
    public static bool IsLanguageServerLike(AiSessionProcessContext context)
        => context.CommandLineContains("--stdio") ||
            context.CommandLineContains("tsserver.js") ||
            context.CommandLineContains("language-server") ||
            context.CommandLineContains("languageserver");

    /// <summary>Browser-extension native messaging bridges.</summary>
    public static bool IsNativeMessagingHost(AiSessionProcessContext context)
        => context.CommandLineContains("--chrome-native-host") ||
            context.CommandLineContains("native-messaging-host") ||
            context.CommandLineContains("chrome-extension://");

    /// <summary>Node workers spawned inside an Electron editor (VS Code style).</summary>
    public static bool IsEditorNodeWorker(AiSessionProcessContext context)
        => context.CommandLineContains("--ms-enable-electron-run-as-node");
}

/// <summary>
/// Ordered classifier chain: the first classifier that returns a verdict wins.
/// Adding a provider means adding a classifier here, not editing the pipeline.
/// </summary>
public sealed class AiSessionProcessClassifierRegistry
{
    private readonly IReadOnlyList<IAiSessionProcessClassifier> _classifiers;

    public AiSessionProcessClassifierRegistry(IReadOnlyList<IAiSessionProcessClassifier>? classifiers = null)
    {
        _classifiers = classifiers is { Count: > 0 } ? classifiers : CreateDefaultClassifiers();
    }

    public static IReadOnlyList<IAiSessionProcessClassifier> CreateDefaultClassifiers()
        =>
        [
            new CodexProcessClassifier(),
            new ClaudeCodeProcessClassifier(),
            new CursorProcessClassifier(),
            new CopilotProcessClassifier(),
            new GeminiProcessClassifier(),
            new OllamaProcessClassifier(),
            new GenericAgentCliClassifier(),
        ];

    /// <summary>First classifier verdict for this process, or null when no family matched.</summary>
    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IAiSessionProcessClassifier classifier in _classifiers)
        {
            AiSessionProcessClassification? classification = classifier.Classify(context);
            if (classification is not null)
            {
                return classification;
            }
        }

        return null;
    }

    /// <summary>Convenience wrapper: classify a raw snapshot and return accepted evidence only.</summary>
    public AiSessionEvidence? ClassifySnapshot(AiSessionProcessSnapshot snapshot, DateTimeOffset observedAt)
        => Classify(AiSessionProcessContext.Create(snapshot, observedAt))?.Evidence;
}

/// <summary>Factory helpers for process-backed evidence records.</summary>
public static class AiSessionProcessEvidence
{
    public static AiSessionEvidence Create(
        AiSessionProcessContext context,
        AiSessionProvider provider,
        string detector,
        double confidence,
        string reason,
        string title,
        string? workspacePath = null,
        string? providerSessionId = null,
        bool requiresCorroboration = false,
        string? corroborationSource = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new AiSessionEvidence
        {
            Source = ProcessSnapshotEvidenceCollector.SourceId,
            Detector = detector,
            Provider = provider,
            Confidence = confidence,
            Reason = reason,
            Title = AiSessionTextSanitizer.SanitizeTitle(title, provider.ToString()),
            Command = AiSessionTextSanitizer.SanitizeCommand(context.CommandLine) ??
                AiSessionTextSanitizer.Truncate(context.ExecutablePath, AiSessionTextSanitizer.MaxCommandLength),
            Pid = context.Snapshot.Pid,
            ParentPid = context.Snapshot.ParentPid,
            ProcessStartTicks = context.Snapshot.StartedAt?.UtcTicks,
            ProcessName = context.ProcessName,
            ExecutablePath = context.ExecutablePath,
            MainWindowTitle = AiSessionTextSanitizer.Clean(context.Snapshot.MainWindowTitle),
            WorkspacePath = AiSessionTextSanitizer.NormalizePath(workspacePath),
            ProviderSessionId = AiSessionTextSanitizer.Clean(providerSessionId),
            StartedAt = context.Snapshot.StartedAt,
            StatusHint = null,
            RequiresCorroboration = requiresCorroboration,
            CorroborationSource = corroborationSource,
            Metadata = metadata,
        };
    }
}

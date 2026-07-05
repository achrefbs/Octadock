using System.IO;
using Octadock.Core.Models;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>Directory-listing view of one Claude Code session transcript file.</summary>
public sealed record AiSessionClaudeTranscriptSnapshot(
    string ProjectDirectoryName,
    string TranscriptPath,
    string SessionId,
    DateTimeOffset LastWriteUtc);

/// <summary>
/// Watches Claude Code's per-project state under ~/.claude/projects. Each
/// session writes a &lt;session-id&gt;.jsonl transcript there; a transcript
/// with a very fresh write time is a running session with a known session id
/// and (encoded) workspace. Privacy: only file names and timestamps are read —
/// transcript content is never opened.
/// </summary>
public sealed class ClaudeCodeStateEvidenceCollector : IAiSessionEvidenceCollector
{
    public const string SourceId = "claude-state";

    /// <summary>A transcript written within this window counts as a live session.</summary>
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(90);

    private const int MaxTranscripts = 20;

    private readonly Func<IReadOnlyList<AiSessionClaudeTranscriptSnapshot>> _transcriptProvider;

    public ClaudeCodeStateEvidenceCollector(
        Func<IReadOnlyList<AiSessionClaudeTranscriptSnapshot>>? transcriptProvider = null)
    {
        _transcriptProvider = transcriptProvider ?? EnumerateTranscripts;
    }

    public string Source => SourceId;

    public Task<AiSessionEvidenceBatch> CollectAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                try
                {
                    IReadOnlyList<AiSessionClaudeTranscriptSnapshot> transcripts = _transcriptProvider();
                    var evidence = new List<AiSessionEvidence>();
                    foreach (AiSessionClaudeTranscriptSnapshot transcript in transcripts)
                    {
                        AiSessionEvidence? item = ClassifyTranscript(transcript, observedAt);
                        if (item is not null)
                        {
                            evidence.Add(item);
                        }
                    }

                    return new AiSessionEvidenceBatch(SourceId, Succeeded: true, evidence);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    return AiSessionEvidenceBatch.Failed(SourceId, $"Claude state read failed: {ex.Message}");
                }
            },
            cancellationToken);

    /// <summary>
    /// Maps one transcript listing to Running evidence, or null when quiet.
    /// No Completed hint is emitted on purpose: a quiet transcript cannot be
    /// told apart from an idle session waiting at the prompt, so lifecycle is
    /// left to process evidence.
    /// </summary>
    public static AiSessionEvidence? ClassifyTranscript(
        AiSessionClaudeTranscriptSnapshot transcript,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        if (transcript.LastWriteUtc < observedAt.Subtract(ActiveWindow) ||
            string.IsNullOrWhiteSpace(transcript.SessionId))
        {
            return null;
        }

        string? decodedWorkspace = AiSessionTextSanitizer.TryDecodeClaudeProjectDirectory(
            transcript.ProjectDirectoryName);
        string folder = AiSessionTextSanitizer.WorkspaceLabel(decodedWorkspace);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["claudeProjectDir"] = transcript.ProjectDirectoryName,
            ["claudeSessionId"] = transcript.SessionId,
        };

        return new AiSessionEvidence
        {
            Source = SourceId,
            Detector = "claude-project-transcript",
            Provider = AiSessionProvider.ClaudeCode,
            Confidence = AiSessionConfidence.Moderate,
            Reason = "Claude Code session transcript is being written right now.",
            Title = string.IsNullOrEmpty(folder) ? "Claude Code" : $"Claude Code - {folder}",
            WorkspacePath = decodedWorkspace,
            ProviderSessionId = transcript.SessionId,
            LogFilePath = transcript.TranscriptPath,
            LastActivityAt = transcript.LastWriteUtc,
            StatusHint = AiSessionStatus.Running,
            Metadata = metadata,
        };
    }

    /// <summary>Root folder holding Claude Code per-project state, or null when absent.</summary>
    public static string? FindClaudeProjectsDirectory()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        string projects = Path.Combine(userProfile, ".claude", "projects");
        return Directory.Exists(projects) ? projects : null;
    }

    private static IReadOnlyList<AiSessionClaudeTranscriptSnapshot> EnumerateTranscripts()
    {
        string? projectsRoot = FindClaudeProjectsDirectory();
        if (projectsRoot is null)
        {
            return [];
        }

        var transcripts = new List<AiSessionClaudeTranscriptSnapshot>();
        foreach (string projectDir in Directory.EnumerateDirectories(projectsRoot))
        {
            string projectName = Path.GetFileName(projectDir);
            foreach (string file in Directory.EnumerateFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                transcripts.Add(new AiSessionClaudeTranscriptSnapshot(
                    projectName,
                    file,
                    Path.GetFileNameWithoutExtension(file),
                    new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero)));
            }
        }

        return transcripts
            .OrderByDescending(t => t.LastWriteUtc)
            .Take(MaxTranscripts)
            .ToArray();
    }
}

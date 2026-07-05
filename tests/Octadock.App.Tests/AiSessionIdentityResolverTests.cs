using FluentAssertions;
using Octadock.App.Services.AiSessionDiscovery;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AiSessionIdentityResolverTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 3, 12, 5, 0, TimeSpan.Zero);

    private readonly AiSessionIdentityResolver _resolver = new();

    [Fact]
    public void Codex_thread_and_runtime_worker_merge_into_one_observation()
    {
        AiSessionEvidence thread = CodexThreadEvidence(
            "thread-1",
            @"C:\Users\acera\Desktop\Workspace\Octadock",
            AiSessionStatus.Running);
        AiSessionEvidence worker = CodexRuntimeEvidence(
            11552,
            "runtime-abc",
            @"C:\Users\acera\Desktop\Workspace\Octadock");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(CodexStateEvidenceCollector.SourceId, thread),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, worker),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(1);
        AiSessionObservation observation = resolution.Observations[0];
        observation.DiscoveryKey.Should().Be("Codex:session:thread-1");
        observation.Pid.Should().Be(11552);
        observation.Status.Should().Be(AiSessionStatus.Running);
        observation.Sources.Should().BeEquivalentTo(
            [CodexStateEvidenceCollector.SourceId, ProcessSnapshotEvidenceCollector.SourceId]);
        observation.Confidence.Should().BeGreaterThan(AiSessionConfidence.Certain);
        observation.Confidence.Should().BeLessThanOrEqualTo(0.99);
        observation.Reason.Should().Contain("Corroborated by 2");
    }

    [Fact]
    public void Codex_runtime_worker_is_dropped_when_state_ran_but_has_no_matching_thread()
    {
        AiSessionEvidence worker = CodexRuntimeEvidence(
            11552,
            "runtime-abc",
            @"C:\Users\acera\Desktop\Workspace\Roamcaster");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(CodexStateEvidenceCollector.SourceId),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, worker),
            ],
            ObservedAt);

        resolution.Observations.Should().BeEmpty();
        resolution.Dropped.Should().ContainSingle()
            .Which.Reason.Should().Contain("corroboration");
    }

    [Fact]
    public void Codex_runtime_worker_survives_when_state_source_is_unavailable()
    {
        AiSessionEvidence worker = CodexRuntimeEvidence(
            11552,
            "runtime-abc",
            @"C:\Users\acera\Desktop\Workspace\Roamcaster");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                AiSessionEvidenceBatch.Failed(CodexStateEvidenceCollector.SourceId, "no state db"),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, worker),
            ],
            ObservedAt);

        resolution.Observations.Should().ContainSingle()
            .Which.DiscoveryKey.Should().Be("Codex:session:runtime-abc");
    }

    [Fact]
    public void Completed_thread_hint_wins_over_live_runtime_worker()
    {
        AiSessionEvidence thread = CodexThreadEvidence(
            "thread-1",
            @"C:\Repo",
            AiSessionStatus.Completed);
        AiSessionEvidence worker = CodexRuntimeEvidence(11552, "runtime-abc", @"C:\Repo");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(CodexStateEvidenceCollector.SourceId, thread),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, worker),
            ],
            ObservedAt);

        resolution.Observations.Should().ContainSingle()
            .Which.Status.Should().Be(AiSessionStatus.Completed);
    }

    [Fact]
    public void Claude_process_without_cwd_pairs_with_fresh_transcript()
    {
        AiSessionEvidence transcript = ClaudeTranscriptEvidence(
            "session-9",
            "C--Users-acera-Desktop-Workspace-Octadock",
            ObservedAt.AddSeconds(-15));
        AiSessionEvidence process = ClaudeProcessEvidence(4242, workspace: null);

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(ClaudeCodeStateEvidenceCollector.SourceId, transcript),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, process),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(1);
        AiSessionObservation observation = resolution.Observations[0];
        observation.DiscoveryKey.Should().Be("ClaudeCode:session:session-9");
        observation.Pid.Should().Be(4242);
        observation.Status.Should().Be(AiSessionStatus.Running);
    }

    [Fact]
    public void Claude_process_with_matching_workspace_pairs_by_workspace_token()
    {
        AiSessionEvidence transcript = ClaudeTranscriptEvidence(
            "session-9",
            "C--Users-acera-Desktop-Workspace-Octadock",
            ObservedAt.AddSeconds(-15));
        AiSessionEvidence process = ClaudeProcessEvidence(
            4242,
            workspace: @"C:\Users\acera\Desktop\Workspace\Octadock");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(ClaudeCodeStateEvidenceCollector.SourceId, transcript),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, process),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(1);
        resolution.Observations[0].WorkspacePath.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
    }

    [Fact]
    public void Claude_process_with_conflicting_workspace_stays_separate()
    {
        AiSessionEvidence transcript = ClaudeTranscriptEvidence(
            "session-9",
            "C--Users-acera-Desktop-Workspace-Octadock",
            ObservedAt.AddSeconds(-15));
        AiSessionEvidence process = ClaudeProcessEvidence(
            4242,
            workspace: @"C:\Users\acera\Desktop\Workspace\OtherRepo");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(ClaudeCodeStateEvidenceCollector.SourceId, transcript),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, process),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(2);
    }

    [Fact]
    public void Two_transcripts_and_two_processes_pair_one_to_one()
    {
        AiSessionEvidence newerTranscript = ClaudeTranscriptEvidence(
            "session-new",
            "C--Users-acera-Repo",
            ObservedAt.AddSeconds(-5));
        AiSessionEvidence olderTranscript = ClaudeTranscriptEvidence(
            "session-old",
            "C--Users-acera-Repo",
            ObservedAt.AddSeconds(-60));
        AiSessionEvidence newerProcess = ClaudeProcessEvidence(2, workspace: null, startedAt: ObservedAt.AddMinutes(-1));
        AiSessionEvidence olderProcess = ClaudeProcessEvidence(1, workspace: null, startedAt: ObservedAt.AddMinutes(-30));

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(ClaudeCodeStateEvidenceCollector.SourceId, newerTranscript, olderTranscript),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, newerProcess, olderProcess),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(2);
        resolution.Observations.Select(o => o.DiscoveryKey).Should().BeEquivalentTo(
            ["ClaudeCode:session:session-new", "ClaudeCode:session:session-old"]);
        resolution.Observations.Should().OnlyContain(o => o.Pid != null);
    }

    [Fact]
    public void Workspaceless_process_never_pairs_with_a_completed_or_quiet_state_group()
    {
        // A quiet Codex desktop thread (Completed hint) must not absorb a live
        // CLI process, or the live session's row would be wrongly completed.
        AiSessionEvidence quietThread = CodexThreadEvidence(
            "thread-quiet",
            @"C:\SomeOtherRepo",
            AiSessionStatus.Completed);
        AiSessionEvidence liveCli = new()
        {
            Source = ProcessSnapshotEvidenceCollector.SourceId,
            Detector = "codex-cli",
            Provider = AiSessionProvider.Codex,
            Confidence = AiSessionConfidence.Strong,
            Reason = "Standalone Codex CLI process.",
            Title = "Codex CLI",
            Pid = 900,
            StartedAt = ObservedAt.AddMinutes(-1),
        };

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(CodexStateEvidenceCollector.SourceId, quietThread),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, liveCli),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(2);
        AiSessionObservation cli = resolution.Observations.Single(o => o.Pid == 900);
        cli.Status.Should().Be(AiSessionStatus.Running);
    }

    [Fact]
    public void Sibling_processes_with_the_same_session_id_form_one_observation()
    {
        AiSessionEvidence first = CodexRuntimeEvidence(11, "shared-session", @"C:\Repo");
        AiSessionEvidence second = CodexRuntimeEvidence(22, "shared-session", @"C:\Repo");

        AiSessionResolution resolution = _resolver.Resolve(
            [
                AiSessionEvidenceBatch.Failed(CodexStateEvidenceCollector.SourceId, "locked"),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, first, second),
            ],
            ObservedAt);

        resolution.Observations.Should().ContainSingle()
            .Which.DiscoveryKey.Should().Be("Codex:session:shared-session");
    }

    [Fact]
    public void Exact_workspace_match_wins_over_a_newer_workspaceless_process()
    {
        AiSessionEvidence transcript = ClaudeTranscriptEvidence(
            "session-1",
            "C--Users-acera-Desktop-Workspace-Octadock",
            ObservedAt.AddSeconds(-10));
        AiSessionEvidence olderMatchingProcess = ClaudeProcessEvidence(
            1,
            workspace: @"C:\Users\acera\Desktop\Workspace\Octadock",
            startedAt: ObservedAt.AddMinutes(-30));
        AiSessionEvidence newerWorkspacelessProcess = ClaudeProcessEvidence(
            2,
            workspace: null,
            startedAt: ObservedAt.AddSeconds(-30));

        AiSessionResolution resolution = _resolver.Resolve(
            [
                Batch(ClaudeCodeStateEvidenceCollector.SourceId, transcript),
                Batch(ProcessSnapshotEvidenceCollector.SourceId, olderMatchingProcess, newerWorkspacelessProcess),
            ],
            ObservedAt);

        resolution.Observations.Should().HaveCount(2);
        AiSessionObservation merged = resolution.Observations
            .Single(o => o.DiscoveryKey == "ClaudeCode:session:session-1");
        merged.Pid.Should().Be(1);
    }

    [Fact]
    public void Low_confidence_evidence_is_dropped_with_a_reason()
    {
        AiSessionEvidence weak = new()
        {
            Source = ProcessSnapshotEvidenceCollector.SourceId,
            Detector = "generic-agent-cli",
            Provider = AiSessionProvider.Generic,
            Confidence = 0.4,
            Reason = "Weak guess.",
            Title = "Maybe an agent",
            Pid = 777,
        };

        AiSessionResolution resolution = _resolver.Resolve(
            [Batch(ProcessSnapshotEvidenceCollector.SourceId, weak)],
            ObservedAt);

        resolution.Observations.Should().BeEmpty();
        resolution.Dropped.Should().ContainSingle()
            .Which.Reason.Should().Contain("below the minimum");
    }

    [Fact]
    public void Evidence_from_failed_batches_is_ignored_entirely()
    {
        AiSessionEvidence orphan = ClaudeProcessEvidence(9);

        AiSessionResolution resolution = _resolver.Resolve(
            [new AiSessionEvidenceBatch(ProcessSnapshotEvidenceCollector.SourceId, Succeeded: false, [orphan], "boom")],
            ObservedAt);

        resolution.Observations.Should().BeEmpty();
        resolution.SucceededSources.Should().BeEmpty();
    }

    [Fact]
    public void Observation_metadata_carries_diagnostic_fields()
    {
        AiSessionEvidence process = ClaudeProcessEvidence(4242, workspace: @"C:\Repo");

        AiSessionResolution resolution = _resolver.Resolve(
            [Batch(ProcessSnapshotEvidenceCollector.SourceId, process)],
            ObservedAt);

        AiSessionObservation observation = resolution.Observations.Should().ContainSingle().Subject;
        observation.Metadata.Should().ContainKey("processName");
        observation.Metadata.Should().ContainKey("executablePath");
        observation.Metadata.Should().ContainKey("workingDirectory");
        observation.Metadata["workingDirectory"].Should().Be(@"C:\Repo");
    }

    private static AiSessionEvidenceBatch Batch(string source, params AiSessionEvidence[] evidence)
        => new(source, Succeeded: true, evidence);

    private static AiSessionEvidence CodexThreadEvidence(
        string threadId,
        string workspace,
        AiSessionStatus statusHint)
        => new()
        {
            Source = CodexStateEvidenceCollector.SourceId,
            Detector = "codex-state-thread",
            Provider = AiSessionProvider.Codex,
            Confidence = AiSessionConfidence.Certain,
            Reason = statusHint == AiSessionStatus.Completed
                ? "Codex thread rollout recorded task completion."
                : "Codex thread with fresh rollout activity.",
            Title = "Codex - " + AiSessionTextSanitizer.WorkspaceLabel(workspace),
            WorkspacePath = workspace,
            ProviderSessionId = threadId,
            StartedAt = ObservedAt.AddMinutes(-10),
            LastActivityAt = ObservedAt.AddSeconds(-20),
            StatusHint = statusHint,
        };

    private static AiSessionEvidence CodexRuntimeEvidence(int pid, string sessionId, string workspace)
        => new()
        {
            Source = ProcessSnapshotEvidenceCollector.SourceId,
            Detector = "codex-runtime-session",
            Provider = AiSessionProvider.Codex,
            Confidence = AiSessionConfidence.NeedsCorroboration,
            Reason = "Codex desktop runtime worker with an assigned session id.",
            Title = "Codex - " + AiSessionTextSanitizer.WorkspaceLabel(workspace),
            Command = $"node kernel.js --session-id {sessionId}",
            Pid = pid,
            ProcessStartTicks = ObservedAt.AddMinutes(-9).UtcTicks,
            ProcessName = "node",
            ExecutablePath = @"C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node.exe",
            WorkspacePath = workspace,
            ProviderSessionId = sessionId,
            StartedAt = ObservedAt.AddMinutes(-9),
            RequiresCorroboration = true,
            CorroborationSource = CodexStateEvidenceCollector.SourceId,
        };

    private static AiSessionEvidence ClaudeProcessEvidence(
        int pid,
        string? workspace = null,
        DateTimeOffset? startedAt = null)
        => new()
        {
            Source = ProcessSnapshotEvidenceCollector.SourceId,
            Detector = "claude-code-cli",
            Provider = AiSessionProvider.ClaudeCode,
            Confidence = AiSessionConfidence.Certain,
            Reason = "Claude Code CLI binary at a known install location.",
            Title = "Claude Code",
            Command = "claude",
            Pid = pid,
            ProcessStartTicks = (startedAt ?? ObservedAt.AddMinutes(-5)).UtcTicks,
            ProcessName = "claude",
            ExecutablePath = @"C:\Users\acera\AppData\Roaming\Claude\claude-code\2.1.197\claude.exe",
            WorkspacePath = workspace,
            StartedAt = startedAt ?? ObservedAt.AddMinutes(-5),
        };

    private static AiSessionEvidence ClaudeTranscriptEvidence(
        string sessionId,
        string projectDirectoryName,
        DateTimeOffset lastWrite)
        => new()
        {
            Source = ClaudeCodeStateEvidenceCollector.SourceId,
            Detector = "claude-project-transcript",
            Provider = AiSessionProvider.ClaudeCode,
            Confidence = AiSessionConfidence.Moderate,
            Reason = "Claude Code session transcript is being written right now.",
            Title = "Claude Code",
            WorkspacePath = AiSessionTextSanitizer.TryDecodeClaudeProjectDirectory(projectDirectoryName),
            ProviderSessionId = sessionId,
            LogFilePath = $@"C:\Users\acera\.claude\projects\{projectDirectoryName}\{sessionId}.jsonl",
            LastActivityAt = lastWrite,
            StatusHint = AiSessionStatus.Running,
            Metadata = new Dictionary<string, string>
            {
                ["claudeProjectDir"] = projectDirectoryName,
                ["claudeSessionId"] = sessionId,
            },
        };
}

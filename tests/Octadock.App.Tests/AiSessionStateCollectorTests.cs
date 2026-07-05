using FluentAssertions;
using Octadock.App.Services.AiSessionDiscovery;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AiSessionStateCollectorTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 3, 12, 5, 0, TimeSpan.Zero);

    // ---- Codex thread state ----

    [Fact]
    public void CodexThread_with_fresh_rollout_is_running_evidence()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f250b-451f-7b70-af17-c2f289897e85",
                cwd: @"\\?\C:\Users\acera\Desktop\Workspace\Octadock",
                updatedAt: ObservedAt.AddMinutes(-1),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddSeconds(-30)),
            ObservedAt);

        evidence.Should().NotBeNull();
        evidence!.Provider.Should().Be(AiSessionProvider.Codex);
        evidence.Detector.Should().Be("codex-state-thread");
        evidence.StatusHint.Should().Be(AiSessionStatus.Running);
        evidence.Title.Should().Be("Codex - Octadock");
        evidence.ProviderSessionId.Should().Be("019f250b-451f-7b70-af17-c2f289897e85");
        evidence.WorkspacePath.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
        evidence.Pid.Should().BeNull();
    }

    [Fact]
    public void CodexThread_open_subagent_uses_nickname_title()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f28b2-445d-7560-9419-b0ec8544e0a1",
                cwd: @"C:\Users\acera\Desktop\Workspace\Roamcaster",
                updatedAt: ObservedAt.AddHours(-1),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddSeconds(-30),
                spawnStatus: "open",
                parentThreadId: "019f28af-c3c8-7bf2-b097-fd609f664121",
                parentUpdatedAt: ObservedAt.AddMinutes(-2),
                source: "{\"subagent\":{\"thread_spawn\":{\"agent_nickname\":\"Huygens\"}}}"),
            ObservedAt);

        evidence.Should().NotBeNull();
        evidence!.Title.Should().Be("Codex subagent - Huygens");
        evidence.StatusHint.Should().Be(AiSessionStatus.Running);
    }

    [Fact]
    public void CodexThread_closed_subagent_is_invisible()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f25d9-4fed-76a1-94f4-cb64a921669f",
                cwd: @"C:\Users\acera\Desktop\Workspace\Octadock",
                updatedAt: ObservedAt.AddMinutes(-1),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddMinutes(-1),
                spawnStatus: "closed",
                parentThreadId: "019f250b-451f-7b70-af17-c2f289897e85",
                parentUpdatedAt: ObservedAt.AddMinutes(-2)),
            ObservedAt);

        evidence.Should().BeNull();
    }

    [Fact]
    public void CodexThread_stale_open_subagent_with_stale_parent_is_invisible()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019e2b7d-9349-74f1-96f1-640aa8407dd8",
                cwd: @"C:\Users\acera\.codex\worktrees\f4c7\AgentOS-dev",
                updatedAt: ObservedAt.AddDays(-40),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddDays(-40),
                spawnStatus: "open",
                parentThreadId: "019e2b7c-c067-76f1-8209-90923d6ace84",
                parentUpdatedAt: ObservedAt.AddDays(-40)),
            ObservedAt);

        evidence.Should().BeNull();
    }

    [Fact]
    public void CodexThread_stale_top_level_thread_is_invisible()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f2503-b1f4-7d33-95d7-ef20f6ce2321",
                cwd: @"C:\Users\acera\Desktop\Workspace\Octadock",
                updatedAt: ObservedAt.AddHours(-1),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddMinutes(-1)),
            ObservedAt);

        evidence.Should().BeNull();
    }

    [Fact]
    public void CodexThread_completed_rollout_yields_completed_hint()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f28af-c3c8-7bf2-b097-fd609f664121",
                cwd: @"C:\Users\acera\Desktop\Workspace\Roamcaster",
                updatedAt: ObservedAt.AddMinutes(-1),
                runState: AiSessionCodexThreadRunState.Completed,
                runStateObservedAt: ObservedAt.AddMinutes(-1)),
            ObservedAt);

        evidence.Should().NotBeNull();
        evidence!.StatusHint.Should().Be(AiSessionStatus.Completed);
        evidence.Reason.Should().Contain("completion");
    }

    [Fact]
    public void CodexThread_quiet_active_rollout_yields_completed_hint()
    {
        AiSessionEvidence? evidence = CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f250b-451f-7b70-af17-c2f289897e85",
                cwd: @"C:\Users\acera\Desktop\Workspace\Octadock",
                updatedAt: ObservedAt.AddMinutes(-1),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddMinutes(-3)),
            ObservedAt);

        evidence.Should().NotBeNull();
        evidence!.StatusHint.Should().Be(AiSessionStatus.Completed);
        evidence.Reason.Should().Contain("quiet");
    }

    [Fact]
    public void CodexThread_archived_or_unknown_state_is_invisible()
    {
        CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f250b-451f-7b70-af17-c2f289897e85",
                cwd: @"C:\Repo",
                updatedAt: ObservedAt.AddMinutes(-1),
                runState: AiSessionCodexThreadRunState.Active,
                runStateObservedAt: ObservedAt.AddSeconds(-10),
                archived: true),
            ObservedAt).Should().BeNull();

        CodexStateEvidenceCollector.ClassifyThread(
            Thread(
                "019f250b-451f-7b70-af17-c2f289897e85",
                cwd: @"C:\Repo",
                updatedAt: ObservedAt.AddMinutes(-1),
                runState: AiSessionCodexThreadRunState.Unknown,
                runStateObservedAt: null),
            ObservedAt).Should().BeNull();
    }

    // ---- Claude Code transcript state ----

    [Fact]
    public void ClaudeTranscript_written_just_now_is_running_evidence()
    {
        AiSessionEvidence? evidence = ClaudeCodeStateEvidenceCollector.ClassifyTranscript(
            new AiSessionClaudeTranscriptSnapshot(
                "C--Users-acera-Desktop-Workspace-Octadock",
                @"C:\Users\acera\.claude\projects\C--Users-acera-Desktop-Workspace-Octadock\9c2fbda1-03a7-46c9-a373-ab77a37d04f8.jsonl",
                "9c2fbda1-03a7-46c9-a373-ab77a37d04f8",
                ObservedAt.AddSeconds(-20)),
            ObservedAt);

        evidence.Should().NotBeNull();
        evidence!.Provider.Should().Be(AiSessionProvider.ClaudeCode);
        evidence.Detector.Should().Be("claude-project-transcript");
        evidence.StatusHint.Should().Be(AiSessionStatus.Running);
        evidence.ProviderSessionId.Should().Be("9c2fbda1-03a7-46c9-a373-ab77a37d04f8");
        evidence.Title.Should().Be("Claude Code - Octadock");
        evidence.Confidence.Should().BeGreaterThanOrEqualTo(AiSessionConfidence.MinimumToShow);
    }

    [Fact]
    public void ClaudeTranscript_quiet_for_minutes_is_invisible()
    {
        AiSessionEvidence? evidence = ClaudeCodeStateEvidenceCollector.ClassifyTranscript(
            new AiSessionClaudeTranscriptSnapshot(
                "C--Users-acera-Desktop-Workspace-Octadock",
                @"C:\Users\acera\.claude\projects\C--Users-acera-Desktop-Workspace-Octadock\old-session.jsonl",
                "old-session",
                ObservedAt.AddMinutes(-10)),
            ObservedAt);

        evidence.Should().BeNull();
    }

    // ---- Sanitizer behaviour the collectors rely on ----

    [Fact]
    public void Sanitizer_redacts_secret_options_and_tokens()
    {
        string? redacted = AiSessionTextSanitizer.SanitizeCommand(
            "agent --api-key sk-abcdef1234567890 --verbose --token=ghp_0123456789abcdef run");

        redacted.Should().NotBeNull();
        redacted.Should().NotContain("sk-abcdef1234567890");
        redacted.Should().NotContain("ghp_0123456789abcdef");
        redacted.Should().Contain("--verbose");
    }

    [Fact]
    public void Sanitizer_claude_project_tokens_match_real_and_decoded_paths()
    {
        const string realPath = @"C:\Users\acera\Desktop\my-repo";
        string? decoded = AiSessionTextSanitizer.TryDecodeClaudeProjectDirectory("C--Users-acera-Desktop-my-repo");

        decoded.Should().NotBeNull();
        AiSessionIdentityResolver.WorkspacesMatch(realPath, decoded).Should().BeTrue();
        AiSessionIdentityResolver.WorkspacesMatch(realPath, @"C:\Users\acera\Desktop\other").Should().BeFalse();
        AiSessionIdentityResolver.WorkspacesMatch(null, realPath).Should().BeFalse();
    }

    private static AiSessionCodexThreadSnapshot Thread(
        string threadId,
        string cwd,
        DateTimeOffset updatedAt,
        AiSessionCodexThreadRunState runState,
        DateTimeOffset? runStateObservedAt,
        string? spawnStatus = null,
        string? parentThreadId = null,
        DateTimeOffset? parentUpdatedAt = null,
        string? source = "vscode",
        bool archived = false)
        => new(
            threadId,
            "Thread title",
            cwd,
            updatedAt.AddHours(-2).ToUnixTimeSeconds(),
            updatedAt.ToUnixTimeSeconds(),
            archived,
            runState,
            runStateObservedAt,
            spawnStatus,
            parentThreadId,
            parentUpdatedAt?.ToUnixTimeSeconds(),
            source,
            "gpt-5.5",
            "openai",
            @"C:\Users\acera\.codex\state_5.sqlite");
}

using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class TurnReconstructorTests
{
    [Fact]
    public async Task Claude_reconstructs_one_tool_assisted_turn_and_excludes_private_reasoning()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "claude-session.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            "{\"type\":\"user\",\"uuid\":\"u1\",\"sessionId\":\"s1\",\"timestamp\":\"2026-07-10T10:00:00Z\",\"cwd\":\"C:/repo\",\"gitBranch\":\"main\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Fix the parser, but do not change the public API.\"}]}}",
            "{\"type\":\"assistant\",\"uuid\":\"a1\",\"sessionId\":\"s1\",\"timestamp\":\"2026-07-10T10:00:01Z\",\"stop_reason\":\"tool_use\",\"message\":{\"role\":\"assistant\",\"model\":\"claude-test\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"private reasoning sentinel\"},{\"type\":\"tool_use\",\"id\":\"tool1\",\"name\":\"Read\",\"input\":{\"file_path\":\"src/Parser.cs\"}}]}}",
            "{\"type\":\"user\",\"uuid\":\"r1\",\"sessionId\":\"s1\",\"timestamp\":\"2026-07-10T10:00:02Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"tool1\",\"content\":\"parser source\",\"is_error\":false}]}}",
            "{\"type\":\"assistant\",\"uuid\":\"a2\",\"sessionId\":\"s1\",\"timestamp\":\"2026-07-10T10:00:03Z\",\"stop_reason\":\"end_turn\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Implemented the parser fix in src/Parser.cs. The tests passed.\"}]}}",
        ]);

        TurnReconstructionResult result = await TurnReconstructor.ReconstructLatestAsync(ProviderKind.Claude, file);

        result.Turn.Should().NotBeNull();
        ReconstructedTurn turn = result.Turn!;
        turn.Boundary.Complete.Should().BeTrue();
        turn.Boundary.Confidence.Should().BeGreaterThan(0.95);
        turn.WorkingDirectory.Should().Be("C:/repo");
        turn.GitBranch.Should().Be("main");
        turn.Model.Should().Be("claude-test");
        turn.Segments.Should().Contain(segment => segment.Kind == SegmentKind.UserMessage);
        turn.Segments.Should().Contain(segment => segment.Kind == SegmentKind.ToolInvocation);
        turn.Segments.Should().Contain(segment => segment.Kind == SegmentKind.ToolResult);
        turn.Segments.Should().Contain(segment => segment.Kind == SegmentKind.AssistantMessage);
        turn.Segments.Should().NotContain(segment => segment.Text.Contains("private reasoning sentinel", StringComparison.Ordinal));
        turn.Segments.Should().OnlyContain(segment => segment.Anchor.ByteEndExclusive >= segment.Anchor.ByteStart);
        turn.Warnings.Should().Contain(warning => warning.Contains("reasoning block", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Codex_uses_explicit_turn_boundaries_and_deduplicates_mirrored_agent_messages()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "rollout.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            "{\"timestamp\":\"2026-07-10T10:00:00Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\"thread1\",\"cwd\":\"C:/repo\",\"git\":{\"branch\":\"feature\"}}}",
            "{\"timestamp\":\"2026-07-10T10:00:01Z\",\"type\":\"turn_context\",\"payload\":{\"turn_id\":\"turn1\",\"cwd\":\"C:/repo/sub\",\"model\":\"codex-test\"}}",
            "{\"timestamp\":\"2026-07-10T10:00:02Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"turn_id\":\"turn1\"}}",
            "{\"timestamp\":\"2026-07-10T10:00:03Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"Implement this without changing screenshots.\"}}",
            "{\"timestamp\":\"2026-07-10T10:00:04Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"shell_command\",\"call_id\":\"c1\",\"arguments\":\"{\\\"command\\\":\\\"dotnet test\\\"}\"}}",
            "{\"timestamp\":\"2026-07-10T10:00:05Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"c1\",\"output\":\"14 tests passed\"}}",
            "{\"timestamp\":\"2026-07-10T10:00:06Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"Implemented the change.\"}]}}",
            "{\"timestamp\":\"2026-07-10T10:00:07Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\",\"message\":\"Implemented the change.\"}}",
            "{\"timestamp\":\"2026-07-10T10:00:08Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"turn_id\":\"turn1\",\"last_agent_message\":\"Implemented the change.\"}}",
        ]);

        TurnReconstructionResult result = await TurnReconstructor.ReconstructLatestAsync(ProviderKind.Codex, file);

        ReconstructedTurn turn = result.Turn!;
        turn.TurnId.Should().Be("turn1");
        turn.Boundary.Complete.Should().BeTrue();
        turn.WorkingDirectory.Should().Be("C:/repo/sub");
        turn.GitBranch.Should().Be("feature");
        turn.Model.Should().Be("codex-test");
        turn.Segments.Count(segment => segment.Text == "Implemented the change.").Should().Be(1);
        turn.Segments.Should().Contain(segment => segment.Kind == SegmentKind.ToolInvocation);
        turn.Segments.Should().Contain(segment => segment.Kind == SegmentKind.ToolResult);
    }

    [Fact]
    public async Task Missing_provider_stop_marker_never_claims_a_complete_turn()
    {
        using var directory = new TestDirectory();
        string file = Path.Combine(directory.Path, "incomplete.jsonl");
        await File.WriteAllLinesAsync(file,
        [
            "{\"type\":\"user\",\"uuid\":\"u1\",\"sessionId\":\"s1\",\"message\":{\"role\":\"user\",\"content\":\"Please investigate.\"}}",
            "{\"type\":\"assistant\",\"uuid\":\"a1\",\"sessionId\":\"s1\",\"message\":{\"role\":\"assistant\",\"content\":\"Still working.\"}}",
        ]);

        TurnReconstructionResult result = await TurnReconstructor.ReconstructLatestAsync(ProviderKind.Claude, file);

        result.Turn!.Boundary.Complete.Should().BeFalse();
        result.Turn.Boundary.MissingCapabilities.Should().Contain("structured-end-boundary");
    }
}

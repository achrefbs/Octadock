using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class HookPayloadParserTests
{
    [Fact]
    public void Parses_documented_snake_case_stop_payload_without_losing_boundaries()
    {
        const string json =
            """
            {
              "hook_event_name": "Stop",
              "session_id": "session-1",
              "turn_id": "turn-7",
              "prompt_id": "prompt-4",
              "transcript_path": "C:\\internal\\session.jsonl",
              "cwd": "C:\\repo",
              "stop_reason": "end_turn",
              "last_assistant_message": "The complete answer"
            }
            """;

        HookPayload payload = HookPayloadParser.Parse(ProviderKind.Codex, json);

        payload.EventName.Should().Be("Stop");
        payload.SessionId.Should().Be("session-1");
        payload.TurnId.Should().Be("turn-7");
        payload.PromptId.Should().Be("prompt-4");
        payload.LastAssistantMessage.Should().Be("The complete answer");
        payload.StopHookActive.Should().BeFalse();
    }

    [Fact]
    public void Parses_camel_case_payload_and_preserves_structured_message_as_json()
    {
        const string json =
            """
            {
              "hookEventName": "Stop",
              "sessionId": "claude-1",
              "transcriptPath": "C:\\internal\\claude.jsonl",
              "workingDirectory": "C:\\repo",
              "lastAssistantMessage": { "text": "Structured" }
            }
            """;

        HookPayload payload = HookPayloadParser.Parse(ProviderKind.Claude, json);

        payload.SessionId.Should().Be("claude-1");
        payload.LastAssistantMessage.Should().Contain("Structured");
    }

    [Fact]
    public void Codex_recursive_stop_marker_is_parsed_fail_closed()
    {
        const string json =
            """
            {
              "hook_event_name": "Stop",
              "session_id": "s1",
              "turn_id": "t1",
              "stop_hook_active": true,
              "last_assistant_message": "done"
            }
            """;

        HookPayload payload = HookPayloadParser.Parse(ProviderKind.Codex, json);

        payload.StopHookActive.Should().BeTrue();
    }
}

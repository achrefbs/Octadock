using System.Text.Json;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class HookPayloadParser
{
    internal static HookPayload Parse(ProviderKind provider, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Hook payload must not be empty.", nameof(json));
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Hook payload root must be an object.");
        }

        return new HookPayload(
            provider,
            FirstString(root, "hook_event_name", "hookEventName", "event_name", "eventName") ?? "unknown",
            FirstString(root, "session_id", "sessionId", "thread_id", "threadId"),
            FirstString(root, "turn_id", "turnId"),
            FirstString(root, "prompt_id", "promptId"),
            FirstString(root, "transcript_path", "transcriptPath", "rollout_path", "rolloutPath"),
            FirstString(root, "cwd", "working_directory", "workingDirectory"),
            FirstString(root, "stop_reason", "stopReason"),
            FirstBoolean(root, "stop_hook_active", "stopHookActive") ?? false,
            FirstText(root, "last_assistant_message", "lastAssistantMessage"));
    }

    private static string? FirstString(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static string? FirstText(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (!root.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }
        return null;
    }

    private static bool? FirstBoolean(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }
}

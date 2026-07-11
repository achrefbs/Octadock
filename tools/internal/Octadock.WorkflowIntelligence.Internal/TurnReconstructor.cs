using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class TurnReconstructor
{
    internal static async Task<TurnReconstructionResult> ReconstructLatestAsync(
        ProviderKind provider,
        string path,
        string? requestedTurnId = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Provider transcript was not found.", fullPath);
        }

        return provider switch
        {
            ProviderKind.Claude => await ReconstructClaudeAsync(fullPath, requestedTurnId, cancellationToken).ConfigureAwait(false),
            ProviderKind.Codex => await ReconstructCodexAsync(fullPath, requestedTurnId, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    private static async Task<TurnReconstructionResult> ReconstructClaudeAsync(
        string path,
        string? requestedTurnId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string pathHash = HashText(path.ToUpperInvariant());
        TurnBuilder? current = null;
        TurnBuilder? selected = null;
        int turns = 0;
        int completeTurns = 0;
        int parsed = 0;
        int errors = 0;
        var warnings = new List<string>();
        string? sessionId = null;
        string? cwd = null;
        string? gitBranch = null;
        string? model = null;

        try
        {
            await foreach (JsonlRecord record in JsonlRecordReader.ReadAsync(path, cancellationToken: cancellationToken))
            {
                if (record.Utf8.IsEmpty)
                {
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(record.Utf8);
                    JsonElement root = document.RootElement;
                    parsed++;
                    sessionId = FirstString(root, "sessionId", "session_id") ?? sessionId;
                    cwd = FirstString(root, "cwd") ?? cwd;
                    gitBranch = FirstString(root, "gitBranch", "git_branch") ?? gitBranch;
                    DateTimeOffset? timestamp = ParseTimestamp(root);
                    string? recordId = FirstString(root, "uuid", "id");
                    string? type = FirstString(root, "type");
                    if (!root.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    model = FirstString(message, "model") ?? model;
                    string? role = FirstString(message, "role") ?? type;
                    bool toolResult = IsClaudeToolResult(message);
                    bool humanUser = string.Equals(type, "user", StringComparison.Ordinal) && !toolResult;
                    if (humanUser)
                    {
                        FinalizeCandidate(current, requestedTurnId, ref selected, ref turns, ref completeTurns);
                        string identity = $"claude\n{sessionId}\n{recordId ?? record.Ordinal.ToString(CultureInfo.InvariantCulture)}";
                        current = new TurnBuilder(
                            ProviderKind.Claude,
                            pathHash,
                            identity,
                            sessionId,
                            turnId: null,
                            promptId: FirstString(root, "promptId", "prompt_id"),
                            cwd,
                            gitBranch,
                            model,
                            timestamp);
                        current.BoundaryEvidence.Add("Claude structured user record opened the logical turn.");
                        AddClaudeMessage(current, message, SegmentRole.User, record, recordId, "/message/content");
                        continue;
                    }

                    if (current is null)
                    {
                        if (!string.Equals(type, "assistant", StringComparison.Ordinal) && !toolResult)
                        {
                            continue;
                        }
                        string identity = $"claude-orphan\n{sessionId}\n{recordId ?? record.Ordinal.ToString(CultureInfo.InvariantCulture)}";
                        current = new TurnBuilder(
                            ProviderKind.Claude,
                            pathHash,
                            identity,
                            sessionId,
                            null,
                            FirstString(root, "promptId", "prompt_id"),
                            cwd,
                            gitBranch,
                            model,
                            timestamp);
                        current.Warnings.Add("The Claude turn began without a retained human-user boundary.");
                        current.MissingCapabilities.Add("structured-start-boundary");
                    }

                    current.UpdateContext(sessionId, cwd, gitBranch, model);
                    if (toolResult)
                    {
                        AddClaudeToolResults(current, message, record, recordId);
                        continue;
                    }
                    if (!string.Equals(role, "assistant", StringComparison.Ordinal) &&
                        !string.Equals(type, "assistant", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    AddClaudeMessage(current, message, SegmentRole.Assistant, record, recordId, "/message/content");
                    string? stopReason = FirstString(root, "stopReason", "stop_reason") ??
                                         FirstString(message, "stopReason", "stop_reason");
                    if (!string.IsNullOrWhiteSpace(stopReason) &&
                        !string.Equals(stopReason, "tool_use", StringComparison.OrdinalIgnoreCase))
                    {
                        current.MarkComplete(timestamp, $"Claude assistant stop marker: {stopReason}.");
                    }
                }
                catch (JsonException)
                {
                    errors++;
                    current?.Warnings.Add($"Unparseable Claude record at ordinal {record.Ordinal}.");
                }
            }
        }
        catch (OversizedJsonlRecordException exception)
        {
            warnings.Add(exception.Message);
            current?.MarkTruncated("A Claude record exceeded the bounded record limit.");
        }

        FinalizeCandidate(current, requestedTurnId, ref selected, ref turns, ref completeTurns);
        stopwatch.Stop();
        if (selected is null)
        {
            warnings.Add(requestedTurnId is null
                ? "No Claude assistant turn could be reconstructed."
                : $"No Claude turn matched '{requestedTurnId}'.");
        }
        ReconstructedTurn? turn = selected?.Build(parsed, errors, new FileInfo(path).Length, stopwatch.Elapsed);
        return new TurnReconstructionResult(
            turn,
            turns,
            completeTurns,
            parsed,
            errors,
            new FileInfo(path).Length,
            stopwatch.Elapsed,
            warnings.Concat(turn?.Warnings ?? []).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static async Task<TurnReconstructionResult> ReconstructCodexAsync(
        string path,
        string? requestedTurnId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string pathHash = HashText(path.ToUpperInvariant());
        TurnBuilder? current = null;
        TurnBuilder? selected = null;
        int turns = 0;
        int completeTurns = 0;
        int parsed = 0;
        int errors = 0;
        var warnings = new List<string>();
        string? sessionId = null;
        string? cwd = null;
        string? gitBranch = null;
        string? model = null;

        try
        {
            await foreach (JsonlRecord record in JsonlRecordReader.ReadAsync(path, cancellationToken: cancellationToken))
            {
                if (record.Utf8.IsEmpty)
                {
                    continue;
                }
                try
                {
                    using JsonDocument document = JsonDocument.Parse(record.Utf8);
                    JsonElement root = document.RootElement;
                    parsed++;
                    DateTimeOffset? timestamp = ParseTimestamp(root);
                    if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string? topType = FirstString(root, "type");
                    string? payloadType = FirstString(payload, "type");
                    if (string.Equals(topType, "session_meta", StringComparison.Ordinal))
                    {
                        sessionId = FirstString(payload, "id", "session_id", "sessionId") ?? sessionId;
                        cwd = FirstString(payload, "cwd") ?? cwd;
                        gitBranch = GetNestedString(payload, "git", "branch") ??
                                    FirstString(payload, "git_branch", "gitBranch") ?? gitBranch;
                        continue;
                    }

                    string? explicitTurnId = FirstString(payload, "turn_id", "turnId");
                    if (string.Equals(topType, "turn_context", StringComparison.Ordinal))
                    {
                        explicitTurnId ??= FirstString(payload, "id");
                        if (current is null || !Same(current.TurnId, explicitTurnId))
                        {
                            FinalizeCandidate(current, requestedTurnId, ref selected, ref turns, ref completeTurns);
                            current = CreateCodexBuilder(
                                pathHash, sessionId, explicitTurnId, record.Ordinal, cwd, gitBranch, model, timestamp);
                        }
                        cwd = FirstString(payload, "cwd") ?? cwd;
                        model = FirstString(payload, "model") ?? model;
                        current.UpdateContext(sessionId, cwd, gitBranch, model);
                        current.BoundaryEvidence.Add("Codex turn_context identified the logical turn.");
                        continue;
                    }

                    if (string.Equals(payloadType, "task_started", StringComparison.Ordinal))
                    {
                        if (current is null || !Same(current.TurnId, explicitTurnId))
                        {
                            FinalizeCandidate(current, requestedTurnId, ref selected, ref turns, ref completeTurns);
                            current = CreateCodexBuilder(
                                pathHash, sessionId, explicitTurnId, record.Ordinal, cwd, gitBranch, model, timestamp);
                        }
                        current.BoundaryEvidence.Add("Codex task_started opened the exact provider turn.");
                        continue;
                    }

                    if (current is null)
                    {
                        if (payloadType is not ("user_message" or "agent_message" or "message"))
                        {
                            continue;
                        }
                        current = CreateCodexBuilder(
                            pathHash, sessionId, explicitTurnId, record.Ordinal, cwd, gitBranch, model, timestamp);
                        current.Warnings.Add("The Codex turn began without task_started or turn_context.");
                        current.MissingCapabilities.Add("structured-start-boundary");
                    }

                    current.UpdateContext(sessionId, cwd, gitBranch, model);
                    string? recordId = FirstString(payload, "id", "call_id", "callId");
                    switch (payloadType)
                    {
                        case "task_complete":
                            AddSimpleText(
                                current,
                                SegmentKind.AssistantMessage,
                                SegmentRole.Assistant,
                                FirstString(payload, "last_agent_message", "lastAgentMessage"),
                                record,
                                recordId,
                                "/payload/last_agent_message");
                            current.MarkComplete(timestamp, "Codex task_complete closed the exact provider turn.");
                            break;
                        case "user_message":
                            AddSimpleText(
                                current,
                                SegmentKind.UserMessage,
                                SegmentRole.User,
                                FirstText(payload, "message", "text", "content"),
                                record,
                                recordId,
                                "/payload/message");
                            break;
                        case "agent_message":
                            AddSimpleText(
                                current,
                                SegmentKind.AssistantMessage,
                                SegmentRole.Assistant,
                                FirstText(payload, "message", "text", "content"),
                                record,
                                recordId,
                                "/payload/message");
                            break;
                        case "message":
                            AddCodexMessage(current, payload, record, recordId);
                            break;
                        case "function_call":
                        case "custom_tool_call":
                            AddCodexToolInvocation(current, payload, record, recordId, payloadType);
                            break;
                        case "function_call_output":
                        case "custom_tool_call_output":
                            AddSimpleText(
                                current,
                                SegmentKind.ToolResult,
                                SegmentRole.Tool,
                                FirstText(payload, "output", "content", "result"),
                                record,
                                recordId,
                                "/payload/output",
                                new Dictionary<string, string>(StringComparer.Ordinal)
                                {
                                    ["toolResultKind"] = payloadType,
                                });
                            break;
                    }
                }
                catch (JsonException)
                {
                    errors++;
                    current?.Warnings.Add($"Unparseable Codex record at ordinal {record.Ordinal}.");
                }
            }
        }
        catch (OversizedJsonlRecordException exception)
        {
            warnings.Add(exception.Message);
            current?.MarkTruncated("A Codex record exceeded the bounded record limit.");
        }

        FinalizeCandidate(current, requestedTurnId, ref selected, ref turns, ref completeTurns);
        stopwatch.Stop();
        if (selected is null)
        {
            warnings.Add(requestedTurnId is null
                ? "No Codex turn could be reconstructed."
                : $"No Codex turn matched '{requestedTurnId}'.");
        }
        ReconstructedTurn? turn = selected?.Build(parsed, errors, new FileInfo(path).Length, stopwatch.Elapsed);
        return new TurnReconstructionResult(
            turn,
            turns,
            completeTurns,
            parsed,
            errors,
            new FileInfo(path).Length,
            stopwatch.Elapsed,
            warnings.Concat(turn?.Warnings ?? []).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static TurnBuilder CreateCodexBuilder(
        string pathHash,
        string? sessionId,
        string? turnId,
        long ordinal,
        string? cwd,
        string? gitBranch,
        string? model,
        DateTimeOffset? timestamp)
    {
        string identity = $"codex\n{sessionId}\n{turnId ?? ordinal.ToString(CultureInfo.InvariantCulture)}";
        return new TurnBuilder(
            ProviderKind.Codex,
            pathHash,
            identity,
            sessionId,
            turnId,
            promptId: null,
            cwd,
            gitBranch,
            model,
            timestamp);
    }

    private static void FinalizeCandidate(
        TurnBuilder? builder,
        string? requestedTurnId,
        ref TurnBuilder? selected,
        ref int turns,
        ref int completeTurns)
    {
        if (builder is null || builder.SegmentCount == 0)
        {
            return;
        }
        turns++;
        if (builder.Complete)
        {
            completeTurns++;
        }
        if (requestedTurnId is null || builder.Matches(requestedTurnId))
        {
            selected = builder;
        }
    }

    private static void AddClaudeMessage(
        TurnBuilder builder,
        JsonElement message,
        SegmentRole role,
        JsonlRecord record,
        string? recordId,
        string pointer)
    {
        if (!message.TryGetProperty("content", out JsonElement content))
        {
            return;
        }
        if (content.ValueKind == JsonValueKind.String)
        {
            AddSimpleText(
                builder,
                role == SegmentRole.User ? SegmentKind.UserMessage : SegmentKind.AssistantMessage,
                role,
                content.GetString(),
                record,
                recordId,
                pointer);
            return;
        }
        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int index = 0;
        foreach (JsonElement block in content.EnumerateArray())
        {
            string blockPointer = $"{pointer}/{index}";
            index++;
            if (block.ValueKind == JsonValueKind.String)
            {
                AddSimpleText(
                    builder,
                    role == SegmentRole.User ? SegmentKind.UserMessage : SegmentKind.AssistantMessage,
                    role,
                    block.GetString(),
                    record,
                    recordId,
                    blockPointer);
                continue;
            }
            if (block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            string? type = FirstString(block, "type");
            switch (type)
            {
                case "text":
                case "input_text":
                case "output_text":
                    AddSimpleText(
                        builder,
                        role == SegmentRole.User ? SegmentKind.UserMessage : SegmentKind.AssistantMessage,
                        role,
                        FirstText(block, "text", "content"),
                        record,
                        recordId,
                        $"{blockPointer}/text");
                    break;
                case "tool_use":
                    string name = FirstString(block, "name") ?? "unknown-tool";
                    string input = block.TryGetProperty("input", out JsonElement toolInput)
                        ? toolInput.GetRawText()
                        : string.Empty;
                    AddSimpleText(
                        builder,
                        SegmentKind.ToolInvocation,
                        SegmentRole.Tool,
                        string.IsNullOrEmpty(input) ? name : $"{name}: {input}",
                        record,
                        FirstString(block, "id") ?? recordId,
                        blockPointer,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["toolName"] = name });
                    break;
                case "thinking":
                case "redacted_thinking":
                    builder.ReasoningBlocksExcluded++;
                    break;
            }
        }
    }

    private static void AddClaudeToolResults(
        TurnBuilder builder,
        JsonElement message,
        JsonlRecord record,
        string? recordId)
    {
        if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        int index = 0;
        foreach (JsonElement block in content.EnumerateArray())
        {
            string pointer = $"/message/content/{index}";
            index++;
            if (block.ValueKind != JsonValueKind.Object ||
                !string.Equals(FirstString(block, "type"), "tool_result", StringComparison.Ordinal))
            {
                continue;
            }
            bool isError = FirstBoolean(block, "is_error", "isError") ?? false;
            string? text = block.TryGetProperty("content", out JsonElement result)
                ? ExtractText(result)
                : null;
            AddSimpleText(
                builder,
                isError ? SegmentKind.Error : SegmentKind.ToolResult,
                SegmentRole.Tool,
                text,
                record,
                FirstString(block, "tool_use_id", "toolUseId") ?? recordId,
                pointer,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["isError"] = isError.ToString(CultureInfo.InvariantCulture),
                });
        }
    }

    private static void AddCodexMessage(
        TurnBuilder builder,
        JsonElement payload,
        JsonlRecord record,
        string? recordId)
    {
        string? roleText = FirstString(payload, "role");
        SegmentRole role = string.Equals(roleText, "user", StringComparison.Ordinal)
            ? SegmentRole.User
            : SegmentRole.Assistant;
        SegmentKind kind = role == SegmentRole.User ? SegmentKind.UserMessage : SegmentKind.AssistantMessage;
        if (!payload.TryGetProperty("content", out JsonElement content))
        {
            return;
        }
        if (content.ValueKind == JsonValueKind.String)
        {
            AddSimpleText(builder, kind, role, content.GetString(), record, recordId, "/payload/content");
            return;
        }
        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        int index = 0;
        foreach (JsonElement block in content.EnumerateArray())
        {
            string pointer = $"/payload/content/{index}";
            index++;
            if (block.ValueKind == JsonValueKind.String)
            {
                AddSimpleText(builder, kind, role, block.GetString(), record, recordId, pointer);
                continue;
            }
            if (block.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            string? blockType = FirstString(block, "type");
            if (blockType is "reasoning" or "thinking" or "summary_text")
            {
                builder.ReasoningBlocksExcluded++;
                continue;
            }
            AddSimpleText(
                builder,
                kind,
                role,
                FirstText(block, "text", "content"),
                record,
                recordId,
                pointer);
        }
    }

    private static void AddCodexToolInvocation(
        TurnBuilder builder,
        JsonElement payload,
        JsonlRecord record,
        string? recordId,
        string payloadType)
    {
        string name = FirstString(payload, "name", "tool_name", "toolName") ?? payloadType;
        string? arguments = FirstText(payload, "arguments", "input", "args");
        AddSimpleText(
            builder,
            SegmentKind.ToolInvocation,
            SegmentRole.Tool,
            string.IsNullOrWhiteSpace(arguments) ? name : $"{name}: {arguments}",
            record,
            recordId,
            "/payload",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["toolName"] = name });
    }

    private static void AddSimpleText(
        TurnBuilder builder,
        SegmentKind kind,
        SegmentRole role,
        string? text,
        JsonlRecord record,
        string? recordId,
        string pointer,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        builder.AddSegment(kind, role, text, record, recordId, pointer, attributes);
    }

    private static bool IsClaudeToolResult(JsonElement message)
    {
        if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object &&
                string.Equals(FirstString(block, "type"), "tool_result", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string? ExtractText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        if (value.ValueKind != JsonValueKind.Array)
        {
            return value.ValueKind is JsonValueKind.Object ? value.GetRawText() : null;
        }
        var parts = new List<string>();
        foreach (JsonElement part in value.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                parts.Add(part.GetString() ?? string.Empty);
            }
            else if (part.ValueKind == JsonValueKind.Object && FirstText(part, "text", "content") is { } text)
            {
                parts.Add(text);
            }
        }
        return string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string? FirstText(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out JsonElement value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }
            return value.ValueKind == JsonValueKind.String ? value.GetString() : ExtractText(value) ?? value.GetRawText();
        }
        return null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static bool? FirstBoolean(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                continue;
            }
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static string? GetNestedString(JsonElement element, string parent, string child)
        => element.TryGetProperty(parent, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object
            ? FirstString(nested, child)
            : null;

    private static DateTimeOffset? ParseTimestamp(JsonElement root)
        => FirstString(root, "timestamp", "created_at", "createdAt") is { } value &&
           DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed
            : null;

    private static bool Same(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.Ordinal);

    private static string HashText(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TurnBuilder
    {
        private const int MaximumSegmentCharacters = 512 * 1024;
        private const int MaximumTurnCharacters = 16 * 1024 * 1024;
        private readonly List<ContentSegment> _segments = [];
        private readonly HashSet<string> _deduplication = new(StringComparer.Ordinal);
        private int _characters;
        private bool _truncated;

        internal TurnBuilder(
            ProviderKind provider,
            string pathHash,
            string identity,
            string? sessionId,
            string? turnId,
            string? promptId,
            string? cwd,
            string? gitBranch,
            string? model,
            DateTimeOffset? startedAt)
        {
            Provider = provider;
            PathHash = pathHash;
            IdentityHash = HashText(identity);
            DocumentId = StableGuid(IdentityHash);
            SessionId = sessionId;
            TurnId = turnId;
            PromptId = promptId;
            WorkingDirectory = cwd;
            GitBranch = gitBranch;
            Model = model;
            StartedAt = startedAt;
        }

        internal ProviderKind Provider { get; }

        internal string PathHash { get; }

        internal string IdentityHash { get; }

        internal Guid DocumentId { get; }

        internal string? SessionId { get; private set; }

        internal string? TurnId { get; }

        internal string? PromptId { get; }

        internal string? WorkingDirectory { get; private set; }

        internal string? GitBranch { get; private set; }

        internal string? Model { get; private set; }

        internal DateTimeOffset? StartedAt { get; }

        internal DateTimeOffset? CompletedAt { get; private set; }

        internal bool Complete { get; private set; }

        internal int SegmentCount => _segments.Count;

        internal int ReasoningBlocksExcluded { get; set; }

        internal List<string> BoundaryEvidence { get; } = [];

        internal List<string> MissingCapabilities { get; } = [];

        internal List<string> Warnings { get; } = [];

        internal void UpdateContext(string? sessionId, string? cwd, string? gitBranch, string? model)
        {
            SessionId ??= sessionId;
            WorkingDirectory = cwd ?? WorkingDirectory;
            GitBranch = gitBranch ?? GitBranch;
            Model = model ?? Model;
        }

        internal void MarkComplete(DateTimeOffset? completedAt, string evidence)
        {
            Complete = true;
            CompletedAt = completedAt ?? CompletedAt;
            BoundaryEvidence.Add(evidence);
        }

        internal void MarkTruncated(string warning)
        {
            _truncated = true;
            Complete = false;
            Warnings.Add(warning);
            MissingCapabilities.Add("bounded-complete-content");
        }

        internal bool Matches(string requested)
            => string.Equals(TurnId, requested, StringComparison.Ordinal) ||
               string.Equals(PromptId, requested, StringComparison.Ordinal) ||
               string.Equals(IdentityHash, requested, StringComparison.OrdinalIgnoreCase);

        internal void AddSegment(
            SegmentKind kind,
            SegmentRole role,
            string text,
            JsonlRecord record,
            string? recordId,
            string pointer,
            IReadOnlyDictionary<string, string>? attributes)
        {
            string normalized = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }
            string hash = HashText(normalized);
            string dedupeKey = $"{kind}\n{role}\n{hash}";
            if (!_deduplication.Add(dedupeKey))
            {
                return;
            }
            if (normalized.Length > MaximumSegmentCharacters || _characters + normalized.Length > MaximumTurnCharacters)
            {
                MarkTruncated("Turn content exceeded the bounded in-memory reconstruction limit.");
                return;
            }

            int ordinal = _segments.Count;
            var anchor = new SourceAnchor(
                Provider,
                PathHash,
                record.Ordinal,
                record.ByteStart,
                record.ByteEndExclusive,
                recordId,
                pointer,
                0,
                normalized.Length,
                record.Sha256,
                hash,
                hash);
            _segments.Add(new ContentSegment(
                StableGuid($"{IdentityHash}\n{ordinal}\n{hash}"),
                DocumentId,
                ordinal,
                kind,
                role,
                normalized,
                hash,
                anchor,
                1,
                attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)));
            _characters += normalized.Length;
        }

        internal ReconstructedTurn Build(int parsed, int errors, long bytesRead, TimeSpan duration)
        {
            if (ReasoningBlocksExcluded > 0)
            {
                Warnings.Add($"{ReasoningBlocksExcluded} private reasoning block(s) were deliberately excluded.");
            }
            if (!Complete)
            {
                MissingCapabilities.Add("structured-end-boundary");
                Warnings.Add("The selected turn has no trustworthy completion boundary.");
            }
            if (errors > 0)
            {
                Warnings.Add($"{errors} source record(s) could not be parsed; confidence was reduced.");
            }
            double confidence = Complete && !_truncated && errors == 0 &&
                                !MissingCapabilities.Contains("structured-start-boundary", StringComparer.Ordinal)
                ? 0.99
                : Complete && !_truncated ? 0.82 : 0.5;
            var boundary = new BoundaryResult(
                DocumentId,
                "assistant_turn",
                StartedAt,
                CompletedAt,
                Complete && !_truncated,
                confidence,
                BoundaryEvidence.Distinct(StringComparer.Ordinal).ToArray(),
                MissingCapabilities.Distinct(StringComparer.Ordinal).ToArray(),
                Warnings.Distinct(StringComparer.Ordinal).ToArray());
            return new ReconstructedTurn(
                DocumentId,
                Provider,
                IdentityHash,
                PathHash,
                SessionId,
                TurnId,
                PromptId,
                WorkingDirectory,
                GitBranch,
                Model,
                boundary,
                _segments.ToArray(),
                parsed,
                errors,
                bytesRead,
                duration,
                Warnings.Distinct(StringComparer.Ordinal).ToArray());
        }

        private static string Normalize(string value)
            => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

        private static Guid StableGuid(string value)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}

using FluentAssertions;
using Octadock.App.Services;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AiSessionProcessClassifierTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 3, 12, 5, 0, TimeSpan.Zero);

    [Fact]
    public void ClassifyProcess_ignores_main_codex_desktop_window()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "Codex",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.623.13972.0_x64__2p2nqsd0c76g0\app\Codex.exe",
            "Codex");

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_ignores_codex_electron_helpers_without_window_title()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "Codex",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.623.13972.0_x64__2p2nqsd0c76g0\app\Codex.exe",
            string.Empty);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_ignores_codex_app_server()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "codex",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.623.13972.0_x64__2p2nqsd0c76g0\app\resources\codex.exe",
            null);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_detects_codex_runtime_session_node()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "node",
            @"C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe",
            null,
            @"""C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe"" --session-id abc123 --working-dir C:\Users\acera\Desktop\Workspace\Octadock");

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.Codex);
        candidate.Title.Should().Be("Codex - Octadock");
        candidate.Detector.Should().Be("codex-runtime-session");
        candidate.SessionId.Should().Be("abc123");
        candidate.WorkingDirectory.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
    }

    [Fact]
    public void SelectCanonicalCandidates_suppresses_runtime_when_latest_workspace_thread_is_inactive()
    {
        var candidate = new AiSessionProcessCandidate(
            AiSessionProvider.Codex,
            "Codex - Roamcaster",
            "node kernel.js --session-id abc123 --working-dir C:\\Users\\acera\\Desktop\\Workspace\\Roamcaster",
            11552,
            StartedAt,
            "codex-runtime-session:abc123",
            "node",
            @"C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node.exe",
            null,
            "codex-runtime-session",
            null,
            "abc123",
            @"C:\Users\acera\Desktop\Workspace\Roamcaster",
            StartedAt,
            null);

        IReadOnlyList<AiSessionProcessCandidate> candidates =
            AiSessionDiscoveryService.SelectCanonicalCandidates(
                [candidate],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"C:\Users\acera\Desktop\Workspace\Roamcaster",
                });

        candidates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"C:\Users\acera\AppData\Local\Programs\Ollama\ollama.exe runner --model C:\models\llama3.2-8b.gguf --port 51234", "ollama-runner", "Ollama - llama3.2-8b")]
    [InlineData(@"""C:\Program Files\Ollama\ollama.exe"" serve", "ollama-server", "Ollama server")]
    public void ClassifyProcess_detects_long_lived_ollama_processes(string commandLine, string detector, string title)
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyProcess(
            new AiSessionProcessSnapshot(
                4321,
                "ollama",
                @"C:\Program Files\Ollama\ollama.exe",
                null,
                StartedAt,
                null,
                commandLine),
            ObservedAt);

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.Ollama);
        candidate.Detector.Should().Be(detector);
        candidate.Title.Should().Be(title);
    }

    [Fact]
    public void ClassifyProcess_ignores_one_shot_ollama_cli_calls()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyProcess(
            new AiSessionProcessSnapshot(
                4321,
                "ollama",
                @"C:\Program Files\Ollama\ollama.exe",
                null,
                StartedAt,
                null,
                @"ollama.exe list"),
            ObservedAt);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_detects_cursor_agent()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyProcess(
            new AiSessionProcessSnapshot(
                777,
                "cursor-agent",
                @"C:\Users\acera\.local\bin\cursor-agent.exe",
                null,
                StartedAt,
                null,
                @"cursor-agent.exe --cwd C:\Users\acera\Desktop\Workspace\Octadock"),
            ObservedAt);

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.Cursor);
        candidate.Detector.Should().Be("cursor-agent");
        candidate.Title.Should().Be("Cursor agent - Octadock");
    }

    [Fact]
    public void ClassifyProcess_detects_copilot_cli_under_node()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyProcess(
            new AiSessionProcessSnapshot(
                888,
                "node",
                @"C:\Program Files\nodejs\node.exe",
                null,
                StartedAt,
                null,
                @"node C:\Users\acera\AppData\Roaming\npm\node_modules\@github/copilot\index.js"),
            ObservedAt);

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.GitHubCopilot);
        candidate.Detector.Should().Be("copilot-cli");
    }

    [Fact]
    public void ClassifyProcess_detects_gemini_cli_under_node()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyProcess(
            new AiSessionProcessSnapshot(
                999,
                "node",
                @"C:\Program Files\nodejs\node.exe",
                null,
                StartedAt,
                null,
                @"node C:\Users\acera\AppData\Roaming\npm\node_modules\@google\gemini-cli\dist\index.js"),
            ObservedAt);

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.Gemini);
        candidate.Detector.Should().Be("gemini-cli");
    }

    [Fact]
    public void DropChildClaudeCandidates_removes_workers_whose_parent_is_also_claude()
    {
        AiSessionProcessCandidate parent = ClaudeCandidate(pid: 100, parentPid: 1);
        AiSessionProcessCandidate child = ClaudeCandidate(pid: 200, parentPid: 100);
        AiSessionProcessCandidate unrelated = ClaudeCandidate(pid: 300, parentPid: 999);

        IReadOnlyList<AiSessionProcessCandidate> result =
            AiSessionDiscoveryService.DropChildClaudeCandidates([parent, child, unrelated]);

        result.Should().BeEquivalentTo(new[] { parent, unrelated });
    }

    [Fact]
    public void DropChildClaudeCandidates_keeps_single_sessions_untouched()
    {
        AiSessionProcessCandidate only = ClaudeCandidate(pid: 100, parentPid: 1);

        AiSessionDiscoveryService.DropChildClaudeCandidates([only])
            .Should().BeEquivalentTo(new[] { only });
    }

    private static AiSessionProcessCandidate ClaudeCandidate(int pid, int parentPid)
        => new(
            AiSessionProvider.ClaudeCode,
            "Claude Code",
            "node claude-code",
            pid,
            StartedAt,
            $"claude-code:{pid}",
            "node",
            @"C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js",
            null,
            "claude-code",
            parentPid,
            null,
            @"C:\Users\acera\Desktop\Workspace\Octadock",
            StartedAt,
            null);

    [Fact]
    public void ClassifyCodexThread_detects_recent_unarchived_desktop_thread()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019f250b-451f-7b70-af17-c2f289897e85",
                "Long prompt",
                @"\\?\C:\Users\acera\Desktop\Workspace\Octadock",
                ObservedAt.AddMinutes(-10).ToUnixTimeSeconds(),
                ObservedAt.AddMinutes(-1).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Active,
                ObservedAt.AddSeconds(-30),
                null,
                null,
                null,
                "vscode",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.Codex);
        candidate.Title.Should().Be("Codex - Octadock");
        candidate.Detector.Should().Be("codex-state-thread");
        candidate.Pid.Should().BeNull();
        candidate.SessionId.Should().Be("019f250b-451f-7b70-af17-c2f289897e85");
        candidate.WorkingDirectory.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
    }

    [Fact]
    public void ClassifyCodexThread_detects_open_subagent_with_nickname()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019f28b2-445d-7560-9419-b0ec8544e0a1",
                "Audit",
                @"C:\Users\acera\Desktop\Workspace\Roamcaster",
                ObservedAt.AddHours(-2).ToUnixTimeSeconds(),
                ObservedAt.AddHours(-1).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Active,
                ObservedAt.AddSeconds(-30),
                "open",
                "019f28af-c3c8-7bf2-b097-fd609f664121",
                ObservedAt.AddMinutes(-2).ToUnixTimeSeconds(),
                "{\"subagent\":{\"thread_spawn\":{\"agent_nickname\":\"Huygens\"}}}",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().NotBeNull();
        candidate!.Title.Should().Be("Codex subagent - Huygens");
        candidate.Detector.Should().Be("codex-state-thread");
    }

    [Fact]
    public void ClassifyCodexThread_ignores_closed_subagent()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019f25d9-4fed-76a1-94f4-cb64a921669f",
                "Plan next implementation slices",
                @"C:\Users\acera\Desktop\Workspace\Octadock",
                ObservedAt.AddHours(-2).ToUnixTimeSeconds(),
                ObservedAt.AddMinutes(-1).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Active,
                ObservedAt.AddMinutes(-1),
                "closed",
                "019f250b-451f-7b70-af17-c2f289897e85",
                ObservedAt.AddMinutes(-2).ToUnixTimeSeconds(),
                "{\"subagent\":{\"thread_spawn\":{\"agent_nickname\":\"Dirac\"}}}",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyCodexThread_ignores_stale_open_subagent_when_parent_is_stale()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019e2b7d-9349-74f1-96f1-640aa8407dd8",
                "Old audit",
                @"C:\Users\acera\.codex\worktrees\f4c7\AgentOS-dev",
                ObservedAt.AddDays(-40).ToUnixTimeSeconds(),
                ObservedAt.AddDays(-40).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Active,
                ObservedAt.AddDays(-40),
                "open",
                "019e2b7c-c067-76f1-8209-90923d6ace84",
                ObservedAt.AddDays(-40).ToUnixTimeSeconds(),
                "{\"subagent\":{\"thread_spawn\":{\"agent_nickname\":\"Pasteur\"}}}",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyCodexThread_ignores_stale_top_level_thread()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019f2503-b1f4-7d33-95d7-ef20f6ce2321",
                "Old thread",
                @"C:\Users\acera\Desktop\Workspace\Octadock",
                ObservedAt.AddHours(-4).ToUnixTimeSeconds(),
                ObservedAt.AddHours(-1).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Active,
                ObservedAt.AddMinutes(-1),
                null,
                null,
                null,
                "vscode",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyCodexThread_ignores_completed_rollout_thread()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019f28af-c3c8-7bf2-b097-fd609f664121",
                "Roamcaster engineering handover",
                @"C:\Users\acera\Desktop\Workspace\Roamcaster",
                ObservedAt.AddHours(-2).ToUnixTimeSeconds(),
                ObservedAt.AddMinutes(-1).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Completed,
                ObservedAt.AddMinutes(-1),
                null,
                null,
                null,
                "vscode",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyCodexThread_ignores_idle_active_rollout_thread()
    {
        AiSessionProcessCandidate? candidate = AiSessionDiscoveryService.ClassifyCodexThread(
            new AiSessionCodexThreadSnapshot(
                "019f250b-451f-7b70-af17-c2f289897e85",
                "Long prompt",
                @"C:\Users\acera\Desktop\Workspace\Octadock",
                ObservedAt.AddHours(-2).ToUnixTimeSeconds(),
                ObservedAt.AddMinutes(-1).ToUnixTimeSeconds(),
                false,
                AiSessionCodexThreadRunState.Active,
                ObservedAt.AddMinutes(-3),
                null,
                null,
                null,
                "vscode",
                "gpt-5.5",
                "openai",
                @"C:\Users\acera\.codex\state_5.sqlite"),
            ObservedAt);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_ignores_codex_runtime_node_without_session_id()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "node",
            @"C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe",
            null,
            @"""C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe"" kernel.js");

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_detects_claude_code_managed_install()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "claude",
            @"C:\Users\acera\AppData\Roaming\Claude\claude-code\2.1.197\claude.exe",
            null);

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.ClaudeCode);
        candidate.Title.Should().Be("Claude Code");
        candidate.Detector.Should().Be("claude-code");
    }

    [Fact]
    public void ClassifyProcess_ignores_claude_desktop_electron_helpers()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "claude",
            @"C:\Users\acera\AppData\Local\AnthropicClaude\app-1.0.0\claude.exe",
            string.Empty);

        candidate.Should().BeNull();
    }

    [Fact]
    public void ClassifyProcess_detects_claude_code_global_install_when_not_native_host()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "claude",
            @"C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe",
            null,
            @"""C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe"" --model claude-opus-4-8");

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.ClaudeCode);
        candidate.Detector.Should().Be("claude-code");
    }

    [Fact]
    public void ClassifyProcess_detects_claude_code_node_worker()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            null,
            @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js --cwd C:\Users\acera\Desktop\Workspace\Octadock");

        candidate.Should().NotBeNull();
        candidate!.Provider.Should().Be(AiSessionProvider.ClaudeCode);
        candidate.Title.Should().Be("Claude Code - Octadock");
        candidate.Detector.Should().Be("claude-code");
        candidate.WorkingDirectory.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
    }

    [Fact]
    public void ClassifyProcess_ignores_claude_code_native_host_path()
    {
        AiSessionProcessCandidate? candidate = Classify(
            "claude",
            @"C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe",
            null,
            @"""C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe"" --chrome-native-host");

        candidate.Should().BeNull();
    }

    private static AiSessionProcessCandidate? Classify(
        string processName,
        string executablePath,
        string? mainWindowTitle,
        string? commandLine = null)
        => AiSessionDiscoveryService.ClassifyProcess(
            new AiSessionProcessSnapshot(
                1234,
                processName,
                executablePath,
                mainWindowTitle,
                StartedAt,
                null,
                commandLine),
            ObservedAt);
}

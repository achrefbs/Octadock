using FluentAssertions;
using Octadock.App.Services.AiSessionDiscovery;
using Octadock.Core.Models;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AiSessionProcessClassifierTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 3, 12, 5, 0, TimeSpan.Zero);

    // ---- Codex family ----

    [Fact]
    public void Rejects_main_codex_desktop_window()
    {
        AiSessionProcessClassification? verdict = Classify(
            "Codex",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.623.13972.0_x64__2p2nqsd0c76g0\app\Codex.exe",
            mainWindowTitle: "Codex");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("codex-desktop-shell");
        verdict.RejectionReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Rejects_codex_electron_helpers()
    {
        AiSessionProcessClassification? verdict = Classify(
            "Codex",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.623.13972.0_x64__2p2nqsd0c76g0\app\Codex.exe",
            commandLine: @"Codex.exe --type=renderer");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void Rejects_codex_app_server()
    {
        AiSessionProcessClassification? verdict = Classify(
            "codex",
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.623.13972.0_x64__2p2nqsd0c76g0\app\resources\codex.exe");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("codex-desktop-shell");
    }

    [Fact]
    public void Detects_codex_runtime_session_node_and_marks_it_for_corroboration()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe",
            commandLine: @"""C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe"" --session-id abc123 --working-dir C:\Users\acera\Desktop\Workspace\Octadock");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        AiSessionEvidence evidence = verdict.Evidence!;
        evidence.Provider.Should().Be(AiSessionProvider.Codex);
        evidence.Detector.Should().Be("codex-runtime-session");
        evidence.Title.Should().Be("Codex - Octadock");
        evidence.ProviderSessionId.Should().Be("abc123");
        evidence.WorkspacePath.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
        evidence.RequiresCorroboration.Should().BeTrue();
        evidence.CorroborationSource.Should().Be(CodexStateEvidenceCollector.SourceId);
        evidence.Confidence.Should().BeGreaterThanOrEqualTo(AiSessionConfidence.MinimumToShow);
    }

    [Fact]
    public void Rejects_codex_runtime_node_without_session_id()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe",
            commandLine: @"""C:\Users\acera\AppData\Local\OpenAI\Codex\runtimes\cua_node\node-v22.16.0-win-x64\bin\node.exe"" kernel.js");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("codex-runtime-idle");
    }

    [Fact]
    public void Detects_standalone_codex_cli()
    {
        AiSessionProcessClassification? verdict = Classify(
            "codex",
            @"C:\Users\acera\.cargo\bin\codex.exe",
            commandLine: @"codex --cd C:\Users\acera\Desktop\Workspace\Roamcaster");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Detector.Should().Be("codex-cli");
        verdict.Evidence.Provider.Should().Be(AiSessionProvider.Codex);
        verdict.Evidence.WorkspacePath.Should().Be(@"C:\Users\acera\Desktop\Workspace\Roamcaster");
    }

    [Fact]
    public void Rejects_codex_cli_helper_modes_but_not_paths_containing_those_words()
    {
        AiSessionProcessClassification? helper = Classify(
            "codex",
            @"C:\Users\acera\.cargo\bin\codex.exe",
            commandLine: @"""C:\Users\acera\.cargo\bin\codex.exe"" mcp-server");
        AiSessionProcessClassification? interactive = Classify(
            "codex",
            @"C:\Users\acera\.cargo\bin\codex.exe",
            commandLine: @"""C:\Users\acera\.cargo\bin\codex.exe"" --cd ""C:\Users\acera\login scripts""");

        helper.Should().NotBeNull();
        helper!.IsAccepted.Should().BeFalse();
        helper.RejectionDetector.Should().Be("codex-cli-helper");

        interactive.Should().NotBeNull();
        interactive!.IsAccepted.Should().BeTrue();
    }

    // ---- Claude Code family ----

    [Fact]
    public void Detects_claude_code_managed_install()
    {
        AiSessionProcessClassification? verdict = Classify(
            "claude",
            @"C:\Users\acera\AppData\Roaming\Claude\claude-code\2.1.197\claude.exe");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Provider.Should().Be(AiSessionProvider.ClaudeCode);
        verdict.Evidence.Detector.Should().Be("claude-code-cli");
        verdict.Evidence.Title.Should().Be("Claude Code");
        verdict.Evidence.Confidence.Should().Be(AiSessionConfidence.Certain);
    }

    [Fact]
    public void Rejects_claude_desktop_electron_process()
    {
        AiSessionProcessClassification? verdict = Classify(
            "claude",
            @"C:\Users\acera\AppData\Local\AnthropicClaude\app-1.0.0\claude.exe");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("claude-desktop-shell");
    }

    [Fact]
    public void Detects_claude_code_global_install()
    {
        AiSessionProcessClassification? verdict = Classify(
            "claude",
            @"C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe",
            commandLine: @"""C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe"" --model claude-opus-4-8");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Detector.Should().Be("claude-code-cli");
    }

    [Fact]
    public void Detects_claude_code_node_worker_with_cwd()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js --cwd C:\Users\acera\Desktop\Workspace\Octadock");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Provider.Should().Be(AiSessionProvider.ClaudeCode);
        verdict.Evidence.Detector.Should().Be("claude-code-node");
        verdict.Evidence.Title.Should().Be("Claude Code - Octadock");
        verdict.Evidence.WorkspacePath.Should().Be(@"C:\Users\acera\Desktop\Workspace\Octadock");
    }

    [Fact]
    public void Rejects_claude_code_native_messaging_host()
    {
        AiSessionProcessClassification? verdict = Classify(
            "claude",
            @"C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe",
            commandLine: @"""C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe"" --chrome-native-host");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("claude-native-host");
    }

    [Fact]
    public void Rejects_claude_exe_at_unknown_location_with_explicit_reason()
    {
        AiSessionProcessClassification? verdict = Classify(
            "claude",
            @"C:\Tools\claude.exe");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("claude-unrecognized-install");
        verdict.RejectionReason.Should().Contain("false positives");
    }

    // ---- Cursor family ----

    [Fact]
    public void Detects_cursor_agent_cli()
    {
        AiSessionProcessClassification? verdict = Classify(
            "cursor-agent",
            @"C:\Users\acera\AppData\Local\Programs\cursor-agent\cursor-agent.exe",
            commandLine: @"""C:\Users\acera\AppData\Local\Programs\cursor-agent\cursor-agent.exe""");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Provider.Should().Be(AiSessionProvider.Cursor);
        verdict.Evidence.Detector.Should().Be("cursor-agent-cli");
        verdict.Evidence.Confidence.Should().BeGreaterThanOrEqualTo(AiSessionConfidence.Strong);

        // Without a command line the same process is still detected, but at a
        // lower confidence because helper modes cannot be ruled out.
        AiSessionProcessClassification? withoutCommandLine = Classify(
            "cursor-agent",
            @"C:\Users\acera\AppData\Local\Programs\cursor-agent\cursor-agent.exe");
        withoutCommandLine!.IsAccepted.Should().BeTrue();
        withoutCommandLine.Evidence!.Confidence.Should().Be(AiSessionConfidence.NeedsCorroboration);
    }

    [Fact]
    public void Rejects_cursor_editor_and_its_electron_helpers()
    {
        AiSessionProcessClassification? main = Classify(
            "Cursor",
            @"C:\Users\acera\AppData\Local\Programs\cursor\Cursor.exe");
        AiSessionProcessClassification? helper = Classify(
            "Cursor",
            @"C:\Users\acera\AppData\Local\Programs\cursor\Cursor.exe",
            commandLine: @"Cursor.exe --type=gpu-process");

        main.Should().NotBeNull();
        main!.IsAccepted.Should().BeFalse();
        main.RejectionDetector.Should().Be("cursor-editor-shell");

        helper.Should().NotBeNull();
        helper!.IsAccepted.Should().BeFalse();
        helper.RejectionDetector.Should().Be("cursor-electron-helper");
    }

    // ---- Copilot family ----

    [Fact]
    public void Detects_copilot_cli_node_worker()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Roaming\npm\node_modules\@github\copilot\index.js");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Provider.Should().Be(AiSessionProvider.GitHubCopilot);
        verdict.Evidence.Detector.Should().Be("copilot-cli");
    }

    [Fact]
    public void Rejects_windows_copilot_app_and_unrecognized_copilot_binaries()
    {
        AiSessionProcessClassification? windowsApp = Classify(
            "Copilot",
            @"C:\Program Files\WindowsApps\Microsoft.Copilot_1.25.0_x64__8wekyb3d8bbwe\Copilot.exe");
        AiSessionProcessClassification? unknown = Classify(
            "copilot",
            @"C:\Tools\copilot.exe");

        windowsApp.Should().NotBeNull();
        windowsApp!.IsAccepted.Should().BeFalse();
        windowsApp.RejectionDetector.Should().Be("windows-copilot-app");

        unknown.Should().NotBeNull();
        unknown!.IsAccepted.Should().BeFalse();
        unknown.RejectionDetector.Should().Be("copilot-unrecognized-install");
    }

    [Fact]
    public void Rejects_editor_embedded_copilot_agents_regardless_of_editor_layout()
    {
        AiSessionProcessClassification? jetbrains = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Local\JetBrains\IntelliJIdea2026.1\plugins\github-copilot-intellij\copilot-agent\dist\agent.js");

        jetbrains.Should().NotBeNull();
        jetbrains!.IsAccepted.Should().BeFalse();
        jetbrains.RejectionDetector.Should().Be("copilot-extension-worker");
    }

    [Fact]
    public void Rejects_claude_code_mcp_server_mode()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js mcp serve");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("claude-code-mcp-server");
    }

    [Fact]
    public void Rejects_copilot_language_server_with_explicit_reason()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\.vscode\extensions\github.copilot-1.250.0\dist\copilot-language-server.js --stdio");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("copilot-language-server");
        verdict.RejectionReason.Should().NotBeNullOrWhiteSpace();
    }

    // ---- Gemini family ----

    [Fact]
    public void Detects_gemini_cli_node_worker()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Roaming\npm\node_modules\@google\gemini-cli\dist\index.js");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Provider.Should().Be(AiSessionProvider.Gemini);
        verdict.Evidence.Detector.Should().Be("gemini-cli");
    }

    [Fact]
    public void Rejects_gemini_ide_bridge()
    {
        AiSessionProcessClassification? verdict = Classify(
            "node",
            @"C:\Program Files\nodejs\node.exe",
            commandLine: @"""C:\Program Files\nodejs\node.exe"" C:\Users\acera\AppData\Roaming\npm\node_modules\@google\gemini-cli\dist\index.js --experimental-acp");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("gemini-ide-bridge");
    }

    // ---- Ollama family ----

    [Fact]
    public void Detects_long_lived_ollama_runner_and_server()
    {
        AiSessionProcessClassification? runner = Classify(
            "ollama",
            @"C:\Users\acera\AppData\Local\Programs\Ollama\ollama.exe",
            commandLine: @"C:\Users\acera\AppData\Local\Programs\Ollama\ollama.exe runner --model C:\models\llama3.2-8b.gguf --port 51234");
        AiSessionProcessClassification? server = Classify(
            "ollama",
            @"C:\Program Files\Ollama\ollama.exe",
            commandLine: @"""C:\Program Files\Ollama\ollama.exe"" serve");

        runner.Should().NotBeNull();
        runner!.IsAccepted.Should().BeTrue();
        runner.Evidence!.Provider.Should().Be(AiSessionProvider.Ollama);
        runner.Evidence.Detector.Should().Be("ollama-runner");
        runner.Evidence.Title.Should().Be("Ollama - llama3.2-8b");

        server.Should().NotBeNull();
        server!.IsAccepted.Should().BeTrue();
        server.Evidence!.Detector.Should().Be("ollama-server");
        server.Evidence.Title.Should().Be("Ollama server");
    }

    [Fact]
    public void Rejects_one_shot_ollama_cli_calls()
    {
        AiSessionProcessClassification? verdict = Classify(
            "ollama",
            @"C:\Program Files\Ollama\ollama.exe",
            commandLine: @"ollama.exe list");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeFalse();
        verdict.RejectionDetector.Should().Be("ollama-cli-oneshot");
    }

    // ---- Generic agent CLIs and noise ----

    [Fact]
    public void Detects_known_generic_agent_cli()
    {
        AiSessionProcessClassification? verdict = Classify(
            "aider",
            @"C:\Users\acera\.local\bin\aider.exe");

        verdict.Should().NotBeNull();
        verdict!.IsAccepted.Should().BeTrue();
        verdict.Evidence!.Provider.Should().Be(AiSessionProvider.Generic);
        verdict.Evidence.Detector.Should().Be("generic-agent-cli");
        verdict.Evidence.Confidence.Should().Be(AiSessionConfidence.Moderate);
        verdict.Evidence.Reason.Should().Contain("Aider");
    }

    [Fact]
    public void Ignores_unrelated_processes_entirely()
    {
        Classify("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe").Should().BeNull();
        Classify("Code", @"C:\Users\acera\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            commandLine: "Code.exe --type=renderer").Should().BeNull();
        Classify("node", @"C:\Program Files\nodejs\node.exe",
            commandLine: @"node.exe C:\repo\node_modules\typescript\lib\tsserver.js").Should().BeNull();
    }

    [Fact]
    public void Ignores_processes_without_executable_metadata()
    {
        AiSessionProcessClassification? verdict = new AiSessionProcessClassifierRegistry().Classify(
            AiSessionProcessContext.Create(
                new AiSessionProcessSnapshot(4, "System", null, null, null, null, null),
                ObservedAt));

        verdict.Should().BeNull();
    }

    [Fact]
    public void Keeps_indirect_same_provider_chains_as_two_sessions()
    {
        // Session A shells out (via cmd) to a headless `claude -p` child: two
        // real sessions, so nothing may be collapsed across the shell hop.
        var sessionA = new AiSessionProcessSnapshot(
            100,
            "node",
            @"C:\Program Files\nodejs\node.exe",
            null,
            StartedAt,
            50,
            @"node.exe C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js --cwd C:\RepoA");
        var shell = new AiSessionProcessSnapshot(
            150,
            "cmd",
            @"C:\Windows\System32\cmd.exe",
            null,
            StartedAt.AddMinutes(1),
            100,
            null);
        var headlessChild = new AiSessionProcessSnapshot(
            200,
            "node",
            @"C:\Program Files\nodejs\node.exe",
            null,
            StartedAt.AddMinutes(1),
            150,
            @"node.exe C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js -p ""do a thing""");

        IReadOnlyList<AiSessionEvidence> evidence = new ProcessSnapshotEvidenceCollector()
            .Collect([sessionA, shell, headlessChild], ObservedAt);

        evidence.Should().HaveCount(2);
        evidence.Select(e => e.Pid).Should().BeEquivalentTo([100, 200]);
    }

    [Fact]
    public void Records_ancestor_pid_chain_for_watched_wrapper_detection()
    {
        var wrapper = new AiSessionProcessSnapshot(
            50, "cmd", @"C:\Windows\System32\cmd.exe", null, StartedAt, 10, null);
        var worker = new AiSessionProcessSnapshot(
            200,
            "node",
            @"C:\Program Files\nodejs\node.exe",
            null,
            StartedAt.AddSeconds(1),
            50,
            @"node.exe C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js");

        IReadOnlyList<AiSessionEvidence> evidence = new ProcessSnapshotEvidenceCollector()
            .Collect([wrapper, worker], ObservedAt);

        evidence.Should().ContainSingle();
        evidence[0].Metadata.Should().NotBeNull();
        evidence[0].Metadata!["ancestorPids"].Should().Be("50,10");
    }

    [Fact]
    public void ReadCommandOption_requires_token_boundaries()
    {
        AiSessionTextSanitizer.ReadCommandOption(
            @"codex --working-directory C:\x --working-dir C:\y", "--working-dir")
            .Should().Be(@"C:\y");
        AiSessionTextSanitizer.ReadCommandOption(
            @"codex --working-directory C:\x", "--working-dir")
            .Should().BeNull();
        AiSessionTextSanitizer.ReadCommandOption(
            @"claude --cwd ""C:\My Repo""", "--cwd")
            .Should().Be(@"C:\My Repo");
    }

    [Fact]
    public void Collapses_wrapper_and_worker_into_one_evidence_record()
    {
        var wrapper = new AiSessionProcessSnapshot(
            100,
            "claude",
            @"C:\Users\acera\AppData\Roaming\Claude\claude-code\2.1.197\claude.exe",
            null,
            StartedAt,
            50,
            null);
        var worker = new AiSessionProcessSnapshot(
            200,
            "node",
            @"C:\Program Files\nodejs\node.exe",
            null,
            StartedAt.AddSeconds(1),
            100,
            @"node.exe C:\Users\acera\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js --cwd C:\Repo");

        IReadOnlyList<AiSessionEvidence> evidence = new ProcessSnapshotEvidenceCollector()
            .Collect([wrapper, worker], ObservedAt);

        evidence.Should().HaveCount(1);
        evidence[0].Pid.Should().Be(200);
        evidence[0].WorkspacePath.Should().Be(@"C:\Repo");
    }

    private static AiSessionProcessClassification? Classify(
        string processName,
        string executablePath,
        string? commandLine = null,
        string? mainWindowTitle = null)
        => new AiSessionProcessClassifierRegistry().Classify(
            AiSessionProcessContext.Create(
                new AiSessionProcessSnapshot(
                    1234,
                    processName,
                    executablePath,
                    mainWindowTitle,
                    StartedAt,
                    null,
                    commandLine),
                ObservedAt));
}

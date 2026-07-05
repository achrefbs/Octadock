using Octadock.Core.Models;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>
/// Codex family: the desktop app spawns rejected Electron helpers and an
/// app-server, plus node "runtime" workers that carry a session id. The
/// standalone Codex CLI (npm/cargo install) is a session by itself.
/// </summary>
public sealed class CodexProcessClassifier : IAiSessionProcessClassifier
{
    public string Id => "codex";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool desktopInstall =
            context.ExecutablePathContains(@"\WindowsApps\OpenAI.Codex") ||
            context.ExecutablePathContains(@"\OpenAI\Codex\");

        if (desktopInstall)
        {
            if (context.FileNameIs("node.exe") && context.ExecutablePathContains(@"\OpenAI\Codex\runtimes\"))
            {
                if (!context.CommandLineContains("--session-id"))
                {
                    return AiSessionProcessClassification.Reject(
                        "codex-runtime-idle",
                        "Codex runtime worker without a session id looks like an idle pool worker.");
                }

                string? sessionId = AiSessionTextSanitizer.ReadCommandOption(context.CommandLine, "--session-id");
                string? workspace = AiSessionTextSanitizer.ReadCommandOption(context.CommandLine, "--working-dir");
                string folder = AiSessionTextSanitizer.WorkspaceLabel(workspace);
                return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                    context,
                    AiSessionProvider.Codex,
                    "codex-runtime-session",
                    AiSessionConfidence.NeedsCorroboration,
                    "Codex desktop runtime worker with an assigned session id; confirmed by thread state when available.",
                    string.IsNullOrEmpty(folder) ? "Codex session" : $"Codex - {folder}",
                    workspace,
                    sessionId,
                    requiresCorroboration: true,
                    corroborationSource: CodexStateEvidenceCollector.SourceId));
            }

            return AiSessionProcessClassification.Reject(
                "codex-desktop-shell",
                "Codex desktop app/helper process; sessions are tracked through thread state instead.");
        }

        if (context.FileNameIs("codex.exe") || string.Equals(context.ProcessName, "codex", StringComparison.OrdinalIgnoreCase))
        {
            string? subcommand = AiSessionTextSanitizer.FirstArgument(context.CommandLine);
            if (subcommand is "app-server" or "mcp" or "mcp-server" or "login" or "logout" or "completion")
            {
                return AiSessionProcessClassification.Reject(
                    "codex-cli-helper",
                    "Codex CLI running in a helper mode (app-server/mcp/login), not an interactive session.");
            }

            string? workspace = AiSessionTextSanitizer.ReadCommandOption(context.CommandLine, "--cd");
            string folder = AiSessionTextSanitizer.WorkspaceLabel(workspace);
            bool commandLineKnown = context.CommandLine is not null;
            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.Codex,
                "codex-cli",
                commandLineKnown ? AiSessionConfidence.Strong : AiSessionConfidence.NeedsCorroboration,
                commandLineKnown
                    ? "Standalone Codex CLI process."
                    : "Standalone Codex CLI process; command line unavailable, helper modes cannot be ruled out.",
                string.IsNullOrEmpty(folder) ? "Codex CLI" : $"Codex CLI - {folder}",
                workspace));
        }

        return null;
    }
}

/// <summary>
/// Claude Code family: the CLI binary and its node workers are sessions;
/// the Claude desktop Electron app and the Chrome native-messaging host are not.
/// </summary>
public sealed class ClaudeCodeProcessClassifier : IAiSessionProcessClassifier
{
    public string Id => "claude-code";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool claudeCodeModule =
            context.CommandLineContains(@"\@anthropic-ai\claude-code\") ||
            context.CommandLineContains("/@anthropic-ai/claude-code/") ||
            context.ExecutablePathContains(@"\@anthropic-ai\claude-code\");
        bool managedInstall = context.ExecutablePathContains(@"\Claude\claude-code\");
        bool claudeExe = context.FileNameIs("claude.exe");

        if (!claudeCodeModule && !managedInstall && !claudeExe)
        {
            return null;
        }

        if (AiSessionProcessNoise.IsNativeMessagingHost(context))
        {
            return AiSessionProcessClassification.Reject(
                "claude-native-host",
                "Claude Code browser native-messaging host, not an interactive session.");
        }

        if (context.ExecutablePathContains(@"\AnthropicClaude\"))
        {
            return AiSessionProcessClassification.Reject(
                "claude-desktop-shell",
                "Claude desktop app process, not a Claude Code session.");
        }

        if (context.CommandLineContains(" mcp serve"))
        {
            return AiSessionProcessClassification.Reject(
                "claude-code-mcp-server",
                "Claude Code running as an MCP server backend, not an interactive session.");
        }

        string? workspace =
            AiSessionTextSanitizer.ReadCommandOption(context.CommandLine, "--cwd") ??
            AiSessionTextSanitizer.ReadCommandOption(context.CommandLine, "--working-dir");
        string folder = AiSessionTextSanitizer.WorkspaceLabel(workspace);
        string title = string.IsNullOrEmpty(folder) ? "Claude Code" : $"Claude Code - {folder}";

        if (claudeExe && (managedInstall || claudeCodeModule))
        {
            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.ClaudeCode,
                "claude-code-cli",
                AiSessionConfidence.Certain,
                "Claude Code CLI binary at a known install location.",
                title,
                workspace));
        }

        if (claudeCodeModule)
        {
            // Node/bun workers running the claude-code package, including workers
            // spawned by editor extensions (run-as-node), are real sessions.
            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.ClaudeCode,
                "claude-code-node",
                AiSessionConfidence.Strong,
                "Worker process running the @anthropic-ai/claude-code package.",
                title,
                workspace));
        }

        return AiSessionProcessClassification.Reject(
            "claude-unrecognized-install",
            "claude.exe outside known Claude Code install locations; not shown to avoid false positives.");
    }
}

/// <summary>
/// Cursor family: the cursor-agent CLI is a session; the Cursor editor itself
/// and its Electron helpers/language servers are not (agent activity inside the
/// editor is not observable from process metadata alone).
/// </summary>
public sealed class CursorProcessClassifier : IAiSessionProcessClassifier
{
    public string Id => "cursor";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool agentCli =
            context.FileNameIs("cursor-agent.exe") ||
            string.Equals(context.ProcessName, "cursor-agent", StringComparison.OrdinalIgnoreCase) ||
            context.CommandLineContains(@"\cursor-agent\") ||
            context.CommandLineContains("/cursor-agent/");
        if (agentCli)
        {
            if (AiSessionProcessNoise.IsLanguageServerLike(context))
            {
                return AiSessionProcessClassification.Reject(
                    "cursor-agent-helper",
                    "cursor-agent helper running as a stdio backend, not an interactive session.");
            }

            bool commandLineKnown = context.CommandLine is not null;
            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.Cursor,
                "cursor-agent-cli",
                commandLineKnown ? AiSessionConfidence.Strong : AiSessionConfidence.NeedsCorroboration,
                commandLineKnown
                    ? "Cursor agent CLI process."
                    : "Cursor agent CLI process; command line unavailable, helper modes cannot be ruled out.",
                "Cursor Agent"));
        }

        bool cursorEditor =
            context.FileNameIs("cursor.exe") &&
            (context.ExecutablePathContains(@"\cursor\") || context.ExecutablePathContains(@"\Cursor\"));
        if (cursorEditor)
        {
            return AiSessionProcessClassification.Reject(
                AiSessionProcessNoise.IsElectronHelper(context) ? "cursor-electron-helper" : "cursor-editor-shell",
                "Cursor editor process; in-editor agent activity is not observable with enough confidence.");
        }

        return null;
    }
}

/// <summary>
/// GitHub Copilot family: the Copilot CLI is a session; the Copilot language
/// server and editor-embedded agents are not.
/// </summary>
public sealed class CopilotProcessClassifier : IAiSessionProcessClassifier
{
    public string Id => "copilot";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool copilotModule =
            context.CommandLineContains(@"\@github\copilot") ||
            context.CommandLineContains("/@github/copilot") ||
            context.ExecutablePathContains(@"\@github\copilot");
        bool copilotIsh =
            copilotModule ||
            context.FileNameIs("copilot.exe") ||
            context.CommandLineContains("copilot-language-server") ||
            context.CommandLineContains("copilot-agent");
        if (!copilotIsh)
        {
            return null;
        }

        if (context.ExecutablePathContains(@"\WindowsApps\Microsoft.Copilot"))
        {
            return AiSessionProcessClassification.Reject(
                "windows-copilot-app",
                "Microsoft Windows Copilot app, not a coding agent session.");
        }

        if (context.CommandLineContains("copilot-language-server") ||
            AiSessionProcessNoise.IsLanguageServerLike(context))
        {
            return AiSessionProcessClassification.Reject(
                "copilot-language-server",
                "Copilot language server (IDE completion backend), not an agent session.");
        }

        if (context.CommandLineContains("copilot-agent") && !copilotModule)
        {
            // Editor-embedded agents (VS Code, Insiders, JetBrains, Cursor,
            // remote server layouts) all run copilot-agent workers.
            return AiSessionProcessClassification.Reject(
                "copilot-extension-worker",
                "Copilot agent embedded in an editor; not observable with enough confidence from process metadata.");
        }

        if (copilotModule)
        {
            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.GitHubCopilot,
                "copilot-cli",
                AiSessionConfidence.Strong,
                "GitHub Copilot CLI process (@github/copilot package).",
                "Copilot CLI"));
        }

        return AiSessionProcessClassification.Reject(
            "copilot-unrecognized-install",
            "copilot.exe outside known GitHub Copilot CLI installs; not shown to avoid false positives.");
    }
}

/// <summary>Gemini CLI family: the interactive CLI is a session; IDE bridges are not.</summary>
public sealed class GeminiProcessClassifier : IAiSessionProcessClassifier
{
    public string Id => "gemini";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool geminiIsh =
            context.FileNameIs("gemini.exe") ||
            context.CommandLineContains(@"\@google\gemini-cli") ||
            context.CommandLineContains("/@google/gemini-cli") ||
            context.CommandLineContains(@"\gemini-cli\dist\") ||
            context.CommandLineContains("/gemini-cli/dist/");
        if (!geminiIsh)
        {
            return null;
        }

        if (context.CommandLineContains("--experimental-acp") ||
            AiSessionProcessNoise.IsLanguageServerLike(context))
        {
            return AiSessionProcessClassification.Reject(
                "gemini-ide-bridge",
                "Gemini CLI running as an IDE bridge, not an interactive session.");
        }

        bool commandLineKnown = context.CommandLine is not null;
        return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
            context,
            AiSessionProvider.Gemini,
            "gemini-cli",
            commandLineKnown ? AiSessionConfidence.Strong : AiSessionConfidence.NeedsCorroboration,
            commandLineKnown
                ? "Gemini CLI process."
                : "Gemini CLI process; command line unavailable, bridge modes cannot be ruled out.",
            "Gemini CLI"));
    }
}

/// <summary>
/// Ollama family: long-lived model runner processes and the local server count
/// as sessions; one-shot CLI invocations (list, ps, pull, ...) do not.
/// </summary>
public sealed class OllamaProcessClassifier : IAiSessionProcessClassifier
{
    public string Id => "ollama";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        bool ollamaIsh = context.FileNameIs("ollama.exe") ||
            string.Equals(context.ProcessName, "ollama", StringComparison.OrdinalIgnoreCase);
        if (!ollamaIsh)
        {
            return null;
        }

        string? subcommand = AiSessionTextSanitizer.FirstArgument(context.CommandLine);
        if (subcommand == "runner")
        {
            string? modelPath = AiSessionTextSanitizer.ReadCommandOption(context.CommandLine, "--model");
            string model = AiSessionTextSanitizer.WorkspaceLabel(modelPath);
            int extension = model.LastIndexOf('.');
            if (extension > 0)
            {
                model = model[..extension];
            }

            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.Ollama,
                "ollama-runner",
                AiSessionConfidence.Strong,
                "Ollama model runner process serving a loaded model.",
                string.IsNullOrEmpty(model) ? "Ollama runner" : $"Ollama - {model}"));
        }

        if (subcommand == "serve")
        {
            return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
                context,
                AiSessionProvider.Ollama,
                "ollama-server",
                AiSessionConfidence.Strong,
                "Ollama local server process.",
                "Ollama server"));
        }

        return AiSessionProcessClassification.Reject(
            "ollama-cli-oneshot",
            "One-shot Ollama CLI invocation (or unknown mode), not a persistent session.");
    }
}

/// <summary>
/// Curated fallback for well-known standalone agent CLIs. Exact-name matches
/// only: this intentionally does not guess from window titles or generic
/// long-running commands, preferring "not shown" over noise.
/// </summary>
public sealed class GenericAgentCliClassifier : IAiSessionProcessClassifier
{
    private static readonly Dictionary<string, (string DisplayName, AiSessionProvider Provider)> KnownAgents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Names must be distinctive enough that a bare process-name match
            // is safe: "goose" is deliberately absent because the pressly/goose
            // database migration CLI shares the name.
            ["aider"] = ("Aider", AiSessionProvider.Generic),
            ["opencode"] = ("OpenCode", AiSessionProvider.Generic),
            ["openhands"] = ("OpenHands", AiSessionProvider.Generic),
            ["jules"] = ("Jules", AiSessionProvider.Jules),
            ["devin"] = ("Devin", AiSessionProvider.Devin),
        };

    public string Id => "generic-agent-cli";

    public AiSessionProcessClassification? Classify(AiSessionProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        (string DisplayName, AiSessionProvider Provider) match;
        if (KnownAgents.TryGetValue(context.ProcessName, out match))
        {
            // Recognized by name only.
        }
        else if (context.ProcessName.StartsWith("python", StringComparison.OrdinalIgnoreCase) &&
            (context.CommandLineContains(@"\aider") || context.CommandLineContains("-m aider")))
        {
            match = KnownAgents["aider"];
        }
        else
        {
            return null;
        }

        if (AiSessionProcessNoise.IsElectronHelper(context) ||
            AiSessionProcessNoise.IsLanguageServerLike(context) ||
            AiSessionProcessNoise.IsNativeMessagingHost(context))
        {
            return AiSessionProcessClassification.Reject(
                "agent-cli-helper",
                $"{match.DisplayName} helper/bridge process, not an interactive session.");
        }

        return AiSessionProcessClassification.Accept(AiSessionProcessEvidence.Create(
            context,
            match.Provider,
            "generic-agent-cli",
            AiSessionConfidence.Moderate,
            $"Known agent CLI '{match.DisplayName}' is running.",
            match.DisplayName));
    }
}

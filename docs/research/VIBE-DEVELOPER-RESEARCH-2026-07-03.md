# Vibe-Developer Experience Research

Date: 2026-07-03  
Purpose: identify useful feature directions for Octadock as a local desktop
toolkit for developers working with AI agents.

## Research Takeaway

Modern coding-agent tools are adding background sessions, hooks, dashboards,
status lines, and analytics. What they mostly do not provide is a small local
Windows desktop surface that watches all of the user's active coding/AI work
across tools, then ties those sessions to screenshots, OCR, clipboard snippets,
voice dictation, files, and notifications.

Octadock should not try to replace Claude Code, Codex, Cursor, Copilot, Jules,
or Vercel workflows. Its opportunity is to become the local mission control
layer around them.

## External Patterns

| Pattern | What exists elsewhere | Octadock opportunity |
| --- | --- | --- |
| Lifecycle hooks | Claude Code hooks run shell commands, HTTP endpoints, or prompts at lifecycle points and receive JSON context. | Accept hook events locally so Claude Code and similar tools can update Octadock sessions. |
| Status lines | Claude Code status lines can show model, cwd, git branch, context/cost, and duration via a custom command. | Mirror agent status in the dock, plus add notifications and history. |
| Completion notifications | Claude Code can call notification hooks or use terminal bell behavior when attention is needed. | Provide richer toast/TTS and visible dock state across tools. |
| Browser/session dashboards | Vercel's agent-browser supports named sessions, `session list`, a local observability dashboard, live viewport, activity feed, console output, and optional AI chat. | Use the same idea for coding-agent runs: named local cards with logs and status. |
| Durable background agents | Vercel workflows and sandboxes can run durable AI code agents, inspect runs locally, and show activity in dashboards. | Add adapters for cloud/background runs while keeping local process watching as the base path. |
| AI analytics | WakaTime tracks AI spend/cost, adoption, effectiveness, and coding activity. | Later, summarize time/cost/outcomes for local sessions without uploading private artifacts by default. |
| Cloud coding agents | Cursor Cloud Agents, GitHub Copilot coding-agent sessions, and Jules emphasize async/background tasks tied to repositories/branches/PRs. | Show those remote sessions beside local terminal/Codex/Claude sessions when APIs or hooks allow. |

## Features Worth Building

### 1. Active AI Sessions / Agent Mission Control

The strongest differentiator. Start generic and local:

- `octadock run -- <command>` wraps a build/test/agent command and records
  start, status, exit code, cwd, branch, duration, and logs.
- `octadock watch --pid <pid>` attaches to an already-running process.
- `octadock agent-event` accepts lifecycle events from hooks/scripts.
- Dock cards show running/waiting/failed/completed/PR-ready states.
- Toast and later TTS alert the user when a run needs attention.

Then add provider-specific adapters:

- Claude Code hooks.
- Codex CLI/session logs where available.
- Cursor Cloud Agents.
- GitHub Copilot coding-agent sessions.
- Google Jules.
- Vercel workflows/agent runs.
- Generic terminal/shell command watcher.

### 2. Context Shelf

Bundle the things a developer wants to hand to an AI:

- screenshot/video;
- OCR text;
- clipboard snippets;
- file previews;
- terminal/build/agent logs;
- prompt snippets;
- branch/repo metadata.

Output targets: clipboard, Markdown file, local MCP tool, or opt-in AI provider.

### 3. Redaction Before Cloud

Before any cloud AI action:

- detect secrets in OCR/text/logs where possible;
- let users manually mark regions on images;
- use solid fill or pixelate, not blur, for destructive redaction;
- show a send preview with destination and provider.

### 4. Speech That Works For Developers

The local Whisper path preserves privacy, but the user experience needs faster
and more accurate options:

- local model picker with clear speed/accuracy estimates;
- optional high-accuracy cloud provider with explicit opt-in;
- Windows speech fallback for zero-download mode;
- hold-to-talk and toggle modes;
- streaming partials or progress;
- code dictionary and custom replacement rules.

### 5. Small Developer Tools

Build the tools that share Octadock's shelf/clipboard/command spine:

- clipboard history;
- command palette;
- OCR grab-text hotkey;
- JSON/Base64/JWT/URL/text transforms;
- code screenshot beautifier;
- snippet/prompt library;
- color picker;
- scratchpad notes.

## Product Principle

The ideal vibe-developer loop is:

`capture or dictate -> inspect/transform -> attach context -> send/watch AI work -> get notified -> recover history`

Octadock already has capture, shelf, OCR, pins, history, automation, preview, and
the first STT slice. Active AI Sessions should be the next architectural bridge
because it gives the toolkit a reason to exist while the AI is working.

## Sources

- [Claude Code hooks reference](https://code.claude.com/docs/en/hooks)
- [Claude Code hooks guide](https://code.claude.com/docs/en/hooks-guide)
- [Claude Code status line](https://code.claude.com/docs/en/statusline)
- [Claude Code terminal notifications](https://code.claude.com/docs/en/terminal-config)
- [Vercel agent-browser repository](https://github.com/vercel-labs/agent-browser)
- [Vercel durable AI code agent guide](https://vercel.com/kb/guide/how-to-build-a-durable-ai-code-agent-on-vercel)
- [Vercel coding agent platform template](https://vercel.com/templates/next.js/coding-agent-platform)
- [WakaTime AI coding analytics](https://wakatime.com/)
- [Cursor Cloud Agents documentation](https://cursor.com/docs/cloud-agent)
- [GitHub Copilot coding-agent sessions](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/cloud-agent/start-copilot-sessions)
- [Google Jules](https://jules.google/)

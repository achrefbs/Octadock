# Octadock Roadmap And Implementation Plan

Last updated: 2026-07-03  
Status: living plan

This roadmap turns the current codebase, bug audits, user feedback, and
vibe-developer research into one implementation plan. The priority is to make
the already-visible product reliable first, then build the developer/AI layer on
top of that spine.

## Phase 0: Finish The Current Branch

Goal: make the fixes the user already asked for visibly work.

Acceptance:

- Tray icon right-click opens the menu reliably. Confirmed by the user on
  2026-07-03 after replacing the WPF tray-library path with a native Windows
  Forms tray icon/menu.
- Right-clicking the permanent Dock itself does not open the old context menu.
  Confirmed by the user on 2026-07-03.
- Recording and dictation pills follow the active monitor while recording or
  listening. Confirmed by the user on 2026-07-03.
- Permanent Dock tracks active monitor changes faster and without weird lag.
  Confirmed by the user on 2026-07-03.
- Screen recordings appear as video cards in the bottom-left shelf.
- Clicking "Video saved" opens the recording's folder in File Explorer.
- Dictation uses the current `small` local Whisper test model and no longer
  uses the old tiny/base alpha default. An opt-in OpenAI transcription provider
  now exists for higher-accuracy testing when an API key is configured, but STT
  still needs live quality verification.

Notes:

- This phase needs real on-device verification, not only unit tests, because the
  remaining reported issue is tray behavior and STT quality cannot be proven by
  unit tests alone.

## Phase 1: Trust And Core Polish

Goal: remove trust-breaking bugs before piling on more tools.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Mixed-DPI overlays | Area and window-picker overlays now create per monitor; finish device verification across mixed-DPI layouts. | Overlay diagnostics match monitor bounds on 100%, 150%, 175%, and mixed-DPI setups. |
| Capture/session serialization | Keep capture, scrolling, recording, and overlay sessions from stomping each other. | Starting a second capture while one is active either queues or clearly rejects it. |
| Editor safety | Done for dirty-close prompts and save cancellation/failure preserving dirty state. Continue checking future destructive open/replace flows. | No work is lost without a warning. |
| Pin persistence | Done for restored-pin clamping and tray gather; continue close-race regression checks. | Off-screen pins restore visibly, and closing a pin stays closed. |
| Settings side effects | Ensure tray/taskbar/protocol/startup toggles have real side effects and cannot remove every visible affordance. | User always has a visible way to reopen or quit Octadock. |
| CLI/help drift | Done for `open`, AI-session verbs, selected-area recording, OCR language, and current Settings tabs; keep docs/parser/help aligned as new commands land. | `octadock open --help`, `octadock open-settings --help`, and top-level help describe the real command set. |
| OCR command parity | Done: `capture-text --area` now uses supplied fixed-region coordinates. | Automation behavior matches docs and scripts can OCR fixed regions. |
| Inert settings audit | Done for CLI/protocol enablement, local crash reports, and shelf size. Continue auditing newly exposed settings as features land. | Every visible setting does something observable. |

## Phase 2: Recording MVP

Goal: make screen recording match the user-facing promise.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Selected-area recording | Command, fixed-region, tray "Record Area", and HUD Record selected-area paths are wired. Next: live-device verification. | User can record a selected area, not only the active monitor. |
| Audio truthfulness | Done: microphone/system-audio settings are disabled and normalized off until the encoder can actually write audio tracks. | The UI no longer promises recording audio in this video-only build. |
| Cursor and pause | Preserve cursor setting; make pause/resume real or label it as unavailable. | The timer, output, and UI state agree. |
| Video shelf workflow | Keep video cards, open-location, managed default storage, drag/save/copy-file, and history behavior. | Recording can be found and shared without hunting through folders. |
| Failure recovery | First slice done: start/stop failures notify and delete incomplete MP4 outputs. Next: explicit cancellation, disk-full classification, and app-shutdown recovery. | Failed recordings do not leave confusing broken shelf cards. |

## Phase 3: Speech And Voice

Goal: make dictation fast and accurate enough for code and AI prompt boxes.

| Feature | Work | Acceptance |
| --- | --- | --- |
| STT settings | First slices done: provider, local/cloud model, language, insertion mode, editable replacement dictionary, provider availability, configurable toggle shortcut, and CLI toggle entry are exposed. Next: live partials. | User can switch models/providers and dictionary entries without code edits. |
| Hotkey modes | First toggle dictation hotkey is wired through Shortcuts (`Ctrl+Shift+2` by default). Next: hold-to-talk mode plus tray/dock listening state polish. | Quick snippets and longer dictation both feel natural. |
| Provider strategy | First optional cloud slice done: OpenAI transcription can be selected when `OPENAI_API_KEY` or `OCTADOCK_OPENAI_API_KEY` is set. Next: Windows speech fallback and provider quality profiling. | User can choose speed/privacy/accuracy tradeoffs. |
| Streaming feedback | Add live partials or at least progress/latency feedback. | The UI does not feel frozen during transcription. |
| Code dictionary | Expand replacements for common code terms and custom user entries. | Dictation into an editor produces developer-friendly text. |

## Phase 4: File Preview And Command HUD

Goal: turn Octadock into a fast inspection surface for developer files.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Windows glass preview polish | Done: draggable HUD-style card chrome, custom preview scrollbars/context menu, safe external-open confirmation, duplicate provider-error notification fix, provider-aware badges, and shelf file-drop entry. Next: source animation and more tests. | `octadock open --filepath <file>` and drag/drop feel intentional and safe. |
| More providers | Add markdown, JSON, log, PDF/image metadata, and code/text syntax-aware previews. | Common developer files open in useful cards. |
| Command palette | Turn the all-in-one HUD into a fuzzy launcher for Octadock actions, recent captures, recent previews, and snippets. | A user can drive most tools from one chord. |
| Preview actions | Done: copy path/content, save copy, image add-to-shelf, and image pin. Next: Ask AI. | The preview is useful without opening another app. |

## Phase 5: Active AI Sessions / Agent Mission Control

Goal: build the feature the user remembered: a local desktop cockpit for active
AI coding work.

This should move from old P3 "Agent-run monitor" language to a P1/P2 product
track because it is a clear differentiator for vibe developers.

| Layer | Work | Acceptance |
| --- | --- | --- |
| Core model | First slice done: `AiSession` domain types cover provider, cwd, command, pid, branch, status, started/ended, exit code, linked artifacts, and notification preferences. Generic `run` now captures bounded stdout/stderr timeline events plus full stdout/stderr log artifacts. Next: richer output modeling and provider log enrichment. | Sessions can be stored, listed, updated, and linked to captures/OCR/snippets. |
| Data schema | Done for sessions/events/artifacts. | Restarting Octadock preserves recent sessions and their final state. |
| Watch commands | First slice done: `octadock run -- <cmd>` and `octadock watch --pid <pid>` start/attach, persist status, update on process exit, and toast completion/failure unless `--notify silent` is used. Protocol URLs are blocked for these local process-watching verbs. | Generic build/test/agent commands can be monitored before tool-specific adapters exist. |
| Visible UI | First window and overlay slices done: tray, Dock AI button, `octadock open-ai-sessions`, and the passive bottom-right overlay show recent/live sessions with details, log-open actions, copy actions, folder reveal, timeline events, and Settings controls for overlay/recent-completion visibility. Next: compact dock/shelf cards for running, waiting, failed, completed, and PR-ready states. | User can inspect tracked sessions today, then glance at persistent cards for attention states. |
| Notifications | Third slice done: toast on generic run/watch finish or failure, generic output-prompt waiting detection for wrapped runs, and notification click-through to the AI Sessions window. Next: sound/TTS behavior, richer notification actions, and provider-specific waiting signals. | User is alerted when an AI agent or build needs them. |
| Hook ingestion | First CLI slice done: `octadock ai-session-event` lets local tool hooks add events or status changes to existing sessions. Next: provider-specific hook adapters and optional file/HTTP receiver. | External agents can push lifecycle events into Octadock without fragile window scraping. |
| Provider adapters | First discovery slice done for already-running Codex runtime sessions, active Codex Desktop state/rollout threads/subagents, and Claude Code worker processes. Next: Claude Code hook adapter/state enrichment, Codex CLI/log enrichment, Cursor agents, GitHub Copilot coding agent sessions, Jules, Vercel workflows, and generic terminal processes where feasible. | Sessions from the most-used tools appear in one local UI without noisy helper-process rows, completed Codex rollouts, or stale Codex subagents. |

## Phase 6: Vibe-Developer Toolkit

Goal: add the utility belt around capture, voice, preview, and AI sessions.

Highest-value order:

1. Clipboard history monitor/UI on top of the prepared Core/Data foundation:
   text/image clips, search, restore, shelf cards, and password-manager/private
   clipboard protections.
2. OCR hotkey promotion: region to text on clipboard as a first-class flow.
3. Snippet and prompt library with placeholders and paste-at-cursor.
4. Text transform toolbox: JSON format/validate, Base64/JWT/URL decode, case
   conversion, hashes, timestamps, diff two clips.
5. Code screenshot beautifier using the annotation/export pipeline.
6. Color picker / palette using the existing selection loupe pixel sampling.
7. Scratchpad notes as shelf items.

## Phase 7: AI Context Layer

Goal: make Octadock useful to AI tools without becoming a privacy mess.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Context Shelf | Bundle captures, OCR, clips, files, prompts, and session logs into one pasteable/context object. | User can build a clean context packet for an AI task. |
| Redaction | Add scriptable secret detection and solid-fill/pixelate redaction before cloud sends. | Secrets are never sent accidentally by default. |
| Ask AI | Local-first Ollama provider, then optional hosted providers. | A user can ask about a capture/file/CSV/clip with clear opt-in. |
| Send to AI | Route context to chosen tools via clipboard, CLI, MCP, or provider-specific integration. | Capture-to-prompt is one or two actions. |
| MCP server | Expose latest capture, OCR, clipboard, prompt library, context shelf, and active sessions as local MCP tools. | Cursor/Claude Code can request Octadock context locally. |

## Phase 8: Advanced Recorder And Distribution

Goal: polish toward a public beta/1.0.

- System audio via WASAPI loopback.
- GIF export.
- Camera overlay.
- Click and keystroke visualization.
- Trim/compress editor.
- Background/social-image tool.
- Upload/share plugins.
- Installer, code signing, auto-update, telemetry/upload decisions, and manual
  test matrix completion. Local opt-in crash-report files are already wired.

## Planning Rule

Every new feature should enter through the same spine:

`hotkey/tray/dock/protocol/CLI -> command parser -> service -> dock/shelf/card -> history/settings/tests`

That keeps Octadock from becoming a pile of mini apps and makes future MCP/AI
exposure much easier.

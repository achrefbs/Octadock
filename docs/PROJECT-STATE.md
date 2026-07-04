# Octadock Project State

Last audited: 2026-07-03  
Branch at audit time: `codex/ai-session-rollout-idle-timeout` after the
AI-session rollout-idle follow-up branch.

This file is the current source of truth for what Octadock actually does today.
Older specs and proposals are useful product history, but some of them describe
features as future work that now exist in code, or describe ambitious features
that are still only planned.

For a compact feature-by-feature matrix, see `docs/CAPABILITIES.md`.

## Current Build State

Octadock is an alpha Windows desktop utility. The capture core is real and the
vibe-developer toolkit has started, but several newer features are partial and
still need on-device verification.

Current integrated alpha version: `0.2.0-alpha.0`. Version metadata lives in
`version.json`, `build/version.props`, and `docs/VERSIONING.md`.

The last local verification before this docs pass was:

- `dotnet build Octadock.sln -c Debug`
- `dotnet test Octadock.sln -c Debug --no-build`
- Result from the latest integration pass: 447 tests passed.

The test source currently contains 5 test projects; theories expand into more
executed cases.

## Built And Wired

- Windows tray utility, single-instance guard, first-run flow, settings window,
  startup/protocol controls, and a permanent glass Octadock dock capsule.
- Global hotkeys for area, window, fullscreen, previous-area, all-in-one HUD,
  OCR, and recording.
- Area capture with selection overlays, live dimensions, magnifier, previous
  region recall, self-timer countdown pill, DPI-aware geometry, and Octadock
  window exclusion where the OS supports it.
- Window and fullscreen capture through the Windows platform layer, with the
  window picker using per-monitor overlays for mixed-DPI placement.
- Capture Shelf with image cards, video cards, copy/save/annotate/pin/discard,
  drag/drop, restore-recently-closed, configurable anchor/size/auto-close, and
  history integration.
- Local SQLite history for captures, actions, pins, settings, thumbnails, soft
  delete/restore, and retention cleanup.
- Clipboard history persistence foundation: Core models/filtering, SQLite schema,
  repository, DI registration, and tests for text clips and image metadata paths.
- Annotation editor with crop, select/move, arrow, rectangle, ellipse, line,
  text, highlighter, blur, pixelate, counter, freehand, undo/redo, export, copy,
  save, drag handle, and `.octadock` project packages.
- Floating pins with persistence, move/resize, opacity, click-through,
  copy/save/annotate, keyboard nudging, gather/hide/close behavior.
- Local OCR through `Windows.Media.Ocr` with region/file entry points and
  compact/lines/layout output modes. Windows AI OCR and Tesseract are provider
  placeholders, not shipped implementations.
- Automation through `octadock://` and `octadock.exe`, using one Core parser and
  per-user named-pipe forwarding to the running tray instance. Settings now gate
  CLI and protocol dispatch before a command is parsed.
- Explorer "Open with" file associations for previewable file types are
  registered at startup under HKCU and routed through `octadock open --filepath`.
- Manual vertical scrolling capture with a visible scrolling-session pill and
  stitched output. Horizontal and robust auto-scroll remain planned.
- Screen recording to MP4 for the active monitor or command-selected/fixed
  region, with countdown, recording pill, stop flow, history entry, shelf video
  card, and "Video saved" notification that opens the file location.
- When no explicit capture save directory is configured, recordings are written
  into Octadock-managed `Recordings\YYYY\MM\DD\...` storage and stored in history
  with root-relative paths.
- File preview / Windows glass viewer first slice: `octadock open`, CSV/text/image
  providers, a reusable preview card, CSV filtering/sorting/stats, unsupported
  file-info fallback, file associations, shelf file-drop entry, UNC rejection,
  open confirmation, and copyable preview content. The card has draggable
  Windows glass chrome, custom preview scrollbars/context menu,
  provider-aware badges, and
  provider-level preview errors no longer create a duplicate command-failure
  notification after the card opens.
- Speech-to-text first slice: dock dictate action, WASAPI microphone capture,
  local Whisper provider, opt-in OpenAI transcription provider, `small` test
  model, auto-language detection, built-in and editable code-term dictionary,
  speech settings for provider/model/language/insertion mode and provider
  availability, configurable toggle hotkey, local-only CLI toggle command,
  dictation pill, paste-at-cursor with clipboard restore, and
  microphone-privacy-settings help when Windows blocks capture.
- Opt-in local crash reports: when enabled in Settings, unhandled dispatcher,
  domain, unobserved task, and startup-failure exceptions save redacted JSON
  reports under `%LOCALAPPDATA%\Octadock\CrashReports`; there is no uploader or
  telemetry transport.
- Active AI Sessions first slice: Core models, SQLite schema/repository/tests,
  generic `octadock run -- <command>` and `octadock watch --pid <pid>` commands,
  caller working-directory forwarding, process-exit persistence,
  bounded stdout/stderr timeline capture and full stdout/stderr log artifacts
  under `AiSessionLogs` for `run`, completion/failure notifications, protocol
  safety blocking for those local process-watching
  verbs, and a visible tray/Dock AI Sessions window with recent
  sessions, details, timeline events, log-open actions, copy actions,
  working-folder reveal, and `octadock open-ai-sessions` automation, plus
  auto-discovery for Codex runtime
  workers, active Codex Desktop state threads/subagents, and Claude Code worker
  processes, with Codex rollout `task_complete` markers used to suppress
  completed/idle threads, a short active-rollout heartbeat used to suppress
  top-level threads that never write `task_complete`, and kept-alive runtime
  workers. A passive bottom-right overlay shows up to eight live/recent sessions
  and completions observed while Octadock was running, and Settings can disable
  that passive overlay or hide recent completions.

## Partial Or Needs Verification

- Tray right-click menu now works after replacing the WPF tray-library path with
  a native Windows Forms tray icon and `ContextMenuStrip`. User confirmation
  received on 2026-07-03.
- Right-click on the permanent Dock itself no longer opens the old menu. User
  confirmation received on 2026-07-03.
- Dictation and recording pills now follow the active monitor while listening or
  recording. User confirmation received on 2026-07-03.
- The permanent Octadock dock follows active-monitor changes fast enough in
  current testing. User confirmation received on 2026-07-03.
- Recording is not PRD-complete. Command-selected/fixed-region setup, tray
  "Record Area", and HUD Record selected-area affordances now exist, but real
  audio encoding, system audio, camera overlay, click/keystroke visualization,
  trim/compress, and full crash/shutdown recovery are still missing. Start/stop
  failures now notify and delete incomplete MP4 outputs instead of leaving broken
  partial recordings.
  Microphone/system-audio settings are currently disabled and normalized off so
  the app stays honest while recording remains video-only.
- Recording files still use absolute paths when the user explicitly configures
  an external capture save directory.
- STT is not good enough yet for the desired Wispr Flow-like experience. Local
  Whisper now defaults/migrates to the higher-quality `small` model, and an
  opt-in OpenAI provider can use `gpt-4o-transcribe`,
  `gpt-4o-mini-transcribe`, or `whisper-1` when `OPENAI_API_KEY` or
  `OCTADOCK_OPENAI_API_KEY` is configured. The audio path now prefers the normal
  Windows mic endpoint before the communications endpoint, normalizes quiet
  utterances, and logs audio diagnostics, and a configurable toggle hotkey
  defaults to `Ctrl+Shift+2`; `octadock dictation` toggles from the CLI while
  `octadock://dictation` is blocked so external URI activation cannot start the
  microphone. There is no Windows speech fallback, streaming partials,
  hold-to-talk hotkey, or performance profile.
- File preview is not proposal-complete. Missing pieces include a polished
  source-rect open animation and Ask AI. Exporting a copy, image add-to-shelf,
  image pinning, provider-aware badges, and shelf file-drop entry are now wired.
- OCR result/history modeling exists (`OcrSource`, `OcrExtracted`), but OCR
  extraction currently copies/notifies rather than creating dedicated OCR
  history rows.
- Settings expose first Active AI Sessions overlay controls, but do not yet
  expose Ask AI, TTS, MCP, upload targets, clipboard history, or code beautifier
  controls.

## Not Implemented Yet

- **Active AI Sessions / Agent Mission Control deeper adapters.** Generic
  run/watch commands, a tray/Dock window, a passive overlay, process discovery,
  full logs for wrapped runs, generic prompt-based waiting alerts for wrapped
  runs, CLI hook event ingestion, and Codex Desktop state/rollout discovery now
  exist, but dock cards, provider-specific waiting/log enrichment, hook
  adapters, TTS provider, MCP server, Claude desktop state enrichment, and
  remote/provider adapters are still missing.
- MCP server for exposing captures/OCR/clipboard/context to Cursor, Claude Code,
  Codex, and other tools.
- Send to AI, Ask AI, Context Shelf, prompt/snippet library, local Ollama
  integration, hosted model provider settings, AI redaction pipeline, and AI
  cost/session analytics.
- Clipboard history watcher/UI/restore flow, command palette, text-transform
  toolbox, code screenshot beautifier, color picker, scratchpad notes, and
  developer mini-tools.
- Upload/share plugins, share links, password-protected links, and team/cloud
  administration.
- Installer, code signing, auto-update, and public beta distribution polish. A
  framework-dependent release zip script now exists, but it is not a signed
  installer. Local opt-in crash-report files exist; uploading/telemetry does not.

## Active AI Sessions Clarification

The feature the user remembered is now partially real in code. The old name was
"Agent-run monitor": toast/TTS when a build or Claude Code run finishes. The
roadmap now promotes it to **Active AI Sessions / Agent Mission Control**; the
merged foundation provides persistence, generic local run/watch commands,
bounded stdout/stderr timeline capture and full stdout/stderr log artifacts for
`run`, completion/failure notifications, generic prompt-based waiting status for
wrapped runs, notification click-through to the AI Sessions window, local CLI
hook event ingestion, a passive bottom-right overlay with Settings controls, and
Windows process discovery for already-running Codex runtime sessions and Claude
Code workers, plus active Codex Desktop thread/subagent discovery from Codex's local state
database and rollout logs. It does not yet provide dock cards, provider-specific
hook adapters, provider-specific waiting detection, Claude desktop state
enrichment, provider log enrichment, or remote provider APIs.

Target behavior:

- See active coding-agent sessions in the tray/Dock AI Sessions window and the
  bottom-right overlay today, then later in dock/shelf cards: running, waiting
  for input, failed, completed, or PR ready.
- Start a watched run with `octadock run -- <command>` or attach to one with
  `octadock watch --pid <pid>`. Octadock notifies on completion/failure unless
  `--notify silent` is supplied. Protocol URLs are intentionally blocked for
  these local process-watching verbs.
- Discover already-running local AI sessions by scanning Windows process
  metadata and Codex state metadata: Codex runtime `node.exe` processes with
  `--session-id`, active Codex Desktop threads/subagents whose rollout logs have
  not reached `task_complete` and whose latest active rollout event is fresh,
  and Claude Code `claude.exe`/node workers are shown, while Codex
  desktop/app-server helpers, Electron helpers, Claude native-host processes,
  completed Codex rollouts, stale active rollout heartbeats, kept-alive idle
  Codex runtimes, and stale "open" subagents are ignored.
- Receive toast when a run finishes, fails, or a wrapped command emits a common
  input prompt, and click those notifications to open the AI Sessions window;
  later add TTS and provider-specific waiting alerts when a run needs attention.
- Link sessions to screenshots, OCR text, clipboard snippets, files, branches,
  PRs, and logs.
- Support generic process watching and local hook events first, then adapters
  for Claude Code hooks, Codex CLI, Cursor agents, Copilot coding agent
  sessions, Jules, and Vercel workflows where APIs or hooks are available.

## Main Risks

- Multi-monitor/DPI behavior must be verified on real hardware after every
  overlay, dock, shelf, pill, or pin positioning change.
- STT quality and latency are user-visible trust issues. The local-only path may
  not be enough; the product needs a fast optional high-accuracy provider with
  clear privacy boundaries.
- Recording audio remains unimplemented, but the current UI/settings no longer
  expose enabled toggles that imply microphone/system audio works.
- AI/session tooling touches shells, logs, files, clipboard, and secrets. It
  needs local-first defaults, redaction, and explicit opt-in for cloud calls.
- Protocol/file preview/AI actions are security-sensitive; externally triggered
  file opens and model sends need confirmation and path restrictions.

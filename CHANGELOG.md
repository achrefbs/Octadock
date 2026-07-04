# Changelog

All notable changes to Octadock are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- The crash-reporting setting now saves local redacted JSON reports for
  unhandled app exceptions under Octadock's `CrashReports` data folder; no
  uploader or telemetry transport is included.

### Changed

- Active AI Sessions now have a visible Octadock window from the tray menu and
  Dock AI button, with recent sessions, selection details, timeline events, copy
  actions, working-folder reveal, and `octadock open-ai-sessions` automation.
- Active AI Sessions now have a passive bottom-right overlay for live sessions
  and completions that Octadock observed while they were running.
- Active AI Sessions now auto-discover already-running Codex runtime sessions
  and Claude Code workers from Windows process metadata, and Codex Desktop
  threads/subagents from Codex's local state database, while folding away Codex
  desktop/app-server helper processes and Claude native-host bridges.
- The passive Active AI Sessions overlay now shows up to eight live/recent
  sessions instead of four.
- Generic Active AI Session `run` commands now capture bounded stdout/stderr
  timeline events plus full stdout/stderr log artifacts, with log-open buttons
  in the AI Sessions window.
- Generic Active AI Session `run` commands now detect common input prompts in
  stdout/stderr, mark the session as waiting, add a timeline event, and notify
  unless `--notify silent` is used.
- `octadock ai-session-event` now lets local tool hooks add timeline events or
  status changes to an existing Active AI Session. Protocol URLs are blocked for
  this command.
- Active AI Session waiting/completion/failure notifications now open the AI
  Sessions window when clicked.
- Settings now expose Active AI Sessions overlay controls, including disabling
  the passive overlay and hiding recent completions.
- File preview cards now show provider-aware badges, distinguishing table,
  markdown, data, log, code, config, web, image, fallback file, and error
  previews.
- Dropping a file onto the visible Capture Shelf now opens it through the file
  preview surface.
- Speech settings now expose a selectable OpenAI transcription provider and
  provider availability, while keeping local Whisper as the privacy-first
  default.
- Dictation now has a configurable global toggle hotkey, defaulting to
  `Ctrl+Shift+2`, wired through the same Shortcuts settings as capture actions.
- `octadock dictation` now toggles dictation from the CLI; the matching protocol
  URL is blocked so external URI activation cannot start the microphone.
- Recording start/stop failures now clean up incomplete MP4 outputs before
  reporting the failure, so broken partial videos are not left in managed
  recording storage.
- File preview now uses a draggable Windows glass HUD card with icon actions,
  custom preview scrollbars, and a Octadock-styled context menu.
- Tray icon and tray context-menu handling now use the native Windows Forms
  notification-area API instead of the WPF tray library path that failed to open
  the menu on-device.
- Speech settings now default and migrate old alpha `tiny`/`base.en` Whisper
  choices to the higher-quality local `small` model.
- Dictation now defaults to language auto-detection, prefers the normal Windows
  microphone endpoint before communications fallback, normalizes quiet audio, and
  logs audio/inference diagnostics.
- Recording audio toggles are disabled and normalized off until microphone/system
  audio tracks are actually encoded.
- The shelf size setting now changes capture shelf card and thumbnail dimensions
  live instead of only being persisted.

### Fixed

- Opening a file no longer returns a command failure after the preview card is
  already shown for provider-level preview errors, avoiding duplicate error
  notifications.
- Codex Desktop itself is no longer reported as a single running AI session;
  Octadock now reads Codex thread state, de-duplicates runtime workers for the
  same workspace, and ignores stale "open" subagents whose parent thread is not
  recent.
- Codex Desktop state discovery now reads rollout `task_complete` markers, so
  completed/idle threads and kept-alive runtime workers stop appearing as live
  AI sessions.
- Codex Desktop sessions now also expire from the live overlay when the latest
  active rollout event is no longer fresh, preventing top-level threads without
  a `task_complete` marker from showing as running for hours.
- Generic Active AI Session `run`/`watch --pid` monitors now show completion or
  failure notifications unless `--notify silent` is used.

### Planned

- Continue the `0.2.x` alpha line with Active AI Sessions UI/hooks/adapters,
  tray/HUD selected-region recording polish, improved dictation provider
  options, clipboard history, Ask AI, and installer/release polish.

## [0.2.0-alpha.0] - 2026-07-03

Initial alpha implementation of Octadock, a Windows screenshot, recording,
preview, and developer-tooling utility centered on a Dock plus Capture Shelf.

### Added

- **Solution scaffold** — a .NET 8 multi-project solution: `Octadock.Core`
  (platform-agnostic domain, `net8.0`), `Octadock.Data` (SQLite persistence,
  `net8.0`), `Octadock.Platform.Windows` (Win32/WinRT, `net8.0-windows`),
  `Octadock.App` (WPF tray app, `net8.0-windows`), and `Octadock.Cli`
  (`octadock.exe`, `net8.0-windows`), with Central Package Management, shared
  analyzer/style settings, and an `.editorconfig`.
- **Core contracts** — domain models, geometry, settings, filename templating,
  `.octadock` project serialization, history retention policy, the automation
  command model (`CommandType`, `CommandTokens`, `OctadockCommand`), the
  `CommandParser`/`CommandFormatter`, and the line-based IPC contract
  (`IpcProtocol`, `IpcRequest`, `IpcResponse`).
- **Capture loop** — area, previous-area, window, active-monitor fullscreen,
  all-monitor fullscreen, and self-timer captures with DPI-aware overlays,
  magnifier, capture exclusion, local persistence, thumbnails, and shelf actions.
- **Dock and shelf** — permanent glass Dock capsule, all-in-one HUD, capture
  shelf cards, restore-recently-closed, copy/save/save-as/annotate/pin/discard,
  drag-out, image transforms, file-copy actions, and Explorer reveal.
- **Annotation editor** — crop, select/move, arrow, rectangle, ellipse, line,
  text, highlighter, blur, pixelate, counter, freehand, undo/redo, copy/export,
  drag-out, and `.octadock` project save/load.
- **Pins and history** — persisted floating pins, click-through lock, opacity,
  gather/show/hide behavior, SQLite-backed history, action logging, soft delete,
  restore, retention cleanup, and thumbnail cache.
- **OCR** — local `Windows.Media.Ocr` provider with region/file OCR and compact,
  lines, and layout output modes copied to the clipboard. Windows AI OCR and
  Tesseract remain provider placeholders.
- **Scrolling capture** — manual vertical scrolling capture with a visible
  session pill and stitched output.
- **Recording first slice** — active-monitor MP4 recording with countdown,
  recording pill, stop/pause controls, history/shelf insertion, video shelf
  cards, command-selected/fixed-region recording prep, and clickable "Video
  saved" notifications that reveal the output file.
- **File preview first slice** — `open` automation verb, file-association
  registration, Dock entry point, CSV/TSV previews with sorting/filtering/stats,
  text/code/config previews, image previews, file-info fallback, and UNC
  rejection, plus open confirmation and copyable preview content.
- **Speech-to-text first slice** — dock-triggered local Whisper dictation with
  WASAPI microphone capture, `base.en` model download/cache, dictation pill,
  English/code-term hints, editable developer dictionary settings,
  paste-at-cursor insertion, clipboard restore, and microphone privacy-setting
  guidance.
- **Automation CLI** — `octadock.exe`, a thin forwarder that validates commands
  locally with `Octadock.Core.Services.CommandParser`, then forwards the raw
  arguments to the running tray instance over the per-user named pipe from
  `IpcProtocol`, launching `Octadock.exe` if it is not already running. Supports
  every automation verb plus `octadock://` protocol activation, `--json`
  machine-readable output, `--help` and per-command help, `--version`,
  `--timeout`, and `--no-launch`. The app now honors automation settings before
  dispatch, so disabled CLI/protocol entry points fail early. Exit codes: `0`
  success, `1` runtime/IPC error, `2` parse error.
- **Active AI Sessions first slice** — generic `octadock run -- <command>` and
  `octadock watch --pid <pid>` commands persist local process sessions/events,
  carry the caller working directory over IPC, and update the session when the
  watched process exits. `octadock://` URLs are blocked for these local
  process-watching verbs.
- **Clipboard history persistence foundation** — Core/Data models, filters,
  SQLite migration, repository, DI registration, and tests for text clips and
  image metadata path records.
- **Documentation** — repository `README`, `docs/ARCHITECTURE.md`,
  `docs/AUTOMATION.md`, `docs/CONTRIBUTING.md`, `docs/TESTING.md`, an MIT
  `LICENSE`, and this changelog.
- **Build and CI** — `build/build.ps1`, `build/build.sh`, and `build/test.ps1`
  helper scripts, plus a GitHub Actions workflow that builds/tests the full
  solution on Windows and builds/tests the cross-platform projects on Linux with
  coverage and a `dotnet format` lint step.
- **Versioning system** — centralized SemVer metadata in `version.json` and
  `build/version.props`, `build/version.ps1` for bump/check automation,
  assembly/file/informational version stamping, CI validation, and
  `docs/VERSIONING.md`.
- **Release packaging** — `build/release.ps1` validates version metadata,
  restores/builds/tests, publishes app and CLI, and creates a framework-dependent
  Windows zip with manifest and SHA256 checksums.

### Changed

- The previous "OCR/scrolling/recording are scaffolded" status is no longer
  accurate. They now exist as partial feature slices with clear limitations.
- The docs now include a dated current-state snapshot, live roadmap, and
  vibe-developer research notes:
  `docs/PROJECT-STATE.md`, `docs/ROADMAP.md`, and
  `docs/research/VIBE-DEVELOPER-RESEARCH-2026-07-03.md`.

### Known limitations

- Recording is video-only. Active-monitor, command-selected/fixed-region, and
  tray/HUD selected-region paths exist, but real microphone/system-audio
  encoding is planned.
- Dictation is local-Whisper-only for now. It needs hotkeys, live partials, and
  better performance and accuracy options beyond the first settings/model
  picker slice.
- Scrolling capture is manual vertical only. Horizontal and auto-scroll are not
  implemented.
- File preview is useful but not proposal-complete; source animation and Ask AI
  are still planned, while shelf-drop, export, image add-to-shelf, and image pin
  actions are wired.
- Active AI Sessions / Agent Mission Control has generic process watching,
  notifications, full log capture, visible window/overlay surfaces, and
  prompt-based waiting detection for wrapped runs, but no dock UI, hook
  adapters, provider-specific log/waiting enrichment, or remote provider APIs
  yet.
- Clipboard history has storage only; there is no clipboard monitor, restore UI,
  shelf card flow, or private/password-manager clipboard exclusion yet.

### Status

The active implementation plan lives in `docs/ROADMAP.md`; the current as-built
state lives in `docs/PROJECT-STATE.md`.

[Unreleased]: https://github.com/suruslabs/octadock/commits/main

# Changelog

All notable changes to Octadock are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Clipboard history: Octadock now watches the Windows clipboard (opt-out via
  the new Settings → Clipboard tab) and keeps a searchable, local-only history
  of text and image clips with source app/window provenance, seen-count
  de-duplication, favorites, per-clip copy/delete, clear-all, a configurable
  cap that trims the oldest non-favorites, and a `Ctrl+Shift+9` hotkey.
  Content marked private by password managers
  (`ExcludeClipboardContentFromMonitorProcessing` and the Windows
  clipboard-history/cloud opt-out formats) is never recorded, and Octadock's
  own clipboard writes are ignored so restoring a clip never re-records it.
  New `octadock open-clipboard-history` CLI/protocol verb, tray menu entry,
  and Dock "Clip" action.
- Text-transform toolbox (`octadock open-text-tools`, tray "Text Tools"):
  28 local, instant transforms — JSON pretty-print/minify, Base64/URL/HTML
  encode-decode, JWT decode, camel/Pascal/snake/kebab/CONSTANT case, MD5/SHA
  hashes, Unix-timestamp conversion, and sort/dedupe/trim/count line tools —
  with live output, copy-result, and chain-output-to-input.
- OCR extractions now create history rows: the grabbed region is stored as an
  OCR capture with the recognized text saved on the action record, the History
  window's OCR filter finds them, and a new "Copy Text" action recovers the
  extracted text later.
- File preview providers: JSON files pretty-print (with comment/trailing-comma
  tolerance and a clear note when invalid), `.log` files preview their tail
  (newest entries) instead of their head, and Markdown files render with
  headings, lists, fenced code blocks, quotes, and inline emphasis. Links in
  rendered markdown are never clickable; the URL shows as a tooltip.
- Native window chrome now follows the theme: every Octadock window gets a
  dark (or light) title bar, theme-matched caption colors, and rounded corners
  on Windows 11, applied live when the theme changes.
- App-wide control theming: scrollbars, context menus, tooltips, sliders,
  radio buttons, progress bars, and menu separators now match the Octadock
  palette instead of the Win32 defaults, and bare text boxes / check boxes /
  combo boxes pick up the themed styles implicitly.
- The file preview card moved from its off-brand cyan palette to Octadock's
  brand teal glass, multiline text boxes now honor their scrollbar settings,
  the annotation editor's default accent is the brand teal instead of the
  legacy blue, and the floating-pin toolbar uses Segoe MDL2 glyphs instead of
  mixed emoji.
- The crash-reporting setting now saves local redacted JSON reports for
  unhandled app exceptions under Octadock's `CrashReports` data folder; no
  uploader or telemetry transport is included.

### Changed — the "obsidian glass" redesign (2026-07-05)

- Octadock has a new dark-first visual identity: deep blue-black surfaces, a
  signature teal→cyan gradient on primary actions, an accent indicator bar on
  the settings navigation, larger card radii, and a soft window backdrop
  gradient. **Dark is now the default theme** — a one-time v3 settings
  migration moves users who never made an explicit choice from System to Dark;
  Light and System remain selectable and Light was refreshed to white cards on
  a cool tinted canvas.
- The passive AI-sessions overlay was redesigned from spinning dashed circles
  into a slim stack of glass status cards: status edge bar + pulsing dot (teal
  live / amber waiting / green done / rose failed), tool title, and a
  "Live for …" activity line, with hover-revealed dismiss.
- The dock capsule, recording/dictation/scrolling/countdown pills, and the
  file preview card moved onto the same deeper obsidian glass base color.

### Added — real-time AI session discovery v2 (2026-07-05)

- AI-session tracking is now event-driven instead of poll-only
  (docs/specs/ai-session-discovery-v2.md): every PID-backed session holds a
  process-exit await and is marked Completed the instant its process dies
  (with the exit code when readable); WMI process-creation events trigger a
  scan within ~1–2 s of claude/codex/node/ollama/cursor-agent/gemini/copilot
  starting; file watchers on Codex's home and Claude Code's project
  transcripts surface activity transitions in under a second; and a new
  session change bus pushes every store write to the overlay and the AI
  Sessions window immediately (the 10 s sweep remains only as a
  reconciliation safety net).
- New provider detectors: Ollama (server + per-model runner processes, with
  the model name in the title; one-shot CLI calls ignored), Cursor
  (`cursor-agent`), GitHub Copilot CLI, and Gemini CLI. Cloud-hosted agents
  (Copilot coding agent, Cursor background agents) need provider APIs and are
  documented as the next adapter step.

### Fixed — session accuracy and store durability (2026-07-05, second pass)

- "Made-up sessions": a resumed Codex thread (or any rediscovered tool) now
  REOPENS its previous session row instead of minting a new one per
  conversation turn, and brand-new processes are ignored for their first 12
  seconds so short-lived CLI helper processes can no longer appear as phantom
  3-second sessions.
- "Wrong times": overlay cards now label active sessions by last activity
  ("Active just now") instead of the session-row age, which read as wrong for
  long-lived Codex threads.
- Event-triggered scans are floored at one per 3 seconds so a streaming tool
  appending to its logs cannot drive continuous full scans.
- Store durability (root cause of the erratic behavior): the SQLite base file
  was never checkpointed while the app ran, so force-killing the process
  rolled the store back to an empty database — sessions vanished, settings
  reset, and first-run reappeared. The database now checkpoints (WAL →
  TRUNCATE) after startup, on every retention cycle, and on clean shutdown;
  startup runs `PRAGMA quick_check` and, when real corruption is found,
  quarantines the damaged file beside itself, rebuilds a fresh schema, and
  salvages settings/captures/actions/pins/clips best-effort.
- New local-only `octadock quit` command (aliases `exit`, `shutdown`;
  `octadock://` blocked) shuts the app down cleanly — scripts and dev loops
  no longer need `taskkill /F`, which is what corrupted the store.
- Per-process log files with a 2-second disk flush replace the shared log
  sink, which had gone permanently silent after a force-killed instance and
  swallowed half a day of diagnostics.

### Fixed

- Active AI Sessions overlay ("the circles"): rows now update in place instead
  of being rebuilt every 2 seconds, so the live ring no longer stutters or
  restarts, tooltips stay open, and dismiss/open clicks are never swallowed;
  the cluster also stops re-issuing window repositioning on every tick.
- AI session discovery: a torn line in a Codex rollout file (or a transiently
  locked Codex state DB) no longer marks live threads Completed and re-creates
  them as duplicate sessions; Claude Code worker processes whose parent is
  already tracked no longer appear as extra circles; `run`/`watch` sessions
  orphaned by an app restart are reconciled instead of spinning "Running"
  forever; PID reuse by an unreadable (elevated) process no longer keeps a
  dead session alive; and the overlay no longer drives a full process scan
  every 2 seconds — it reads the repository and lets the discovery loop scan.
- The AI Sessions window now auto-refreshes every 5 seconds while open, so it
  agrees with the overlay and never shows exited processes as running.

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

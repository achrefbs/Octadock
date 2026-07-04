# PRD: Octadock Screenshot Utility For Windows

Date: 2026-07-01
Status: Living draft. Several P0/P1 items are now implemented or partially
implemented; see `../PROJECT-STATE.md` for current state and `../ROADMAP.md` for
the active implementation plan.

## Problem Statement

Windows has Snipping Tool, Xbox Game Bar, Print Screen, and PowerToys-adjacent workflows, but the daily screenshot loop is still clumsy for power users: capture something, keep it visible, annotate it, drag it somewhere, or recover it later. Octadock solves that by turning every capture into a temporary desktop object docked at the bottom-left of the active monitor.

## Product Positioning

Octadock is a Windows desktop screenshot and recording utility centered around a Capture Shelf. It should feel instant, quiet, and dependable: capture first, decide what to do next, and avoid hunting through folders unless the user explicitly wants a saved file.

## Goals

- Reduce capture-to-paste time to under 2 seconds for repeat area captures.
- Make the core loop possible without File Explorer: capture, copy/save/annotate/pin/discard.
- Keep recent captures recoverable for up to 30 days without manual cleanup.
- Provide privacy-first local OCR, annotation, and history by default.
- Provide automation through hotkeys, `octadock://` URLs, and `octadock.exe` CLI commands.

## Non-Goals

- No CleanShot branding, visual cloning, assets, project-file compatibility, or proprietary endpoint compatibility.
- No hosted cloud service in v1.
- No non-Windows support in v1.
- No team administration or SSO in v1.
- No Microsoft Store-first requirement. MSIX can be added if OCR/package identity or distribution benefits justify it.

## Personas

- Builder: developer, designer, founder, PM, or support person who screenshots all day and wants capture-to-share speed.
- Writer: creates docs/tutorials and needs annotations, step markers, clean window captures, and background/padding tools.
- Debugger: records bugs, captures hover states, pins visual references, and extracts text from UI.
- Windows power user: uses AutoHotkey, PowerShell, Flow Launcher, PowerToys Run, or keyboard-first workflows.

## User Stories

- As a builder, I want a selected-area screenshot to appear on a shelf immediately so I can copy, drag, save, pin, or annotate it without finding a file.
- As a builder, I want the shelf on the bottom-left of my active monitor so it is predictable and out of my way.
- As a builder, I want recent screenshots to stay accessible briefly so I can recover something I accidentally closed.
- As a writer, I want arrows, text, highlights, blur, and counters so I can explain a workflow clearly.
- As a writer, I want to combine several captures on one canvas so I can create comparison images without opening a separate editor.
- As a debugger, I want to pin a capture above other windows so I can compare a reference while working.
- As a debugger, I want OCR on a selected region so I can copy text from non-selectable UI.
- As a Windows power user, I want protocol and CLI commands so I can trigger captures from AutoHotkey, PowerShell, or launcher tools.
- As a recorder, I want area recordings with microphone and cursor settings so I can show a bug quickly.
- As a vibe developer, I want Octadock to show active AI coding sessions and notify me when an agent/build needs attention so I do not lose track of background work.

## Requirements

### P0 Must Have

#### Tray App And Setup

Acceptance criteria:

- App launches as a Windows tray utility.
- Tray menu exposes capture actions, history, settings, pause, and quit.
- First-run setup explains capture permissions/limitations and configures startup behavior.
- User can enable/disable launch at login.
- App can run unpackaged during development.

#### Global Capture Hotkeys

Acceptance criteria:

- User can trigger area, fullscreen, window, and previous-area capture with configurable shortcuts.
- Shortcut conflicts are detected when `RegisterHotKey` fails.
- Escape cancels active capture without creating a shelf item.
- Default shortcuts avoid reserved Windows combinations where possible.

#### Area Capture

Acceptance criteria:

- User can draw a rectangle across any monitor.
- Selection shows dimensions and a magnifier.
- User can move/resize selection before capture.
- Capture does not include Octadock UI on Windows versions/APIs where exclusion is supported.
- Captured image respects DPI scaling and monitor coordinates.

#### Window And Fullscreen Capture

Acceptance criteria:

- Window mode highlights candidate windows under cursor.
- User can capture a visible app window.
- Fullscreen mode captures active monitor by default.
- Multi-monitor capture can be configured as active monitor, selected monitor, or all monitors.

#### Capture Shelf

Acceptance criteria:

- New captures appear in a bottom-left shelf on the active monitor by default.
- Shelf supports copy, save, annotate, pin, discard, drag/drop, and context menu.
- Shelf can be moved to another corner and size can be adjusted.
- Shelf auto-closes according to settings: never, 30 seconds, 1 minute, 5 minutes, or after action.
- Multiple captures stack without covering primary content excessively.
- Recently closed shelf item can be restored from tray menu or shortcut.

#### Clipboard And Drag/Drop

Acceptance criteria:

- Copy writes bitmap data to the Windows clipboard.
- Copy also offers file-drop-compatible data where useful.
- Dragging a shelf item works with File Explorer, browser upload fields, Teams/Slack/Discord, and common editors.
- Exported temp files remain valid long enough for target apps to consume them.

#### Annotation Editor

Acceptance criteria:

- Editor opens from shelf, file, clipboard, or history.
- Tools: crop, arrow, rectangle, text, blur, pixelate, highlighter.
- Supports undo/redo, copy, save, save as, export, and drag handle.
- Export flattens annotations into PNG/JPEG.
- Project save keeps the original raster plus editable vector objects.

#### Floating Pins

Acceptance criteria:

- User can pin a shelf item, editor image, file, or clipboard image.
- Pinned images float above normal app windows.
- Pins can be moved, resized, opacity-adjusted, copied, saved, annotated, and closed.
- Lock-through mode lets mouse clicks pass to apps underneath.
- Arrow keys nudge position when the pin is focused.

#### Local History

Acceptance criteria:

- Captures are indexed with type, timestamp, dimensions, source process/window when available, monitor id, action status, and file path.
- User can open, annotate, pin, copy, save, delete, and restore recent captures.
- Retention setting supports disabled, 1 day, 7 days, 30 days, and forever.

### P1 Should Have

#### All-In-One HUD

Acceptance criteria:

- One shortcut opens a compact HUD with area, fullscreen, window, scrolling, OCR, and recording modes.
- HUD remembers last selection rectangle and aspect ratio.
- HUD supports fixed dimensions and locked aspect ratios.

#### OCR

Implementation status: partial. Region/file OCR works through `Windows.Media.Ocr`
and copies text to the clipboard. Windows AI/Tesseract fallbacks, dedicated OCR
history rows, and richer OCR workflow polish still need work.

Acceptance criteria:

- User can select a region and copy recognized text to clipboard.
- User can run OCR on a file or shelf item.
- User can choose compact output or line-break-preserving output.
- OCR runs locally by default.
- App clearly reports if Windows OCR capability is unavailable and offers fallback.

#### Scrolling Capture

Implementation status: partial. Manual vertical scrolling capture works with a
visible session pill. Auto-scroll and horizontal scrolling are not implemented.

Acceptance criteria:

- User selects a scrollable region.
- App captures as user scrolls manually.
- App can attempt auto-scroll where UI Automation allows.
- App stitches captures into a single image and warns when output becomes too large.

#### Screen Recording MVP

Implementation status: partial. Active-monitor and selected-area MP4 recording
work with countdown, pill, shelf/history insertion, and reveal notification. Real
microphone/system-audio encoding is not implemented.

Acceptance criteria:

- User records fullscreen or selected area to MP4.
- User can include microphone and cursor.
- App shows a timer and stop control.
- Finished recording appears in Capture Shelf.

#### Automation

Implementation status: built, with drift notes. Protocol/CLI dispatch exists and
the `open` preview verb exists. Region-aware capture, OCR, recording, and
all-in-one HUD preloading are wired; recording audio/device parameters remain
planned.

Acceptance criteria:

- App registers `octadock://`.
- App exposes `octadock.exe` commands.
- Commands exist for capture area, previous area, fullscreen, window, self-timer, scrolling capture, pin, OCR, record, annotate, history, restore recent, and settings.
- Commands support optional coordinates and action parameters where relevant.

### P2 Future

- System audio recording through WASAPI loopback.
- GIF recording.
- Camera overlay.
- Keystroke and mouse-click visualization.
- Video trim/compress editor.
- Background/social-image tool.
- Advanced annotation tools: counter, ellipse, line, pencil smoothing, spotlight, smart highlighter, resize, rotate, flip, saved colors.
- Horizontal scrolling capture.
- WebP/AVIF/HEIF support where codecs are available.
- Upload plugins for local folder, OneDrive folder, S3/R2, WebDAV, and self-hosted server.
- Tags, share links, expiring links, password-protected links.

### Vibe-Developer / AI Session Track

- Active AI Sessions / Agent Mission Control: track local and remote AI coding
  sessions, including provider/tool, cwd, branch, command/PID, status, duration,
  logs, artifacts, and PR links. Current alpha supports generic local
  `run`/`watch`, persisted timelines, full stdout/stderr logs for wrapped runs,
  notifications, generic input-prompt waiting detection for wrapped runs, local
  CLI hook events, and an AI Sessions window; dock cards, PR links,
  provider-specific hook adapters, provider-specific waiting/log
  enrichment, and provider adapters remain.
- Generic run watching through `octadock run -- <command>` and
  `octadock watch --pid <pid>`.
- Provider-specific hook/event adapters for tools such as Claude Code, Codex
  CLI, Cursor agents, GitHub Copilot coding-agent sessions, Jules, and Vercel
  workflows where APIs or hooks are available.
- Dock/shelf cards for running, waiting, failed, completed, or PR-ready sessions.
- Toast and optional TTS notifications when a session needs attention or finishes.
- Context Shelf, Send to AI, Ask AI, MCP server, prompt/snippet library, and
  redaction before cloud sends.

## Success Metrics

Leading:

- Capture-to-shelf latency p95 under 500 ms for normal area captures.
- Capture-to-copy task completion under 2 seconds for repeat users.
- 80 percent of captures resolved by shelf action without opening File Explorer.
- Crash-free sessions above 99.5 percent in beta.

Lagging:

- 50 percent weekly retention among beta users who take 20 or more screenshots per week.
- Median beta satisfaction above 8/10.
- Fewer than 5 percent of captures fail due to DPI, multi-monitor, permission, or overlay-inclusion bugs.

## UX Principles

- The shelf is a tool surface, not a marketing surface.
- Default actions should be one click or one keystroke.
- Capture UI must disappear before actual capture.
- Do not cover the selected region after capture if the shelf can avoid it.
- Never upload anything without explicit user configuration and action.
- Every destructive action has undo or restore where possible.

## Open Questions

- Engineering: Should MVP use WPF end-to-end, or WPF overlays plus WinUI 3 settings later?
- Engineering: Is `Windows.Graphics.Capture` enough for still capture and recording, or do we need DXGI Desktop Duplication in MVP?
- Engineering: Which OCR path is most reliable on the target machines: Windows App SDK AI OCR, Windows.Media.Ocr with MSIX, or bundled Tesseract?
- Design: What original Windows visual language should replace CleanShot's overlay aesthetic?
- Product: Is cloud upload part of the roadmap or strictly BYO/self-hosted?
- Legal: Confirm naming, iconography, and feature descriptions are sufficiently distinct before public release.

## Release Phases

### Alpha 1: Capture Shelf Loop

- Tray app.
- Global hotkeys.
- Area/fullscreen/window/previous capture.
- Capture Shelf with copy/save/discard/drag/drop.

### Alpha 2: Annotation And Pins

- Annotation editor.
- Project file.
- Pinboards.
- Local history.

### Beta 1: Power User

- OCR.
- All-in-one HUD.
- Protocol and CLI automation.
- Scrolling capture.
- File preview.
- Dictation.

### Beta 2: Recording

- MP4 recording.
- Microphone.
- Cursor setting.
- Shelf integration.

### Beta 3: Vibe Developer

- Active AI Sessions / Agent Mission Control.
- Clipboard history.
- Command palette.
- Prompt/snippet library.
- Context Shelf and local-first Ask AI.
- MCP server.

### 1.0

- Polish, settings, performance, installer, optional MSIX, docs, automated tests, and opt-in local crash reports.

# Octadock Project State

Last audited: 2026-07-03

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
- Clipboard history end to end: a `WM_CLIPBOARDUPDATE` monitor gated by the
  Settings → Clipboard tab, privacy filtering for password-manager exclusion
  formats and Octadock's own writes, hash-based de-duplication with seen
  counts, managed PNG + thumbnail storage for image clips, cap-based trimming
  that spares favorites, a searchable/filterable Clipboard window with
  copy/favorite/delete/clear actions and live refresh, a `Ctrl+Shift+9`
  hotkey, tray/Dock entry points, and `octadock open-clipboard-history`
  automation. Everything stays local.
- Text-transform toolbox: 28 local transforms (JSON format/minify, Base64/URL/
  HTML encode-decode, JWT decode, identifier casing, MD5/SHA hashes, Unix
  timestamp conversion, line sort/dedupe/trim/count) in a live two-pane window
  reachable from the tray and `octadock open-text-tools`.
- OCR history rows: region OCR grabs persist the source image as an
  `OcrSource` capture plus an `OcrExtracted` action carrying the text, and the
  History window's OCR filter plus "Copy Text" action recover the text later.
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
- Theme-native window chrome: every window gets a dark/light title bar,
  theme-matched caption colors, and Windows 11 rounded corners that follow
  live theme switches, plus app-wide themed scrollbars, context menus,
  tooltips, sliders, radio buttons, and progress bars. The preview card,
  annotation accent, and pin toolbar glyphs are unified on the brand teal /
  Segoe MDL2 language.
- File preview / Windows glass viewer first slice: `octadock open`, CSV/text/image
  providers, a reusable preview card, CSV filtering/sorting/stats, unsupported
  file-info fallback, file associations, shelf file-drop entry, UNC rejection,
  open confirmation, and copyable preview content. The card has draggable
  Windows glass chrome, custom preview scrollbars/context menu,
  provider-aware badges, and
  provider-level preview errors no longer create a duplicate command-failure
  notification after the card opens.
- Speech-to-text: dock dictate action, WASAPI microphone capture, local
  Parakeet TDT 0.6B v3 default engine (sherpa-onnx int8, ~0.06 RTF measured,
  native punctuation/casing, 25 European languages, pinned SHA-256 manifest
  with resumable download), local Whisper fallback provider, opt-in OpenAI
  transcription provider, auto-language detection with per-utterance routing
  to Whisper for languages outside Parakeet's coverage, whisper-for-now
  stopgap while the Parakeet model downloads in the background, startup
  recognizer warm-up, built-in and editable code-term dictionary, speech
  settings for provider/model/language/insertion mode and provider
  availability, configurable toggle hotkey, local-only CLI toggle command,
  dictation pill, paste-at-cursor with clipboard restore, and
  microphone-privacy-settings help when Windows blocks capture.
- Command Deck grouping (first slice): the dock's actions are clustered
  capture · record · text&voice · library · settings with separators and
  all-MDL2 glyphs (no more mixed text buttons), and Settings collapsed from
  12 flat tabs into 4 sections (Capture / Voice / Library / System) with
  nested sub-pages; `SelectTab` deep links resolve section + sub-page.
  Remaining Phase-4 work: evolve the History window into the Deck console
  (facets by date/type/app, recent-activity feed, voice panel) and the
  themed-DatePicker/theme polish backlog.
- Read aloud v2 (verbatim-first): default flow extracts text and speaks it
  as written through the built-in Windows voices (WindowsTtsProvider, no
  download/key, offline), sentence-chunked with prefetch so audio starts
  after the first chunk; playback pill with pause/resume/stop and elapsed;
  `--explain` (and the explain/summarize verbs) opt into the AI explanation
  pass, which now falls back to Windows voices when ElevenLabs is absent;
  Ctrl+Shift+0 hotkey; Settings → Read aloud (provider/voice/rate). The
  screen-discovery overlay remains on the roadmap, out of scope for this pass.
- Live dictation partials: embedded Silero VAD (offline, extracted on first
  use) + a provider-agnostic simulated-streaming session in Core — VAD-closed
  segments decode once (stable), the open tail re-decodes every ~400 ms
  (volatile, dimmed on the pill), and finalize decodes only the tail so
  stop-to-text stays instant regardless of utterance length (verified live:
  first sentence frozen mid-utterance, finalize <1 s). Pill discard button,
  speech-reactive dot, `speech.livePartials` (default on) and
  `speech.autoStopOnSilence` (default off, ~2 s) settings; falls back to
  plain record-then-transcribe when the provider or VAD cannot stream.
- Opt-in local crash reports: when enabled in Settings, unhandled dispatcher,
  domain, unobserved task, and startup-failure exceptions save redacted JSON
  reports under `%LOCALAPPDATA%\Octadock\CrashReports`; there is no uploader or
  telemetry transport.
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
- STT engine quality is now in the target band: Parakeet TDT v3 (default)
  measured RTF 0.062 with correct punctuation/casing on the live benchmark
  (`OCTADOCK_LIVE_STT=1`, report in `%TEMP%\octadock-stt-bench.txt`); settings
  v4 migrates persisted `whisper` selections to `parakeet` once. Whisper
  (`small` default) remains for the 99-language tail, and the opt-in OpenAI
  provider can use `gpt-4o-transcribe`, `gpt-4o-mini-transcribe`, or
  `whisper-1` when `OPENAI_API_KEY` or `OCTADOCK_OPENAI_API_KEY` is
  configured. The audio path prefers the normal Windows mic endpoint before
  the communications endpoint and logs audio diagnostics, and a configurable
  toggle hotkey defaults to `Ctrl+Shift+2`; `octadock dictation` toggles from
  the CLI while `octadock://dictation` is blocked so external URI activation
  cannot start the microphone. Hold-to-talk now exists via
  `speech.activationMode` (toggle/hold/both; LL keyboard hook with injected-
  input filtering and a stuck-key watchdog, hotkey unregistered while a hold
  mode owns the gesture, paste waits for modifier release). Settings →
  Dictation is Describe()-driven (availability-labeled provider picker) with
  a local-model manager (download progress/delete/sizes) and live-partials/
  auto-stop toggles — Dictation v2 is feature-complete; remaining polish
  rides with Phase 4 (Voice settings section grouping).
- File preview is not proposal-complete. Missing pieces include a polished
  source-rect open animation and Ask AI. Exporting a copy, image add-to-shelf,
  image pinning, provider-aware badges, and shelf file-drop entry are now wired.
- OCR history rows exist for region OCR grabs; file-based OCR
  (`capture-text --filepath`) still copies/notifies without a history row.
- Settings expose the new Clipboard tab, but do not yet expose Ask AI, TTS,
  MCP, upload targets, or code beautifier controls.

## Not Implemented Yet

- MCP server for exposing captures/OCR/clipboard/context to Cursor, Claude Code,
  Codex, and other tools.
- Send to AI, Ask AI, Context Shelf, prompt/snippet library, local Ollama
  integration, hosted model provider settings, AI redaction pipeline, and AI
  cost/session analytics.
- Command palette, code screenshot beautifier, color picker, scratchpad notes,
  and the remaining developer mini-tools. (Clipboard history and the
  text-transform toolbox shipped in the 2026-07-05 pass.)
- Upload/share plugins, share links, password-protected links, and team/cloud
  administration.
- Installer, code signing, auto-update, and public beta distribution polish. A
  framework-dependent release zip script now exists, but it is not a signed
  installer. Local opt-in crash-report files exist; uploading/telemetry does not.

## Removed

- **Active AI Sessions / Agent Mission Control** was removed on 2026-07-05:
  auto-discovery, the `run`/`watch`/hook CLI tracking, the AI Sessions window,
  the passive overlay, and its database tables are gone (schema migration 6
  drops the tables). Local session discovery proved unreliable, and the product
  refocused on dictation, read-aloud, and design consolidation.

## Main Risks

- Multi-monitor/DPI behavior must be verified on real hardware after every
  overlay, dock, shelf, pill, or pin positioning change.
- STT quality and latency are user-visible trust issues. The local-only path may
  not be enough; the product needs a fast optional high-accuracy provider with
  clear privacy boundaries.
- Recording audio remains unimplemented, but the current UI/settings no longer
  expose enabled toggles that imply microphone/system audio works.
- AI tooling touches shells, logs, files, clipboard, and secrets. It needs
  local-first defaults, redaction, and explicit opt-in for cloud calls.
- Protocol/file preview/AI actions are security-sensitive; externally triggered
  file opens and model sends need confirmation and path restrictions.

# Changelog

All notable changes to Octadock are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Removed — Phase 1 product-change batch (2026-07-23)

- Generic file preview: the preview card window/host/inspector/recovery UI and
  the CSV/TSV, JSON, Markdown, log, text/code, and metadata providers, plus the
  generic local-image "image surface". The `open` verb is now a truthful
  tombstone (non-zero "feature removed" error, absent from help).
- Floating pins: pin window/service, quick pen, persisted restore, Gather,
  inline pin AI edit, and the pin post-capture action. `pin` is a tombstone;
  `pins` database rows and files remain in place, inert (no destructive
  migration).
- The all-in-one capture HUD (window/service/state/shortcut/settings); the
  `all-in-one` command is a tombstone. Tray Pause/Resume is removed; the
  unrelated cross-flow CaptureGate serialization semaphore was kept.
- "Open a File" tray action, arbitrary-file Shelf drops (the Shelf now accepts
  Octadock captures and recordings only), and arbitrary-file/clipboard
  annotation entries (`open-annotate`, `open-from-clipboard`,
  `add-shelf-item` are tombstones).
- Explorer "Open with Octadock" preview associations and image shell verbs;
  the legacy per-user registrations are actively unregistered at startup as
  upgrade cleanup.

### Added — Phase 1 product-change batch (2026-07-23)

- Recording audio (still Beta): optional microphone (WASAPI) and system/app
  (loopback) AAC tracks as explicit Settings opt-ins (default video-only), a
  sample-count audio clock with bounded drift correction,
  pump-failure/device-loss/disk-pressure handling, validated finalization
  (frames + nonzero size + MP4 ftyp/mdat/moov + mvhd duration), crash-remnant
  sweep, and a SessionEnding finalize budget. No false success: failed output
  is deleted and no History row is written.
- Dictation hardening: a ten-state controller (Idle/Preparing/Listening/
  Transcribing/Inserting/AwaitingReview/Completed/Discarded/Cancelled/Failed)
  with distinct pill phases; honest insertion (no false success, exact
  clipboard restore including non-text, no duplicate paste); a
  review-before-insert insertion mode; failure-specific recovery guidance; a
  microphone picker (`speech.microphoneDeviceId`); model consent showing
  provider/size/storage location; model manager cancel/retry/delete; model
  integrity verification with corrupt quarantine; device-loss surfacing; and a
  versioned SAPI-corpus benchmark at `tools/acceptance/stt/` with a recorded
  baseline.
- History has an explicit Annotate action for image captures; annotation
  accepts Octadock capture images and capture-derived `.octadock` projects
  only, records an honest `Annotated` action, and fails visibly on
  missing/corrupt sources.
- Shelf "Delete permanently…" route with a default-No themed confirmation that
  deletes the file and History row; the × stays dismiss-only.
- Design-system foundation: semantic brush aliases, metric tokens (border
  thickness, shadows, motion, font sizes, Lucide icon ramp 12/14/16/20/24),
  shared styles with a usage guide in `Resources/Themes/Shared.xaml`, unified
  floating surfaces with reduced-motion gating, unified standard windows, one
  themed default-Cancel ConfirmationDialog for destructive flows, and a Lucide
  annotation editor toolbar with automation names.

### Changed — Phase 1 product-change batch (2026-07-23)

- The Dock is now minimal: a split Capture button (Area on click; Window,
  Full screen, All monitors, Previous area, Timer, Scrolling manual-vertical
  Beta, OCR, Record in its menu), Dictate, Shelf, History, and a More menu
  (Clipboard, Context, Settings, Account/About, Exit). The standalone
  reviewed-handoff Dock button is gone; contextual Shelf/History/Context/tray
  entries remain.
- The tray menu is regrouped (Capture submenu, Dictate, library group, app
  group) and tray left-click toggles the Shelf.
- Shortcut defaults are only Area `Ctrl+Shift+4`, Full screen `Ctrl+Shift+3`,
  and Dictate `Ctrl+Shift+2`; other supported chords ship unassigned but
  editable, and stored user chords are preserved. Launch at login defaults on
  for new profiles with a first-run opt-out.
- Capture settings split into Basic (save folder, image format, cursor,
  selection incl. freeze-screen and the new combined Precision aids setting —
  both default on) and Advanced (filename template, JPEG quality, window
  shadow/frame, monitor behavior, fixed size/aspect, timer, manual vertical
  scrolling Beta). No user-facing capture-engine controls remain.

### Changed

- Tagged `v<version>` releases now run a strict Windows packaging workflow that
  validates tag/source identity, records executed public-artifact-boundary
  evidence, produces a versioned ZIP and manifest, and writes and verifies
  SHA-256 checksums for the uploaded release deliverables.
- The landing page now uses a persistent, scroll-independent WebGL aquarium
  with seven established wireframe octopuses across three depth bands, plus
  three much smaller juvenile swimmers. Each
  animal has an independent skeleton, hydrostat solver, route, breath rhythm,
  arm-recruitment seed, and navigation state; residents roam from top to
  bottom, idle, meet in one social pair, inspect, and avoid every other body.
- Touching an octopus gives only that animal a finite two-pulse escape. Nearby
  residents orient and briefly freeze after staggered delays instead of copying
  the jet, and escape recovery continues forward before broad roaming resumes.
- Octopus locomotion now uses fixed-step thrust, anisotropic drag, added-mass
  mantle dynamics, bounded turning torque, distributed arm inertia, and
  aspect-correct screen-space collision/hit testing instead of tying animation
  phase or direction to page scroll. The shared GLB is parsed once, each rig
  receives an independent skeleton clone, and depth-tiered pose updates keep
  the ten-agent aquarium within the existing rendering budget.
- Calm mantle-first roaming now keeps a restrained trailing/V arm crown instead
  of forcing all eight arms through a full umbrella stroke. Small balanced arm
  phase offsets plus axial and tangential middle-arm relief prevent the power
  pose from collapsing into a flat fan; full bundling remains exclusive to an
  escape jet. Foreground residents update their complete hydrostat pose at
  60 Hz, while distant residents no longer drop below 30 Hz.
- Social rendezvous use a slightly shorter 7–11 second quiet interval while
  retaining exactly one reciprocal pair at a time, so contact is easier to
  notice without turning the aquarium into coordinated schooling.
- Unbothered residents now cross the aquarium with lower routine thrust and a
  slower 4.6–6.0 second arm cadence, while touch escape keeps its original
  high-speed turn and two-pulse jet. Render-only root interpolation removes
  fixed-step stair-stepping without delaying hit testing or physical response.
- The simulated workstation showcase has been removed. The landing page now
  keeps one continuous aquarium behind an ordinary, responsive product story:
  hero → Capture → Use → Keep → eight-instrument index →
  local-first ledger → automation → pricing → release status. The three
  narrative chapters and restored detailed index now work together instead of
  forcing one view of the product to replace the other.
- The aquarium typography pass raises the contrast of the large CAPTURE, USE,
  and KEEP depth words, and expands the ambiguous one-word decoration to STAYS
  YOURS so it fills the open-water composition with a clearer message.
- STAYS YOURS is vertical again in the hero, while shorter hero and story
  scroll lengths bring the first product chapter forward. The former additive
  purple thermocline plane is replaced by a broad, low-strength depth tint so
  the optional-cloud passage changes temperature without a hard light seam.
- The former records deck and separate cloud beat are now one concise
  local-first section. Rounded proof cards have been replaced by ruled
  editorial rows that lead directly into the complete five-path egress ledger;
  the current video, Context Stack, and website-tracking limits remain visible
  without repeating them.
- The automation terminal has a keyboard-operable copy control with live status
  feedback. The final installer action is deliberately disabled until the
  signed build, download host, and matching SHA-256 can ship together.
- The dock now reads as a command deck: actions are grouped
  [capture area/window/screen/scroll] · [record] · [OCR/read/dictate] ·
  [history/clipboard/file] · [settings] with separators between clusters,
  and every action uses a Segoe MDL2 glyph (the mixed text buttons — Read,
  Clip, File, ⚙ — are gone).
- Settings reorganized from 12 flat tabs into four sections: **Capture**
  (screenshots, recording, annotate), **Voice** (dictation + read aloud),
  **Library** (shelf, history, clipboard, OCR), and **System** (general,
  shortcuts, automation, advanced). Deep links (`open-settings` +
  tab keys) still land on the right sub-page.

### Added

- Three micro residents—Quick Silver, Little Current, and Ink Spark—reuse the
  complete octopus rig with smaller collision/effect scales and a dedicated
  fast locomotion profile. They cross the tank at more than twice the adult
  roaming speed, turn and jet away quickly when touched, and remain outside
  adult meetings and propagated startles so the approved seven keep their calm
  social behavior.

- A bounded, single-draw underwater effects pool adds ambient bubbles,
  distance-sampled mouse wakes, a shiny bioluminescent star cursor, and
  siphon/jet bubbles during escape. The effects share the aquarium render
  target, fog, bloom, and color grade while the aquarium remains continuous
  behind every section. Controls, coarse pointers, reduced motion, and
  forced-colors mode preserve the native cursor.

- Read aloud v2 — verbatim first, local first: `octadock read` (tray "Read
  Region Aloud", dock Read, or the new `Ctrl+Shift+0` hotkey) now speaks the
  selected text exactly as written through the built-in Windows voices — no
  AI CLI, no API key, on-device, with audio starting after the first
  sentence chunk (synthesis of the next chunk overlaps playback). A playback
  pill offers pause/resume and stop with elapsed time. The AI explanation
  flow is one flag away (`read --explain`; the `explain`/`summarize` verbs
  imply it) and now also works keylessly by falling back to Windows voices
  when ElevenLabs is not configured. New Settings → Read aloud section picks
  the voice provider (windows/elevenlabs), a voice, and the speaking rate
  (0.75–2×, applied at synthesis).
- Settings → Dictation grew into a real speech control panel: the provider
  picker now reflects live availability (unavailable engines are labeled), a
  new "Local models" manager shows whether Parakeet and the selected Whisper
  model are on disk with their download sizes and offers download-with-
  progress and delete without leaving Settings, live-partials and
  auto-stop-on-silence get checkboxes, and the developer dictionary gains a
  syntax hint.
- Push-to-talk dictation: a new "Activation" setting (`speech.activationMode`)
  chooses how the dictation shortcut behaves — `toggle` (default, unchanged),
  `hold` (keep the key down to talk, release to insert; a quick tap discards
  as accidental), or `both` (hold to talk, tap to toggle). Hold modes use an
  opt-in low-level keyboard hook that ignores injected input (Octadock's own
  paste can never re-trigger it), swallows the chord so the key never types
  into the focused app, and a watchdog recovers the release even when it
  happens over an elevated window. Pasting now waits for physical modifier
  keys to clear first, so releasing Ctrl+Shift late can no longer turn the
  injected Ctrl+V into Ctrl+Shift+V.
- Live dictation partials: while you speak, the dictation pill shows the
  transcript growing in real time — text confirmed by the voice activity
  detector renders solid, the still-decoding tail renders dimmed. Silero VAD
  (embedded, ~630 KB, local) splits speech into segments so finished
  sentences are decoded exactly once and stopping only decodes the last few
  words, making stop-to-text effectively instant even after long dictations.
  The pill gains a discard button and its dot now pulses only while speech is
  detected. New settings: `speech.livePartials` (on by default; requires the
  Parakeet engine) and `speech.autoStopOnSilence` (off by default; stops and
  inserts after ~2 s of silence).
- Dictation's new default engine: NVIDIA Parakeet TDT 0.6B v3 (int8) running
  on-device after model download via sherpa-onnx. It transcribes 20-30× faster
  than realtime on ordinary CPUs with native punctuation/casing across 25
  European languages. The ~640 MB model downloads once (resumable, SHA-256
  verified); if a Whisper
  model is already on disk the first dictation uses Whisper immediately while
  Parakeet fetches in the background, and a toast announces the upgrade.
  Explicitly configured languages outside Parakeet's coverage automatically
  route that utterance to Whisper (99 languages). Existing "whisper" provider
  settings migrate to "parakeet" once; Whisper and the opt-in OpenAI cloud
  provider remain selectable in Settings → Speech to text.
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
- Context Stack first slice: persistent local Context packages, snapshot/reference
  ownership, package navigation, add/open/delete items, folder export, zip export,
  and relative-path manifests with no absolute source-path leaks.
- Image files now open through the floating image surface instead of the old
  image preview card. The surface supports quick pen annotations, save same/new
  choice, advanced annotation, source reveal/open, Add to Context, and
  pin/unpin topmost.
- Trial and licensing spine: 14-day local trial, signed entitlement file outside
  the SQLite database, Account & Billing activation UI, `octadock://activate`,
  ambient trial status, and service-seam gates for paid features after expiry.
- Isolated license service under `services/license-service`: Stripe webhook
  verification, license issuance/revocation, activation, signed entitlements,
  trust-anchor endpoint, admin health, alert seams, and optional Stripe
  reconciliation source.
- Release packaging now publishes self-contained single-file win-x64 app and CLI
  outputs, then creates a versioned zip with manifest and SHA256SUMS.

### Removed

- Active AI Sessions (auto-discovery, run/watch/hook tracking, window, overlay,
  database tables; schema migration 6 drops the tables).

### Changed — the "obsidian glass" redesign (2026-07-05)

- Octadock has a new dark-first visual identity: deep blue-black surfaces, a
  signature teal→cyan gradient on primary actions, an accent indicator bar on
  the settings navigation, larger card radii, and a soft window backdrop
  gradient. **Dark is now the default theme** — a one-time v3 settings
  migration moves users who never made an explicit choice from System to Dark;
  Light and System remain selectable and Light was refreshed to white cards on
  a cool tinted canvas.
- The dock capsule, recording/dictation/scrolling/countdown pills, and the
  file preview card moved onto the same deeper obsidian glass base color.

### Fixed — store durability (2026-07-05, second pass)

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

### Changed

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

### Planned

- Continue the `0.2.x` alpha line with design-system enforcement, Context Stack
  polish, rich file previews, scrolling reliability, Ask AI/MCP, recording audio,
  and installer/release polish.

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

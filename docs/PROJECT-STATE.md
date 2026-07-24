# Octadock Project State

Last audited: 2026-07-23

This file is the current source of truth for what Octadock actually does today.
Older specs, wargames, proposals, and launch plans are product history; trust
this file and `docs/CAPABILITIES.md` first when docs disagree with code.

Current integrated alpha version: `0.2.0-alpha.0`. Version metadata lives in
`version.json`, `build/version.props`, and `docs/VERSIONING.md`.

## Current Product Shape

Octadock is a local-first Windows capture-to-context workspace. Its primary
launch workflow is:

`capture / dictate / Context / History / Clipboard -> choose outcome -> add missing intent -> review -> finish`

Capture, cursor dictation, Context, and explicit reviewed handoff are the four
primary pillars. Clipboard history, local text tools, read aloud, capture
annotation, and automation remain useful secondary capabilities. Manual
vertical scrolling capture and MP4 screen recording (optional microphone and
system/app audio, default video-only) are explicitly Beta. Octadock does not
watch the screen or discover model/tool activity in the background.

Generic file preview, the generic image surface, floating pins, and the
all-in-one capture HUD were removed from the product on 2026-07-23 (owner
decision); see Removed From Product Direction.

## Verification Snapshot

Phase 1 product-change batch (11 commits) landed on `main` on 2026-07-23.
**Final Release gate run pending (2026-07-23)** — the coordinator fills in the
post-batch test-count table after the canonical
`.\build\build.ps1 -Configuration Release` run. Until then, treat the
2026-07-19 snapshot below as the last recorded automated evidence, not the
current count.

Last recorded pre-batch evidence, observed on 2026-07-19 from `main` at
`219b487` plus the fingerprinted dirty candidate source state:

- Canonical `.\build\build.ps1 -Configuration Release` passed.
- Desktop solution: 1,219/1,219 tests passed (Core 729, App 329, Data 88,
  Platform.Windows 50, CLI 23).
- License service: 75/75; internal Workflow Intelligence: 27/27, still excluded
  from public artifacts.
- Website: 15 syntax, 6 static-contract, and 21 Chromium tests passed.
- Self-contained App/CLI publish, version synchronization, copy-honesty, and
  public-artifact boundary passed.
- Acceptance tooling: 30 self-test assertions; dictation 59/59 deterministic;
  WPF static scan clean with 0 new and 0 baselined findings.
- GitHub Actions is still not a valid remote signal because runs fail before
  jobs begin. Real microphone, assistive-technology, mixed-DPI, performance,
  soak, and clean-VM evidence is not implied by the automated pass.

Post-batch automated evidence already on record: the versioned STT benchmark
baseline at `tools/acceptance/stt/` (numbers under Speech-to-text below) and
the deterministic dictation matrix now at 74 rows.

The automated suite is strong for parsers, persistence, services, gates, and
headless view models. It does not prove pixel-level WPF quality, mixed-DPI
overlay placement, scrolling capture reliability on real apps, microphone/device
behavior, or clean-VM launch readiness.

## Built And Wired

- Windows tray utility, single-instance guard, first-run flow, settings window,
  startup/protocol controls, and a permanent minimal Dock capsule. The Dock is
  a split Capture button (Area on click; Window, Full screen, All monitors,
  Previous area, Timer, Scrolling manual-vertical Beta, OCR, and Record in its
  menu) plus Dictate, Shelf, History, and a More menu (Clipboard, Context,
  Settings, Account/About, Exit). The tray menu is regrouped into a Capture
  submenu, Dictate, a library group, and an app group; tray left-click toggles
  the Shelf. There is no Pause/Resume and no standalone reviewed-handoff Dock
  button (the engine keeps its contextual Shelf/History/Context/tray entries).
- Global hotkeys: only Area (`Ctrl+Shift+4`), Full screen (`Ctrl+Shift+3`), and
  Dictate (`Ctrl+Shift+2`) are assigned by default; window, previous-area, OCR,
  recording, clipboard history, and read aloud chords are supported but
  unassigned and editable. Stored user chords are preserved. Launch at login
  defaults on for new profiles with a first-run opt-out; existing choices are
  preserved.
- Area capture with per-monitor selection overlays, live dimensions and
  magnifier (combined as the **Precision aids** setting, default ON),
  freeze-screen selection (default ON), previous-region recall, self-timer
  countdown pill, DPI-aware geometry, and Octadock window exclusion where
  Windows supports it. Settings exposes the inverse opt-in, **Include Octadock
  overlays in screen captures and desktop recordings**, and reapplies it live
  to floating Octadock surfaces. There are no user-facing capture-engine
  controls; WGC/GDI fallback stays automatic.
- Window and fullscreen capture through the Windows platform layer, including a
  per-monitor window picker and WGC/GDI fallback behavior.
- Capture Shelf with smaller uniform card canvases, aspect-fit image media,
  visible recording plates, keyboard/hover actions, click-to-open, drag/drop,
  and history integration. It accepts Octadock captures and recordings only
  (arbitrary-file drops are removed). The explicit × dismisses only the Shelf
  card (the capture stays in History and on disk); a separate **Delete
  permanently…** route with a default-No themed confirmation deletes the file
  and History row truthfully. A fixed corner tab collapses/restores in place,
  and the idle Shelf follows the active monitor on the same low-frequency
  cadence as the Dock.
- Local SQLite history for captures, actions, settings, clipboard clips,
  thumbnails, soft delete/restore, retention cleanup, and Context records.
  Legacy `pins`/preview rows remain inert; no destructive migration.
- Clipboard history end to end: `WM_CLIPBOARDUPDATE` monitor, password-manager
  exclusion formats, own-write suppression, dedupe/seen counts, image storage,
  favorites, search/filter UI, copy/delete/clear, cap-based trimming,
  Dock/tray entry points, and `octadock open-clipboard-history`.
- Text-transform toolbox with 28 local transforms: JSON format/minify, Base64,
  URL, HTML, JWT decode, identifier casing, MD5/SHA hashes, Unix timestamp
  conversion, line sort/dedupe/trim/count, live output, and chaining.
- OCR through `Windows.Media.Ocr` with region/file entry points and compact,
  lines, and layout modes. Region OCR persists an `OcrSource` capture plus an
  `OcrExtracted` action so History can recover the text.
- Annotation editor with crop, select/move, arrow, rectangle, ellipse, line,
  text, highlighter, blur, pixelate, counter, freehand, undo/redo, export,
  copy, save, drag handle, and `.octadock` project packages, on one Lucide
  icon language with automation names. Annotate accepts Octadock capture
  images and capture-derived `.octadock` projects only: post-capture, Shelf,
  and History Annotate open `IAnnotationService.OpenAsync(CaptureRecord)` and
  record an honest `Annotated` action; History has an explicit Annotate action
  for image captures; missing or corrupt sources fail visibly.
- Automation through `octadock.exe` and `octadock://`, using the same Core
  parser and per-user named-pipe forwarding to the running tray instance.
  Settings gate CLI/protocol dispatch before parsing. Replacing a distinct
  existing entitlement through an activation command requires a visible,
  default-No identity comparison. Successful capture commands add the exact
  durable `captureId` to `--json` and IPC replies; cancellation before
  artifact creation is a truthful failure. Removed features keep parse-level
  tombstones (`pin`, `open`, `open-annotate`, `open-from-clipboard`,
  `add-shelf-item`, `all-in-one`): dispatch returns a non-zero "feature
  removed in this version" error and the tokens are absent from help.
- `open-context` / `context-stack` command opens the Context window. The legacy
  command alias remains for automation compatibility; the product name is
  Context.
- Context: persistent named packages, snapshot/reference ownership, verified
  large-file references, add files/captures/images, package navigation,
  per-item include/exclude review, notes, durable reorder, export preview,
  open/delete, and safe folder/zip export. Handoff/export includes exactly the
  reviewed item set; missing, changed, or unseen references fail closed.
  Manifests use relative paths without absolute path leaks. Create/add is
  license-gated; viewing/exporting existing packages remains available after
  expiry.
- **Beta — manual scrolling capture:** visible session pill and stitched
  output for manual vertical movement. Horizontal and auto-scroll modes are
  rejected; motion/overlap checks and memory limits reduce invalid output but
  do not imply universal app/page support.
- **Beta — screen recording:** active-monitor and command/tray selected-region
  MP4 video with optional microphone (WASAPI) and system/app (loopback) AAC
  audio tracks; both are explicit opt-ins in Settings and the default remains
  video-only. Audio uses a sample-count clock with bounded drift correction.
  Pump-failure, device-loss, and disk-pressure are handled; finalization
  requires frames, nonzero size, and a valid MP4 structure (ftyp/mdat/moov
  plus mvhd duration); crash remnants are swept and SessionEnding finalizes
  within a budget. There is no false success: failed output is deleted and no
  History row is written. Countdown, pill, stop flow, history/shelf
  integration, and managed storage are wired.
- Design system: semantic brush aliases plus metric tokens (border thickness,
  shadows, motion, font sizes, icon ramp 12/14/16/20/24) and shared styles
  (Button, DangerButton.Filled, ListBox(Item), StatusPill, DialogFooter,
  Text.Label) with a usage guide in `Resources/Themes/Shared.xaml`. Floating
  surfaces (all pills, Dock, Shelf, overlays) are unified, including
  reduced-motion gating; standard windows share ListBox styles, SectionCard,
  quiet empty states, and danger-styled destructive actions. One themed
  default-Cancel ConfirmationDialog covers destructive flows (Shelf permanent
  delete, clear clipboard, clear history, delete Context). The WPF static
  token/accessibility gate is clean (0 new, 0 baselined); rendered and
  assistive-technology acceptance remains separate.
- Speech-to-text: Dock dictate action, WASAPI microphone capture, local
  Parakeet TDT 0.6B v3 default engine via sherpa-onnx, explicit local Whisper
  fallback, opt-in OpenAI provider only via `OCTADOCK_OPENAI_API_KEY` (never a
  silent cloud fallback), auto-language routing, and live partials via
  embedded Silero VAD. Hardened transcript formatter/join, Unicode-boundary
  dictionary, and long-session incremental join; model integrity verification
  with corrupt quarantine and retryable failures; device loss is surfaced
  (WasInterrupted/CaptureInterrupted). A dictation state machine
  (Idle/Preparing/Listening/Transcribing/Inserting/AwaitingReview/Completed/
  Discarded/Cancelled/Failed) drives distinct pill phases; insertion is honest
  (no false success on focus/paste failure, exact clipboard restore including
  non-text, no duplicate paste), with a review-before-insert insertion mode
  and failure-specific recovery guidance. Settings include a microphone picker
  (`speech.microphoneDeviceId`); model consent shows provider, size, and
  storage location; the model manager has working cancel/retry/delete.
  `octadock://dictation` is blocked. A versioned benchmark lives at
  `tools/acceptance/stt/` (23-case manifest, SAPI-generated corpus,
  `Invoke-SttBenchmark.ps1`); baseline on an AMD Ryzen 7 5800H (16 threads,
  31.4 GB, Windows 11 26200): cold start 2,785 ms; warm median RTF 0.062;
  onset→first partial 64–132 ms wall (fast-forward feed); 0 dropped/duplicated
  segments across 20 streaming cases; batch WER: natural-english 2.9%,
  short-phrases 0%, dev-vocab 3.6%, long-form 0%, punctuation 0%, quiet-mic
  0%, noise 5.9%, commands 18.2%, filenames/paths 25.8%, numbers 57.1%
  (digit-vs-word tokenization), auto-language 48.3% (es/fr SAPI).
  Accent/German and all real-microphone rows remain PENDING.
- Read aloud v2: verbatim by default through built-in Windows voices, sentence
  chunking/prefetch, playback pill, pause/resume/stop, provider voice/rate
  settings, and optional ElevenLabs TTS. `--explain` now opens Use with AI
  with an editable investigation goal, and its result can be read aloud.
- Use with AI: an outcome-first surface entered from the current Shelf
  capture, included Context items, selected History/Clipboard row, tray, or
  dictated intent. Build, Investigate, Verify, Extract, and Handoff seed
  concrete goals and acceptance criteria before the advanced packet review.
  The review turns recent captures, Context packages, clipboard text/images,
  local files, annotations, and local OCR into a deterministic `TASK.md` plus
  `manifest.json` and an exported `SHA256SUMS`. Every asset has a safe
  relative path and SHA-256; changed Context references and evidence changed
  after review fail closed. Text secret redaction defaults on, including
  namespaced/bracketed assignments and complete quoted values; shared
  line-ending normalization keeps fake source delimiters inside indented
  untrusted data. The UI explicitly warns that attached screenshot pixels
  remain unchanged. Before/after verification adds both images, a
  deterministic heat map, changed-pixel ratio, mean/max delta, and change
  bounds. A destination-named confirmation gates a read-only Codex/Claude CLI
  handoff: Codex is ephemeral with shell/exec disabled and explicit image
  attachments; Claude receives only packet-relative Read/Glob access. There is
  no provider fallback, write-enabled execution, API-key storage, or
  prompt/session history. Normal temporary packets are deleted immediately;
  link-skipping retention scavenges crash leftovers. Results remain ephemeral
  unless copied. The stable `ai` command, historical aliases,
  `ShowAiActions` presenter seam, and legacy action services remain as
  backward-compatible adapters into the new workspace.
- Licensing/trial spine: 14-day local trial, monotonic high-water trial clock,
  signed entitlement file outside `octadock.db`, client Ed25519 verification,
  machine hash, Settings > Account & Billing activation UI, `activate`
  command, `octadock://activate`, ambient tray/dock trial status, and
  service-seam gates for paid features after expiry. Strong offline
  reset/replay resistance is not complete and depends on the trial-policy
  decision in `docs/ROADMAP.md`.
- License service: isolated ASP.NET Core service under `services/license-service`
  with Stripe webhook signature verification, replay-safe event handling,
  license issuance/revocation, activation endpoint, device limit, entitlement
  signing, trust-anchor endpoint, authenticated non-cacheable admin health
  page, alert seams, and optional live Stripe reconciliation source. Public
  `/health` is minimal; admin metrics fail closed without the configured
  header token; webhook responses expose no license key. Production key
  delivery, network auth, keys/DNS/Stripe, legal, and support remain external
  gates.
- Update-check infrastructure: SemVer comparison, manifest model, optional
  Ed25519 signed manifest verification, and null source by default. Full
  auto-update is not built.
- Opt-in local crash reports: redacted JSON files under
  `%LOCALAPPDATA%\Octadock\CrashReports`; no uploader or telemetry transport.
- Release packaging: `build/release.ps1` validates version/tag/source state,
  builds/tests Release, publishes self-contained single-file win-x64 app and
  CLI outputs, executes the public-artifact-boundary gate with machine-readable
  evidence, creates a versioned zip and manifest, then writes and verifies
  SHA256SUMS. `.github/workflows/release.yml` repeats that strict path for an
  exact existing `v<version>` tag and uploads the package/evidence as an
  Actions artifact; it does not sign or publicly publish the build.
- Static paid-beta website/legal surfaces under `web/` with honest placeholder
  download/checkout links. Hosting, live DNS, legal review, and signed build
  URL are not complete.

## Partial Or Needs Verification

- The design-system pass is implemented and the static WPF gate is clean, but
  rendered acceptance is not complete. All repositioned surfaces still need
  fixed-viewport comparison, keyboard/focus, high-contrast/
  reduced-transparency, screen-reader, and mixed-DPI checks before release.
- Context is a usable primary workflow and now feeds Use with AI directly.
  Notes/reorder and exact include/export controls are implemented; rendered
  launch-scope acceptance remains. MCP is deferred and is not a beta
  completion requirement.
- Clipboard history defaults off for fresh settings. First run persists an
  explicit enable/decline choice, the listener does not start before
  enablement, Settings can pause monitoring, and Clipboard History can clear
  retained data. Existing persisted choices are preserved; live WPF/Win32
  verification remains.
- The reviewed handoff is source-bound across Shelf, Context, History,
  Clipboard, tray, and automation compatibility aliases. Automated fail-closed
  reset, exact-source, and temporary-lease paths pass. Contextual
  owner/topmost logic is implemented; real owner/focus/close/rendered
  positioning and mixed-DPI acceptance remains.
- Beta recording now encodes optional microphone and system/app audio tracks,
  but real hardware/audio-device acceptance is pending, so it stays labeled
  Beta. Camera overlay, click/keystroke visualization, GIF, and trim/compress
  are not built.
- Beta scrolling capture supports manual vertical movement only. It needs
  adversarial live testing on apps/pages that scroll in both directions, use
  sticky overlays, or have sparse dark content.
- OCR history rows exist for region OCR grabs; file-based OCR still
  copies/notifies without a history row.
- Dictation has a deterministic 74-row matrix and a versioned SAPI-corpus
  benchmark baseline, but still needs live quality testing across microphones,
  accents, languages, model-download states, and Windows privacy/device
  failures. A measured limitation: segment-wise streaming WER exceeds batch
  WER on some categories (paths 0.50 vs 0.26, numbers 0.71 vs 0.57, French
  0.67 vs 0.48) from decode context loss; documented as future work. Windows
  Speech fallback is not shipped.
- Read aloud works; ambient screen discovery is not part of the paid-beta
  scope.
- Explorer file associations for preview/image types are actively
  unregistered at startup as upgrade cleanup; no association is registered
  anymore.
- The authenticated admin/ops panel is a minimal launch-health surface, not a
  full-statistics support console. Production network authentication remains
  external.
- Release packaging is a zip, not an installer. Code signing, SmartScreen
  reputation, MSIX/installer decisions, auto-update host, update signing key,
  clean-VM verification, and public beta distribution are still open.

## Not Implemented Yet

- MCP, optional local-model destinations, and a reusable task/profile library.
  These are deferred and require their own permission, refresh, and retention
  design.
- Command palette/fuzzy launcher, code screenshot beautifier, color picker,
  scratchpad notes, and remaining developer mini-tools.
- Rich universal file handling for PDF, Office, archives, design files, and
  writeback-safe annotations for non-image documents.
- Upload/share plugins, share links, password-protected links, team/cloud
  admin, first-party accounts, and self-service device management.
- Full production commercial launch: live Stripe config, production KMS
  signing key, support mailbox readiness, legal review, Cloudflare/DNS
  hosting, signed installer, and real payment rehearsals.

## Removed From Product Direction

- Background AI/session discovery, `run`/`watch`/hook tracking, passive
  overlays, and their database tables were removed on 2026-07-05. Schema
  migration 6 drops the old tables. They are not launch features or future
  commitments.
- Generic file preview (preview card window/host/inspector/recovery UI and the
  CSV/TSV/JSON/Markdown/log/text/metadata providers), the generic local-image
  surface, floating pins (window/service, quick pen, persisted restore,
  Gather, inline pin AI edit, pin post-capture action), the all-in-one capture
  HUD, tray Pause/Resume, the "Open a File" tray action, arbitrary-file Shelf
  drops, and Explorer "Open with Octadock" preview associations plus image
  shell verbs were removed on 2026-07-23 by owner decision to focus the
  product on capture, dictation, Context, and reviewed handoff. The legacy
  per-user associations are actively unregistered at startup as upgrade
  cleanup; the matching CLI/protocol verbs (`pin`, `open`, `open-annotate`,
  `open-from-clipboard`, `add-shelf-item`, `all-in-one`) remain as truthful
  tombstones that fail non-zero; `pins`/preview data is left in place, inert.

## Main Risks

- Visual quality cannot be accepted by compile/tests alone. The implemented
  design-system pass still needs before/after screenshots at fixed sizes
  against the reference board plus accessibility acceptance.
- Multi-monitor/DPI behavior must be verified after every overlay, dock,
  shelf, pill, or Context positioning change.
- Context/AI features touch files, captures, clipboard, prompts, logs, and
  secrets. Redaction, explicit opt-in, and clear export boundaries are
  required.
- Recording audio is built but not hardware-accepted. Marketing and in-app
  copy must keep recording labeled Beta; the copy-honesty gate enforces the
  wording.
- Protocol, automation tombstones, external file opens, and cloud AI/STT
  flows are security-sensitive; keep dispatch gates and the named-opt-in
  cloud boundary in the gate.

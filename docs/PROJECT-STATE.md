# Octadock Project State

Last audited: 2026-07-17

This file is the current source of truth for what Octadock actually does today.
Older specs, wargames, proposals, and launch plans are product history; trust
this file and `docs/CAPABILITIES.md` first when docs disagree with code.

Current integrated alpha version: `0.2.0-alpha.0`. Version metadata lives in
`version.json`, `build/version.props`, and `docs/VERSIONING.md`.

## Current Product Shape

Octadock is a local-first Windows capture-to-context workspace. Its primary
launch workflow is:

`capture / pin / Context / History / Clipboard -> choose outcome -> add missing intent -> review -> finish`

Capture, cursor dictation, Context, and explicit reviewed handoff are the four primary
pillars. Clipboard history, local text tools, read aloud, file preview,
annotation, pins, and automation remain useful secondary capabilities. Manual
vertical scrolling capture and MP4 video-only screen recording are explicitly
Beta. Octadock does not watch the screen or discover model/tool activity in the
background.

## Verification Snapshot

Most recently recorded clean baseline, observed on 2026-07-17 from `main` at
`a60c779`:

- Full Release gate: `.\build\build.ps1 -Configuration Release` passed.
- Desktop solution: 1,010/1,010 tests passed (Core 609, App 251, Data 82,
  Platform.Windows 47, CLI 21).
- License service: 65/65 Release tests passed.
- Internal Workflow Intelligence suite: 27/27 Release tests passed; it remains
  excluded from public artifacts by the public-boundary gate.
- Version synchronization, public-artifact boundary, copy-honesty, and 13
  production website JavaScript module syntax checks passed.
- GitHub Actions is not yet a valid remote signal: recent runs fail at workflow
  startup before jobs begin. Current blockers and gates are tracked only in
  `docs/ROADMAP.md`.
- Test projects in the desktop solution: Core, Data, CLI, Platform.Windows, and
  App. The license service has its own isolated solution and test project.

Recovery-candidate evidence from `codex/recovery-2026-07-17` on the same date:

- Full Release build and self-contained single-file App/CLI publish passed.
- Desktop solution: 1,111/1,111 tests passed (Core 685, App 276, Data 82,
  Platform.Windows 47, CLI 21).
- License service: 75/75 tests passed; internal Workflow Intelligence: 27/27.
- Version synchronization, copy-honesty, public-artifact boundary, and website
  syntax gates passed. The build reports 615 existing analyzer warnings.

The automated suite is strong for parsers, persistence, services, gates, and
headless view models. It does not prove pixel-level WPF quality, mixed-DPI
overlay placement, scrolling capture reliability on real apps, microphone/device
behavior, or clean-VM launch readiness.

## Built And Wired

- Windows tray utility, single-instance guard, first-run flow, settings window,
  startup/protocol controls, and a permanent Dock capsule.
- Global hotkeys for area, window, fullscreen, previous-area, all-in-one HUD,
  OCR, recording, dictation, read aloud, and clipboard history.
- Area capture with per-monitor selection overlays, live dimensions, magnifier,
  previous-region recall, self-timer countdown pill, DPI-aware geometry, and
  Octadock window exclusion where Windows supports it.
- Window and fullscreen capture through the Windows platform layer, including a
  per-monitor window picker and WGC/GDI fallback behavior.
- Capture Shelf with compact image-first rows, keyboard/hover actions,
  click-to-open image surface, durable discard/restore, drag/drop, configurable
  anchor/density/auto-close, and history integration. Video shelf cards exist
  for Beta recordings.
- Local SQLite history for captures, actions, pins, settings, clipboard clips,
  thumbnails, soft delete/restore, retention cleanup, and Context records.
- Clipboard history end to end: `WM_CLIPBOARDUPDATE` monitor, password-manager
  exclusion formats, own-write suppression, dedupe/seen counts, image storage,
  favorites, search/filter UI, copy/delete/clear, cap-based trimming, hotkey,
  Dock/tray entry points, and `octadock open-clipboard-history`.
- Text-transform toolbox with 28 local transforms: JSON format/minify, Base64,
  URL, HTML, JWT decode, identifier casing, MD5/SHA hashes, Unix timestamp
  conversion, line sort/dedupe/trim/count, live output, and chaining.
- OCR through `Windows.Media.Ocr` with region/file entry points and compact,
  lines, and layout modes. Region OCR persists an `OcrSource` capture plus an
  `OcrExtracted` action so History can recover the text.
- Annotation editor with crop, select/move, arrow, rectangle, ellipse, line,
  text, highlighter, blur, pixelate, counter, freehand, undo/redo, export, copy,
  save, drag handle, and `.octadock` project packages.
- Image surface / pin window is now the default image opener. It supports
  move/resize, opacity, pin/unpin topmost, position lock, quick pen annotations,
  clear ink, copy, save, save-behavior prompt, advanced annotation editor,
  reveal/open source, add to Context, keyboard nudging, persisted pins, gather,
  hide, and close behavior.
- File preview first slice: images route to the image surface; CSV/TSV, JSON,
  log, Markdown, and broad text/code/config files open in the preview card;
  unsupported files use a file-info fallback. The service rejects UNC paths and
  prompts before opening executable/script-like and known shell-active files,
  including Windows Internet shortcuts and app packages, externally.
- Explorer integration: startup registers per-user "Open with Octadock" for
  previewable text, CSV, code, and image file types. Images also have an
  "Add to Octadock dock" verb routed through `add-shelf-item`.
- Automation through `octadock.exe` and `octadock://`, using the same Core parser
  and per-user named-pipe forwarding to the running tray instance. Settings gate
  CLI/protocol dispatch before parsing. Replacing a distinct existing entitlement
  through an activation command requires a visible, default-No identity comparison.
- `open-context` / `context-stack` command opens the Context window. The legacy
  command alias remains for automation compatibility; the product name is
  Context.
- Context: persistent named packages, snapshot/reference ownership, verified
  large-file references, add files/captures/images, package navigation, per-item
  include/exclude review, export preview, open/delete, and safe folder/zip export.
  Manifests use relative paths without absolute path leaks. Create/add is
  license-gated; viewing/exporting existing packages remains available after
  expiry.
- **Beta — manual scrolling capture:** visible session pill and stitched output
  for manual vertical movement. Horizontal and auto-scroll modes are rejected;
  motion/overlap checks and memory limits reduce invalid output but do not imply
  universal app/page support.
- **Beta — screen recording:** active-monitor and command/tray/HUD selected-region
  MP4 video, with countdown, pill, stop flow, history/shelf integration, managed
  storage, failed-frame-pump recovery, and incomplete-output cleanup. No
  microphone or system-audio track is encoded.
- Theme/chrome work: semantic dark/light/high-contrast tokens, native title-bar
  behavior, Windows 11 rounded corners, reduced-transparency fallbacks, themed
  controls, Lucide icons, and a cohesive opaque-window/glass-floating-surface
  system. The Dock, HUD, Shelf, Context, Settings, History, and first-run flow
  have received the current design pass.
- Speech-to-text: Dock dictate action, WASAPI microphone capture, Parakeet TDT
  0.6B v3 default engine via sherpa-onnx, local Whisper fallback, explicit
  `OCTADOCK_OPENAI_API_KEY` opt-in OpenAI provider, auto-language routing,
  model download consent/manager, code-term dictionary, configurable insertion,
  configurable shortcut, CLI toggle, live partials via embedded Silero VAD,
  auto-stop option, discard button, toggle/hold/both activation modes, and
  paste-at-cursor with clipboard restore. Model preparation/warmup is cancellable,
  microphone cleanup is guaranteed on cancellation/failure, and partial text is
  preserved for recovery. `octadock://dictation` is blocked.
- Read aloud v2: verbatim by default through built-in Windows voices, sentence
  chunking/prefetch, playback pill, pause/resume/stop, `Ctrl+Shift+0`, provider
  voice/rate settings, and optional ElevenLabs TTS. `--explain` now opens the
  Use with AI with an editable investigation goal, and its result can be read aloud.
- Use with AI: an outcome-first surface entered from the current Shelf capture,
  pin, included Context items, selected History/Clipboard row, Dock, or dictated
  intent. Build, Investigate, Verify, Extract, and Handoff seed concrete goals
  and acceptance criteria before the advanced packet review. The review turns recent captures, Context packages, clipboard
  text/images, local files, annotations, and local OCR into a deterministic
  `TASK.md` plus `manifest.json` and an exported `SHA256SUMS`. Every asset has a safe relative path and SHA-256;
  changed Context references and evidence changed after review fail closed.
  Text secret redaction defaults on, including namespaced/bracketed assignments and
  complete quoted values; shared line-ending normalization keeps fake source
  delimiters inside indented untrusted data. The UI explicitly warns that attached
  screenshot pixels remain unchanged. Before/after verification adds both images,
  a deterministic heat map, changed-pixel ratio, mean/max delta, and change bounds.
  A destination-named confirmation gates a read-only Codex/Claude CLI handoff:
  Codex is ephemeral with shell/exec disabled and explicit image attachments;
  Claude receives only packet-relative Read/Glob access. There is no provider fallback, write-enabled
  execution, API-key storage, or prompt/session history. Normal temporary packets
  are deleted immediately; link-skipping retention scavenges crash leftovers.
  Results remain ephemeral unless copied. The stable `ai` command, historical
  aliases, `ShowAiActions` presenter seam, and legacy action services remain as
  backward-compatible adapters into the new workspace.
- Licensing/trial spine: 14-day local trial, monotonic high-water trial clock,
  signed entitlement file outside `octadock.db`, client Ed25519 verification,
  machine hash, Settings > Account & Billing activation UI, `activate` command,
  `octadock://activate`, ambient tray/dock trial status, and service-seam gates
  for paid features after expiry. Strong offline reset/replay resistance is not
  complete and depends on the trial-policy decision in `docs/ROADMAP.md`.
- License service: isolated ASP.NET Core service under `services/license-service`
  with Stripe webhook signature verification, replay-safe event handling,
  license issuance/revocation, activation endpoint, device limit, entitlement
  signing, trust-anchor endpoint, authenticated non-cacheable admin health page,
  alert seams, and optional live Stripe reconciliation source. Public `/health`
  is minimal; admin metrics fail closed without the configured header token;
  webhook responses expose no license key. Production key delivery, network auth,
  keys/DNS/Stripe, legal, and support remain external gates.
- Update-check infrastructure: SemVer comparison, manifest model, optional
  Ed25519 signed manifest verification, and null source by default. Full
  auto-update is not built.
- Opt-in local crash reports: redacted JSON files under
  `%LOCALAPPDATA%\Octadock\CrashReports`; no uploader or telemetry transport.
- Release packaging: `build/release.ps1` validates version/tag/source state,
  builds/tests Release, publishes self-contained single-file win-x64 app and CLI
  outputs, executes the public-artifact-boundary gate with machine-readable
  evidence, creates a versioned zip and manifest, then writes and verifies
  SHA256SUMS. `.github/workflows/release.yml` repeats that strict path for an
  exact existing `v<version>` tag and uploads the package/evidence as an Actions
  artifact; it does not sign or publicly publish the build.
- Static paid-beta website/legal surfaces under `web/` with honest placeholder
  download/checkout links. Hosting, live DNS, legal review, and signed build URL
  are not complete.

## Partial Or Needs Verification

- The current design-system pass is implemented, but rendered acceptance is not
  complete. Primary surfaces still need fixed-viewport comparison, keyboard/focus,
  high-contrast/reduced-transparency, and mixed-DPI checks before release.
- Context is a usable primary workflow and now feeds Use with AI directly.
  Launch polish includes notes/reorder and per-derivative export controls; MCP is
  deferred and is not a beta completion requirement.
- Clipboard history defaults off for fresh settings. First run persists an
  explicit enable/decline choice, the listener does not start before enablement,
  Settings can pause monitoring, and Clipboard History can clear retained data.
  Existing persisted choices are preserved; live WPF/Win32 verification remains.
- The reviewed handoff engine is implemented, but its standalone workspace
  positioning still conflicts with the intended source-bound information architecture.
- File preview is useful but not universal. Rich PDF, Office, archives/zip,
  design files, syntax-highlighted code, and non-image annotation/writeback are
  not built. Unsupported files fall back to a metadata card and external open.
- Image quick-pen exists in the image surface, but the full advanced editor is
  still a separate flow. Save/thumbnail refresh is wired for the main stack/shelf
  paths, but this needs visual regression testing.
- Beta recording is intentionally MP4 video only. It has no microphone/system
  audio, camera overlay, click/keystroke visualization, GIF, trim/compress, or
  verified crash/shutdown/disk-pressure recovery.
- Beta scrolling capture supports manual vertical movement only. It needs
  adversarial live testing on apps/pages that scroll in both directions, use
  sticky overlays, or have sparse dark content.
- OCR history rows exist for region OCR grabs; file-based OCR still copies/notifies
  without a history row.
- Dictation is much further along than the original alpha, but still needs live
  quality testing across microphones, accents, languages, model-download states,
  and Windows privacy/device failures. Windows Speech fallback is not shipped.
- Read aloud works; ambient screen discovery is not part of the paid-beta scope.
- File associations are always registered for supported types; there is no
  settings UI to enable/disable them or claim every file type.
- The authenticated admin/ops panel is a minimal launch-health surface, not a
  full-statistics support console. Production network authentication remains external.
- Release packaging is a zip, not an installer. Code signing, SmartScreen
  reputation, MSIX/installer decisions, auto-update host, update signing key,
  clean-VM verification, and public beta distribution are still open.

## Not Implemented Yet

- MCP, optional local-model destinations, and a reusable task/profile library.
  These are deferred and require their own permission, refresh, and retention design.
- Command palette/fuzzy launcher, code screenshot beautifier, color picker,
  scratchpad notes, and remaining developer mini-tools.
- Rich universal file handling for PDF, Office, archives, design files, and
  writeback-safe annotations for non-image documents.
- Upload/share plugins, share links, password-protected links, team/cloud admin,
  first-party accounts, and self-service device management.
- Full production commercial launch: live Stripe config, production KMS signing
  key, support mailbox readiness, legal review, Cloudflare/DNS hosting, signed
  installer, and real payment rehearsals.

## Removed From Product Direction

- Background AI/session discovery, `run`/`watch`/hook tracking, passive overlays,
  and their database tables were removed on 2026-07-05. Schema migration 6 drops
  the old tables. They are not launch features or future commitments.

## Main Risks

- Visual quality cannot be accepted by compile/tests alone. The implemented UI
  pass still needs before/after screenshots at fixed sizes against the reference
  board plus accessibility acceptance.
- Multi-monitor/DPI behavior must be verified after every overlay, dock, shelf,
  pill, Context, preview, or pin positioning change.
- Context/AI features touch files, captures, clipboard, prompts, logs, and
  secrets. Redaction, explicit opt-in, and clear export boundaries are required.
- Recording audio and rich file previews are deliberately deferred technical
  surfaces. Marketing and in-app copy must keep recording labeled Beta and video only.
- Protocol, file associations, external file opens, and cloud AI/STT flows are
  security-sensitive; keep UNC/executable/cloud-send protections in the gate.

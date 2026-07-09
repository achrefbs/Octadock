# Octadock Project State

Last audited: 2026-07-09

This file is the current source of truth for what Octadock actually does today.
Older specs, wargames, proposals, and launch plans are product history; trust
this file and `docs/CAPABILITIES.md` first when docs disagree with code.

Current integrated alpha version: `0.2.0-alpha.0`. Version metadata lives in
`version.json`, `build/version.props`, and `docs/VERSIONING.md`.

## Verification Snapshot

Observed on 2026-07-09 after commit `25dd2db`:

- Desktop solution: `dotnet test .\Octadock.sln -c Debug` passed 773/773 tests.
- License service: `dotnet test .\services\license-service\Octadock.LicenseService.sln -c Debug --no-build`
  passed 65/65 tests.
- Test projects in the desktop solution: Core, Data, CLI, Platform.Windows, and
  App. The license service has its own isolated solution and test project.

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
- Capture Shelf with image-only cards at rest, hover actions, click-to-open image
  surface, restore-recently-closed, drag/drop, configurable anchor/size/auto-close,
  and history integration. Video shelf cards exist for recordings.
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
  prompts before opening executable/script-like files externally.
- Explorer integration: startup registers per-user "Open with Octadock" for
  previewable text, CSV, code, and image file types. Images also have an
  "Add to Octadock dock" verb routed through `add-shelf-item`.
- Automation through `octadock.exe` and `octadock://`, using the same Core parser
  and per-user named-pipe forwarding to the running tray instance. Settings gate
  CLI/protocol dispatch before parsing.
- `open-context` / `context-stack` command opens the Context Stack window.
- Context Stack first slice: persistent Context packages, snapshot/reference
  ownership, large-file reference policy, add files, add captures/images, package
  navigation, open item through the preview service, delete, zip export, folder
  export, manifest generation with relative paths, no absolute path leaks, and
  license-gated create/add. Viewing/exporting existing Context packages remains
  available after expiry.
- Manual vertical scrolling capture with a visible scrolling-session pill and a
  stitched output. The implementation rejects horizontal and auto-scroll modes.
- Screen recording to MP4 for active monitor and command/tray/HUD selected
  regions, with countdown, recording pill, stop flow, history entry, shelf video
  card, managed default storage, and cleanup of incomplete MP4s on start/stop
  failure. Recording is video-only.
- Theme/chrome work: dark/light title bars, Windows 11 rounded corners,
  themed scrollbars/context menus/tooltips/sliders/radio/progress controls, and
  first-pass glass styling for the Dock, shelf, preview card, pins, settings,
  and Context Stack.
- Speech-to-text: Dock dictate action, WASAPI microphone capture, Parakeet TDT
  0.6B v3 default engine via sherpa-onnx, local Whisper fallback, explicit
  `OCTADOCK_OPENAI_API_KEY` opt-in OpenAI provider, auto-language routing,
  model download consent/manager, code-term dictionary, configurable insertion,
  configurable shortcut, CLI toggle, live partials via embedded Silero VAD,
  auto-stop option, discard button, toggle/hold/both activation modes, and
  paste-at-cursor with clipboard restore. `octadock://dictation` is blocked.
- Read aloud v2: verbatim by default through built-in Windows voices, sentence
  chunking/prefetch, playback pill, pause/resume/stop, `Ctrl+Shift+0`, provider
  voice/rate settings, optional ElevenLabs TTS, and optional `--explain` via the
  user's Codex/Claude CLI before TTS.
- Licensing/trial spine: 14-day local trial, monotonic high-water trial clock,
  signed entitlement file outside `octadock.db`, client Ed25519 verification,
  machine hash, Settings > Account & Billing activation UI, `activate` command,
  `octadock://activate`, ambient tray/dock trial status, and service-seam gates
  for paid features after expiry.
- License service: isolated ASP.NET Core service under `services/license-service`
  with Stripe webhook signature verification, replay-safe event handling,
  license issuance/revocation, activation endpoint, device limit, entitlement
  signing, trust-anchor endpoint, admin health page, alert seams, and optional
  live Stripe reconciliation source. Production keys/DNS/Stripe/legal remain
  external gates.
- Update-check infrastructure: SemVer comparison, manifest model, optional
  Ed25519 signed manifest verification, and null source by default. Full
  auto-update is not built.
- Opt-in local crash reports: redacted JSON files under
  `%LOCALAPPDATA%\Octadock\CrashReports`; no uploader or telemetry transport.
- Release packaging script: `build/release.ps1` validates version metadata,
  builds/tests Release, publishes self-contained single-file win-x64 app and CLI
  outputs, creates a versioned zip, manifest, and SHA256SUMS.
- Static paid-beta website/legal surfaces under `web/` with honest placeholder
  download/checkout links. Hosting, live DNS, legal review, and signed build URL
  are not complete.

## Partial Or Needs Verification

- Visual design is not final. A shared direction exists, but WPF surfaces are not
  fully token-enforced or pixel-matched to the user's concept board. Settings,
  Context Stack, file preview, shelf, history, and some menus still need a real
  design-system pass plus screenshot gates.
- Context Stack is a real first slice, not the final Context product. Missing:
  per-item include/exclude controls in the UI, notes/reorder polish, redaction,
  clipboard/prompt/log/OCR source integrations, "Add to Context" from every
  surface, export preview, MCP exposure, Ask AI, and AI-provider workflows.
- File preview is useful but not universal. Rich PDF, Office, archives/zip,
  design files, syntax-highlighted code, and non-image annotation/writeback are
  not built. Unsupported files fall back to a metadata card and external open.
- Image quick-pen exists in the image surface, but the full advanced editor is
  still a separate flow. Save/thumbnail refresh is wired for the main stack/shelf
  paths, but this needs visual regression testing.
- Recording is not PRD-complete. It has selected-region video, but no microphone
  audio, system audio, camera overlay, click/keystroke visualization, GIF,
  trim/compress, disk-full classification, or crash/shutdown recovery.
- Scrolling capture is fragile by nature and only manual vertical mode is
  supported. It needs adversarial live testing on apps/pages that scroll in both
  directions, use sticky overlays, or have sparse dark content.
- OCR history rows exist for region OCR grabs; file-based OCR still copies/notifies
  without a history row.
- Dictation is much further along than the original alpha, but still needs live
  quality testing across microphones, accents, languages, model-download states,
  and Windows privacy/device failures. Windows Speech fallback is not shipped.
- Read aloud works, but the "screen discovery" overlay for picking explain/read
  targets is still roadmap work.
- File associations are always registered for supported types; there is no
  settings UI to enable/disable them or claim every file type.
- The admin/ops panel is a minimal launch-health surface, not the future
  full-statistics admin panel the product wants.
- Release packaging is a zip, not an installer. Code signing, SmartScreen
  reputation, MSIX/installer decisions, auto-update host, update signing key,
  clean-VM verification, and public beta distribution are still open.

## Not Implemented Yet

- MCP server for exposing Octadock context to Cursor, Claude Code, Codex, and
  other tools.
- Ask AI / Send to AI, local Ollama, hosted model-provider settings, redaction
  pipeline, AI cost/session analytics, and prompt/snippet library.
- Command palette/fuzzy launcher, code screenshot beautifier, color picker,
  scratchpad notes, and remaining developer mini-tools.
- Rich universal file handling for PDF, Office, archives, design files, and
  writeback-safe annotations for non-image documents.
- Upload/share plugins, share links, password-protected links, team/cloud admin,
  first-party accounts, and self-service device management.
- Full production commercial launch: live Stripe config, production KMS signing
  key, support mailbox readiness, legal review, Cloudflare/DNS hosting, signed
  installer, and real payment rehearsals.

## Removed

- **Active AI Sessions / Agent Mission Control** was removed on 2026-07-05:
  auto-discovery, `run`/`watch`/hook CLI tracking, the AI Sessions window,
  passive overlay, and database tables are gone. Schema migration 6 drops the
  tables. The product refocused on capture, voice, files, Context, and design.

## Main Risks

- Visual quality cannot be accepted by compile/tests alone. The next UI pass
  needs before/after screenshots at fixed sizes against the reference board.
- Multi-monitor/DPI behavior must be verified after every overlay, dock, shelf,
  pill, Context Stack, preview, or pin positioning change.
- Context/AI features touch files, captures, clipboard, prompts, logs, and
  secrets. Redaction, explicit opt-in, and clear export boundaries are required.
- Recording audio and rich file previews are still large technical surfaces and
  should not be implied in marketing or in-app copy.
- Protocol, file associations, external file opens, and cloud AI/STT flows are
  security-sensitive; keep UNC/executable/cloud-send protections in the gate.

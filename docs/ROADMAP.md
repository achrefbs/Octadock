# Octadock Roadmap And Implementation Plan

Last updated: 2026-07-09

Status: living plan. For the current audited inventory, read
`docs/PROJECT-STATE.md` and `docs/CAPABILITIES.md` first.

The priority is no longer "build any missing slice once." The app has many real
parts now. The work is to make the visible product feel coherent, harden the
minute-to-minute workflows, and avoid selling or documenting features that are
still partial.

## Current Checkpoint

Built and wired:

- capture/shelf/history/annotation/pins;
- image surface as the default image opener, with quick pen and advanced editor;
- file preview first slice;
- Context Stack first slice;
- dictation with Parakeet, Whisper fallback, live partials, and explicit
  `OCTADOCK_OPENAI_API_KEY` OpenAI opt-in;
- read-aloud v2;
- clipboard history and text tools;
- trial/license gate plus isolated license service;
- self-contained release zip script.

Still not ready:

- final design system and pixel-quality UI;
- universal file preview/editing;
- final Context product with redaction, source integrations, AI, and MCP;
- recording audio/advanced recorder;
- robust scrolling capture;
- installer/signing/auto-update/live launch operations.

## Phase 0: Truth, Hygiene, And Gates

Goal: keep the repo honest so no agent builds from a stale map.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Source-of-truth docs | Keep `PROJECT-STATE`, `CAPABILITIES`, README, Testing, and backlog current after every major pass. | A new agent can answer built/partial/planned without reading old proposals. |
| CLI/help drift | Keep CLI help, automation docs, and parser behavior aligned. | `octadock --help` and `docs/AUTOMATION.md` describe real behavior. |
| Copy honesty | Avoid "fully offline" or "local AI" where model downloads or cloud CLIs may be involved. | Grep gates stay clean and docs name network-capable paths. |
| Visual acceptance gate | Require before/after screenshots for UI work, not just compile/tests. | UI changes include fixed-size screenshots against the concept board. |

## Phase 1: Design System And Daily Surfaces

Goal: make Octadock feel like one compact premium Windows tool, not separate
screens from different passes.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Shared WPF design layer | Centralize tokens, glass surfaces, spacing, radii, icon buttons, toolbars, menus, hover overlays, and typography. | Dock, shelf, Context Stack, image surface, preview, settings, and history stop hand-rolling visual rules. |
| Capture Shelf | Keep image-first cards, hover actions, clean click-to-open behavior, and minimal card chrome. | At rest the shelf is mostly the screenshot; actions appear cleanly on hover. |
| Image surface | Treat it as the image viewer and pin surface. Refine top/bottom action placement, quick pen, save behavior, source reveal, and unpinned default. | Opening any supported image lands in the same polished surface and never duplicates the old preview screen. |
| Context Stack | Make it compact enough to float without covering work; preserve navigation between packages; keep folder export primary. | User can keep Context visible while working and immediately understand package/item state. |
| Settings/history/preview | Migrate the ugliest remaining pages to the same primitives. | No surface looks like legacy WPF or a different app. |

## Phase 2: File Preview And Editing

Goal: make Octadock a fast, safe local file inspection layer.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Existing providers | Keep image, CSV/TSV, JSON, log, Markdown, broad text/code/config, and fallback stable. | `octadock open --filepath` works predictably for supported files. |
| Rich previews | Add PDF, Office, archives/zip, and selected design-file strategies after library/licensing review. | Each supported type has a useful preview, clear limits, and safe external open fallback. |
| Archive safety | Inspect zip contents without unsafe extraction; add size/path traversal guards. | Archives cannot write outside a temp sandbox or surprise-open executables. |
| Non-image annotation/writeback | Decide sidecar vs direct write per file type. Direct overwrite must use SafeFileWriter and explicit save choice. | User knows whether editing replaces the original or creates a new file. |
| File associations | Add settings control and broader "Open with Octadock" coverage without overpromising rich preview. | Explorer verbs are useful but reversible. |

## Phase 3: Context Product

Goal: turn the built Context Stack into the real working context system.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Package controls | Add item include/exclude, reorder, notes, source badges, package rename, and export preview. | Export contains exactly what the UI shows. |
| Source integrations | Add from shelf, history, image surface, file preview, clipboard, OCR rows, prompts, and Explorer verbs. | A user can build context without hunting for a special entry point. |
| Export formats | Keep normal folder export as primary; keep zip; add Markdown index and later image-collage export if useful. | Export is understandable outside Octadock. |
| Redaction/privacy | Add local secret detection and redaction before any cloud/AI path. | Secrets are not accidentally included in AI-bound packages. |
| AI/MCP | Add local-first Ask AI, optional hosted providers, send-to-tool paths, and MCP after redaction and consent are real. | AI workflows are opt-in, auditable, and can be disabled. |

## Phase 4: Capture, Scroll, And Recording Reliability

Goal: harden the workflows people will judge immediately.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Mixed-DPI overlays | Keep area/window overlays, shelf, pills, Context Stack, and pins correct on real monitor layouts. | Manual matrix passes on 100/150/200 percent and mixed-DPI setups. |
| Scrolling capture | Rework match logic and test top-to-bottom and bottom-to-top paths with sticky overlays and sparse pages. | Long captures produce ordered, uncropped images on common browsers/docs. |
| Recording MVP | Keep selected-area video stable and honest. | Recording starts/stops reliably and failed outputs do not linger. |
| Advanced recorder | Add mic/system audio, cursor, click/key overlays, GIF, camera, trim/compress only after the video core is solid. | Bug-report videos can be shared without another editor. |

## Phase 5: Voice And Read

Goal: make voice features feel dependable rather than experimental.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Dictation QA | Test microphones, privacy failures, model downloads, long dictations, hold-to-talk, and multilingual routing. | User-visible errors are clear and stop-to-text is consistently fast. |
| Provider strategy | Keep Parakeet default, Whisper fallback, explicit OpenAI opt-in. Add Windows Speech fallback if it proves useful. | User can choose privacy/speed/accuracy without hidden network calls. |
| Read aloud | Keep verbatim default fast; refine `--explain` consent/copy and target selection. | Reading a region/file/clipboard is predictable and stoppable. |
| Screen discovery | Build the overlay for choosing read/explain targets. | User can select a screen region for read/explain without confusion. |

## Phase 6: Commercial, Admin, And Distribution

Goal: make a paid beta installable, supportable, and honest.

| Feature | Work | Acceptance |
| --- | --- | --- |
| Installer/signing | Decide MSIX vs installer, code sign, build SmartScreen reputation, clean-VM verify. | A fresh Windows 10/11 machine can install/run without developer tools. |
| Update path | Host signed manifest, add UI, and decide manual update vs auto-update. | Update check never downgrades and never trusts unsigned manifests when signing is configured. |
| Stripe/license ops | Wire live Stripe key/webhook secret, production KMS signing key, tax posture, support email, and legal pages. | Money -> key -> activation works in live rehearsals. |
| Admin panel | Expand beyond launch health into license/search/audit/refund/reconciliation/support stats. | Founder can answer sales/support/activation questions without database spelunking. |
| Website/download | Publish DNS, signed build URL, SHA-256, pricing/checkout, and support docs. | Public pages match the shipped app and do not sell unbuilt features. |

## Planning Rule

Every feature should enter through the same spine:

`hotkey/tray/dock/protocol/CLI -> command parser -> service -> UI surface -> history/settings/tests`

For visual work, add:

`design token -> shared control/style -> screenshot gate -> manual acceptance`

That keeps Octadock from becoming a pile of mini apps and makes future Context,
MCP, and AI exposure much easier.

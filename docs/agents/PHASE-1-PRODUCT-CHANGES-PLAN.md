# Phase 1 product changes — implementation plan (2026-07-23)

Baseline: `main` at `1706fac`, clean tree. Scope: approved Phase 1 product
changes only (no Gate E). Authority: owner task brief of 2026-07-23, which
overrides the preview freeze and earlier Pin decisions.

## Work streams and sequencing

| Wave | Stream | Scope ownership (disjoint file sets) |
| --- | --- | --- |
| 1a | Preview/Pin removal + annotation routing | `src/Octadock.Core/Commands`, `src/Octadock.Core/Services/*Preview*`, `src/Octadock.Cli`, `src/Octadock.App/Preview`, `src/Octadock.App/Pins`, `CommandDispatcher`, DI, Tray/Dock/Shelf/History/Context preview-pin entry points, `Editing/AnnotationService`, `Platform.Windows/System/FileAssociationRegistration.cs`, related tests |
| 1b | STT platform/Core hardening + benchmark | `src/Octadock.Platform.Windows/Stt`, `Audio`, `src/Octadock.Core/Speech`, STT benchmark tooling, related tests |
| 1c | Recorder completion (audio + recovery) | `src/Octadock.Platform.Windows/Recording`, `src/Octadock.Core/Recording`, `App/Services/RecordingController.cs`, `CaptureUx/RecordingPill.cs`, related tests |
| 2a | Shell simplification | Dock split Capture button + More menu, tray streamline, HUD removal, Pause/Resume removal, shortcut defaults, launch-at-login default + first-run opt-out |
| 2b | STT app-side UX + settings | Dictation states/UX, insertion/clipboard honesty, review-before-insert/undo, Dictation settings Basic/Advanced, model consent detail |
| 2c | Capture settings + Shelf delete safety | Precision aids setting, Basic/Advanced capture settings, Shelf × dismiss vs confirmed permanent delete |
| 3 | Design-system coherence pass | Tokens/styles consolidation across all remaining surfaces; rendered evidence |
| 4 | Docs + gates + acceptance evidence | PROJECT-STATE, CAPABILITIES, ARCHITECTURE, ROADMAP, AUTOMATION, UI-INVENTORY, first-run/help copy, PRODUCT-REVIEW-MATRIX; full Release gate; negative/positive proofs |

Waves run sequentially; streams inside a wave run in parallel subagents with
disjoint file ownership and private build artifact dirs. Commits are created
per stream after each wave by the coordinator (agents do not run git).

## Binding decisions (apply uniformly)

1. **Removed automation commands**: deleted features keep a parse-level
   tombstone where the command was previously documented; dispatch returns a
   truthful, non-zero "feature removed in this version" error. Tokens are
   removed from help/usage text. No removed command may report success.
2. **Shelf ingress**: Shelf accepts Octadock captures and recordings only.
   Arbitrary-file drops, `add-shelf-item`, and the Explorer "Add to Octadock
   dock" verb are removed.
3. **Annotation ingress**: `IAnnotationService.OpenAsync(CaptureRecord)` is the
   capture path and records an honest `Annotated` action. File open accepts
   only registered capture image files and capture-derived `.octadock`
   packages; clipboard-to-annotation entry is removed. Missing/corrupt sources
   fail visibly.
4. **Explorer associations**: startup unregisters legacy per-user "Open with
   Octadock" preview associations and image shell verbs (upgrade cleanup);
   registration code for preview types is deleted, not just skipped.
5. **Data compatibility**: `pins`/preview-related DB rows and files remain
   inert; no destructive migration. Pin restore at startup stops.
6. **Shared utilities**: image-extension detection / image-decode error
   contracts still needed by capture/thumbnail/annotation paths move to
   `App/Imaging` or `Core/Imaging` before Preview deletion.
7. **STT**: Parakeet default, Whisper explicit local fallback, OpenAI only via
   named env-key opt-in, never silent cloud fallback. Baseline benchmark
   (cold/warm start, latency stages, RTF, CPU/mem, WER, dropped/duplicated
   segments) is recorded before optimization; hardware-only rows stay
   explicitly pending.
8. **Recorder**: add microphone + system/app audio tracks, keep explicit
   video-only choice, harden crash/shutdown/disk-full/device-loss recovery;
   no false success; Beta wording stays until acceptance evidence exists.
9. **Settings model**: capture and dictation settings split Basic vs Advanced
   per the owner brief; "Precision aids" (dimensions + magnifier) defaults on;
   freeze-screen stays default-on; launch-at-login defaults on for new
   profiles with first-run opt-out; default shortcuts only Area, Full screen,
   Dictate.

## Evidence plan

## Progress log

- **Wave 1 (committed)** — `092bf51` STT hardening + benchmark (baseline:
  cold 2,785 ms, warm RTF 0.062, 0 dropped/duplicated segments; WER by
  category in `artifacts/acceptance/stt/`); `7474780` recorder mic/system
  audio + finalization/recovery; `4db5720` preview/pin removal + annotation
  re-route; `2699781` this plan. Integrated tree: 1,182 desktop tests green.
- **Wave 2 (in progress)** — shell simplification (minimal Dock, streamlined
  tray, HUD + Pause/Resume removal, shortcut defaults, launch-at-login
  default-on), dictation UX (six states, review-before-insert, insertion
  honesty, device-loss surfacing, speech settings Basic/Advanced), capture
  settings (precision aids, Basic/Advanced, Shelf permanent delete).
  Integration fixes by coordinator: restored `CaptureGate` (cross-flow
  serialization semaphore — not pause machinery; pause was tray state);
  removed the stale force-false recording-audio normalizer in
  `SettingsService` (+ regression tests).
- **Wave 3 (committed)** — `30047ee` design foundation (semantic aliases,
  metric/elevation/motion/font/icon tokens, shared styles, usage guide);
  `70bbe9e` floating surfaces unified (all pills on StatusPill, Dock, Shelf,
  overlays; reduced-motion gating); `7f0397b` standard windows unified
  (shared ListBox styles, ConfirmationDialog for destructive flows, Lucide
  toolbar in the editor, dead FromHud removed). Static WPF gate clean.
- **Wave 4 (committed)** — `cb1f5db` source-of-truth docs refresh;
  `09a1e0b` CI-mode analyzer fixes (CA1001/CA2201). Canonical Release gate
  **passed** 2026-07-24 local: 1,259 desktop + 75 license + 27 internal
  tests, web 21 Chromium, publish + boundary gates. Rendered smoke at 175%
  DPI (`artifacts/rendered/`): Settings Basic/Advanced, History, Clipboard,
  Context, Text Tools, Dock rest+expanded, Shelf with fresh capture,
  annotation editor opened on a fresh capture, unsaved-changes prompt.
  Live CLI tombstones verified (`pin`, `all-in-one`, `open-from-clipboard`
  exit 1 truthfully). PROJECT-STATE snapshot updated with real numbers.

- Scoped `dotnet test` after every slice; full
  `pwsh -NoProfile -File .\build\build.ps1 -Configuration Release` after waves.
- Rendered WPF evidence at repeatable viewports (100/150/200% DPI) in wave 3-4.
- C-04 deterministic dictation acceptance + opt-in live-model run; real
  microphone/device/privacy rows stay pending unless actually executed.
- Negative proof sweep (no reachable preview/pin surface, command, protocol,
  association, or Shelf drop path; legacy associations uninstalled; removed
  CLI commands fail truthfully; preview data not erased).
- Positive annotation proof (capture → Shelf/History → Annotate → edit →
  undo/redo → copy/export/save → reopen project; raw capture unchanged;
  `Annotated` action recorded; corrupt sources fail; restart recovery).

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

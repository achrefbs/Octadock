# Current project state — 13 September 2026

Version: **0.3.0-alpha.2**. Working branch: `codex/design-refresh`.

## Implemented

- Removed license/trial/account UI and feature gates, machine identity, activation client, entitlement verification and the Stripe/license server.
- Removed cloud speech providers, automatic speech-model downloads, update checks and remote AI process execution. Added explicit local model imports.
- Preserved local capture, OCR, annotation, reading, dictation, clipboard history, Context, redaction, comparisons and bundle export.
- Fixed window-picker self-selection, pause-induced scrolling capture completion and stationary-header scroll matching.
- Reworked dock drag to use physical cursor deltas; persisted separate normalized anchors per monitor.
- Added work-area fitting for utility windows, responsive History layout and a single shelf edge indicator. The alpha.2 shelf now uses image-only tiles with a small hover/focus action rail.
- Fixed safe retention of stale external paths, a shelf animation race and double-disposal of speech providers.
- Removed commercial website pages and CI service jobs; retained MIT licensing.

## Design refresh

- Image-only Library with live search, a floating selection toolbar, responsive filters and optional Info drawer.
- Settings uses a labeled sidebar, aligned preference rows and a narrow-window page selector; all existing preferences remain.
- Neutral light/dark themes, restrained blue accents, softer shadows and rounded controls.
- Recording copies as a file; images copy as bitmaps. Copy failures are visible.
- Three local landing options at `web/concepts/`: Air, Studio and Nocturne. Browser interactions are illustrative. The chooser saves a local preference, not a deployment. The original landing page remains at `/` for comparison.

## Validation

The Release gate passed: 1,168 public solution tests (CLI 23, Core 651, Data 88, Windows Platform 118, App 288), 27 internal harness tests, six website static contracts and 23 Chromium browser tests. Self-contained app/CLI publishing and the public artifact boundary passed. Existing informational analyzer warnings remain.

Native acceptance also passed on the attached 2560 x 1440 / 175% primary display and 1920 x 1080 / 100% negative-origin display. Both light and dark runs checked narrow and wide utility windows, History Info open/close, Settings General/Voice models/Shortcuts, dock placement, local export, picker exclusion, capture of a synthetic external window and clean provider disposal. Browser checks also cover all concepts at 390px and short 320px widths, demo clipboard/download actions, reduced motion, local-only requests and automated WCAG A/AA checks. Geometry tests additionally include portrait, narrow and resized monitor layouts. These results do not replace interactive hardware acceptance below.

## External state

At initial inspection the GitHub repository was private, with PR #5 open and recent Actions runs failing before jobs started. Changing local code does not change that service state. Repository publication, hosted-site deployment, release signing and a public release are separate remaining release actions.

## Known limits

Windows x64 only. Manual vertical scrolling requires overlapping viewports and does not support auto-scroll or horizontal stitching. Protected/minimized/special GPU windows may refuse capture. Hardware dictation accuracy, recording audio, long-duration soak and interactive dock dragging still need acceptance beyond automated regression coverage.

# Current project state — 13 September 2026

Version: **0.3.0-alpha.1**. Working branch: `codex/free-local-modernization`.

## Implemented

- Removed license/trial/account UI and feature gates, machine identity, activation client, entitlement verification and the Stripe/license server.
- Removed cloud speech providers, automatic speech-model downloads, update checks and remote AI process execution. Added explicit local model imports.
- Preserved local capture, OCR, annotation, reading, dictation, clipboard history, Context, redaction, comparisons and bundle export.
- Fixed window-picker self-selection, pause-induced scrolling capture completion and stationary-header scroll matching.
- Reworked dock drag to use physical cursor deltas; persisted separate normalized anchors per monitor.
- Added work-area fitting for utility windows, responsive History layout, a single shelf edge indicator and always-available screenshot action footer.
- Fixed safe retention of stale external paths, a shelf animation race and double-disposal of speech providers.
- Removed commercial website pages and CI service jobs; retained MIT licensing.

## Validation

The Release gate passed: 1,162 public solution tests (CLI 23, Core 651, Data 88, Windows Platform 118, App 282), 27 internal harness tests, six website static contracts and 15 Chromium browser tests. Self-contained app/CLI publishing and the public artifact boundary passed. Existing informational analyzer warnings remain.

Native acceptance also passed on the attached 2560 x 1440 / 175% primary display and 1920 x 1080 / 100% negative-origin display. It checked narrow and wide utility windows, dock placement, local export, picker exclusion, capture of a synthetic external window and clean provider disposal. Geometry tests additionally include portrait, narrow and resized monitor layouts. These results do not replace interactive hardware acceptance below.

## External state

At initial inspection the GitHub repository was private, with PR #5 open and recent Actions runs failing before jobs started. Changing local code does not change that service state. Repository publication, hosted-site deployment, release signing and a public release are separate remaining release actions.

## Known limits

Windows x64 only. Manual vertical scrolling requires overlapping viewports and does not support auto-scroll or horizontal stitching. Protected/minimized/special GPU windows may refuse capture. Hardware dictation accuracy, recording audio, long-duration soak and interactive dock dragging still need acceptance beyond automated regression coverage.

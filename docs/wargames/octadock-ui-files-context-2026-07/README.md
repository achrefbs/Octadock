# Octadock UI, Files, and Context Shelf Wargame

This folder is the handoff packet for pressure-testing the next Octadock plan with
Fable or another multi-agent scenario/wargaming model.

## How to Use

Point Fable at this directory and paste the prompt in `FABLE_PROMPT.md`.

Recommended read order:

1. `FABLE_PROMPT.md`
2. `PROJECT_CONTEXT.md`
3. `IMPLEMENTATION_PLAN.md`
4. `WARGAME_PLAN.md`
5. `OUTPUT_TEMPLATE.md`
6. `reference-images/`

## Reference Images

- `reference-images/01-pinned-media-viewer.png` - desired pinned/media viewer direction.
- `reference-images/02-command-deck-shelf-files.png` - command deck, shelf, file/table concept.
- `reference-images/03-history-library-inspector.png` - history/library with right inspector.
- `reference-images/04-shelf-row-panel.png` - compact shelf row panel; strongest reference for Capture Shelf redesign.
- `reference-images/05-capture-hud.png` - capture HUD concept.

## Ground Rules

- Do not revive AI discovery or AI Sessions. That work has been dropped for now.
- Treat the current code status in `PROJECT_CONTEXT.md` as the source of truth.
- The desired output is not a new product vision. It is a stress test of the
  current plan, plus concrete revisions that make implementation safer.
- Favor local-first, Windows-native workflows.


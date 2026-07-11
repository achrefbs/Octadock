# Design QA — Capture Shelf row panel

- source visual truth path: `docs/wargames/octadock-ui-files-context-2026-07/reference-images/04-shelf-row-panel.png`
- implementation screenshot path: not captured
- viewport: intended 364 DIP panel width (medium density), dark theme; rendered viewport unavailable
- state: intended five-row shelf, newest row selected, action rail shown on hover or keyboard focus; rendered state unavailable

## Full-view comparison evidence

Blocked. The source reference was opened and inspected, but no rendered screenshot of the rebuilt WPF `ShelfWindow` was captured. Code inspection and a successful WPF build are not substitutes for visual comparison, so frame proportions, density, typography, glass opacity, shadows, and row rhythm cannot be signed off.

## Focused-region comparison evidence

Blocked for the same reason. Without an implementation capture, the header, selected teal rail/dot, fixed 84 × 56 thumbnail well, extension-preserving filename treatment, metadata lines, Lucide action rail, hover state, keyboard-focus state, empty state, and extreme-aspect-ratio letterboxing cannot be compared in a combined visual input.

## Findings

- [P1] Rendered implementation evidence is missing
  - Location: `ShelfWindow` and `ShelfItemView`.
  - Evidence: the source image is available, but there is no implementation screenshot at the matching viewport and state.
  - Impact: the required typography, spacing/layout, colors/tokens, image quality, icons, copy, interaction states, and accessibility fidelity passes cannot be completed honestly.
  - Fix: run the rebuilt app with capture exclusion disabled, populate five representative shelf items (including a recording and extreme-aspect-ratio image), capture the 364-DIP dark-theme panel at rest and with a row action rail visible, combine each implementation capture with the source, and perform the comparison passes.

## Open Questions

- Whether the 364-DIP implementation needs minor optical adjustment after direct comparison with the approximately 2× source reference.
- Whether light-theme contrast or text scaling exposes any clipping that is not visible from XAML inspection.

## Comparison History

- No P0/P1/P2 visual iteration was possible because the first implementation capture is missing.
- Functional evidence completed separately: the WPF project built successfully; all 15 Capture Shelf tests passed; all 124 app tests passed from the coherent build.

## Implementation Checklist

1. Capture the rebuilt shelf at the matching dark-theme viewport and representative five-row state.
2. Capture selected, hover/action-rail, keyboard-focus, empty, long-filename, recording, and extreme-aspect-ratio states.
3. Compare source and implementation together using the required fidelity surfaces.
4. Fix any P0/P1/P2 drift and repeat at the same viewport and state.

## Follow-up Polish

- Evaluate animation timing and glass/shadow balance on a real desktop wallpaper after the fidelity pass.

final result: blocked

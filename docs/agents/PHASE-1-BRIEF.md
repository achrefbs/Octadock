# Phase 1 task briefs (recovery)

Read `docs/PLAN.md` first (rules), then the matching gate table in
`docs/ROADMAP.md` for the binding acceptance evidence. **Do not start Gate E.**
Founder-blocked items are at the bottom — do not attempt them, do not simulate
them.

## T-0 — Land the in-flight preview/context hardening

The working tree carries ~35 modified and 4 new files (preview recovery
taxonomy + tests, `ActiveContextState`, pin/shelf/context touches). Goal:
verify the full Release gate is green with them, produce a short per-file
inventory (what changed, why), and prepare the diff for the integration path.
Do not rewrite the work; do not let it rot uncommitted. Evidence: build/test
output + the inventory.

## A-05 — Reproducible website validation

Every quality claim about `web/` must be backed by committed tooling: syntax,
link/asset, static fallback, accessibility, and browser-smoke checks runnable
from a script in the repo. Remove or re-back any claim (e.g. the npm/56-test
one) that has no committed manifest. Evidence: the script runs clean from a
fresh checkout.

## A-06 — Release packaging workflow

Author the strict release workflow: versioned ZIP, manifest, SHA-256 checksums,
public-boundary evidence, documented tag path (extends `build/release.ps1`).
Remote runs stay blocked until A-03/A-04, but the workflow must pass
`actionlint` and dry-run locally where possible.

## C-01 — Contextual AI review consolidation (largest single item)

Shelf, Context, History, and Clipboard must open one coherent,
source-bound review; the standalone "AI screen" positioning, copy, and entry
points are removed; the `ai` command, historical aliases, and the
`ShowAiActions` seam remain as compatibility adapters. Key files:
`src/Octadock.App/Ai/AgentWorkspaceWindow.xaml`, `Ai/AiActionsWindow.xaml`,
`Ai/AgentWorkspaceViewModel.cs`, plus tray/dock entry points. Evidence:
regression tests for every entry source + rendered checks.

## C-02 — Capture/Shelf/History correctness pass

Duplicate, discard/undo, restore, thumbnail refresh, centered-open, restart
recovery, and mixed-DPI cases pass automated plus rendered tests.

## C-03 — Context launch-scope pass

Included/exported items match exactly; changed or missing references fail
closed; naming/notes/reorder acceptance recorded.

## C-04 — Dictation reliability matrix (final evidence needs founder hardware)

Prepare the model/device/privacy/cancel/partial/clipboard-recovery matrix and
all automatable coverage. Real microphone/language/device runs happen on the
founder's hardware — flag them as pending, never fabricate results.

## C-05 — Performance baseline

Startup, idle CPU, capture latency, memory, handle count, GPU: publish
before/after evidence, then take the top three fixes.

## C-06 — Visual/accessibility acceptance (partially human)

Fixed-viewport comparisons, keyboard/focus, high contrast, reduced
transparency/motion, mixed DPI across primary surfaces. Automate what is
automatable; list what needs eyes.

## C-07 — Long-run integrity soak

Repeated core workflows show no false success, leak, corrupt artifact, or stuck
device/hook.

## B-09 — Security rescan

Only after the founder decides B-04 (offline-trial policy). Scope: no high
finding; every medium fixed or explicitly accepted.

## Founder-blocked — do not attempt

| Item | Why blocked |
| --- | --- |
| A-03 GitHub Actions startup failures | Account billing/quota — founder access only |
| A-07 GitHub plan / branch protection | Owner settings |
| A-09 worktree/archive retirement | Needs founder approval of the inventory |
| B-04 offline-trial policy | Product/legal decision |
| Gate D signing, Stripe, legal, DNS | External accounts and identity |

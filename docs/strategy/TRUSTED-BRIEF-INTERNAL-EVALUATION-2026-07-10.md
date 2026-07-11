# Trusted Brief internal evaluation — 2026-07-10

Status: internal evidence baseline. This is not public feature approval and not
a claim that summary quality has passed founder review.

## Implemented loop

The isolated Workflow Intelligence harness now supports:

1. Claude Code `Stop`/`SubagentStop` and Codex `Stop` payload parsing.
2. Complete-turn reconstruction from provider JSONL with exact byte, record,
   JSON-pointer, and character anchors.
3. Explicit start/end boundary evidence and honest incomplete-turn downgrade.
4. Private reasoning-block exclusion and tool invocation/result separation.
5. A deterministic typed evidence ledger with high-importance promotion for
   negation and modality such as `do not`, `never`, `expected to fail`, and
   `not verified`.
6. Evidence-only brief composition: displayed text is copied from identified
   evidence rather than invented by the composer.
7. Independent deterministic verification for unsupported claims, omitted
   high-importance evidence, and misplaced assumptions/unresolved questions.
8. Revalidation of every displayed anchor against the unchanged original
   JSONL record bytes immediately before accepting the brief.
9. Low-value/short-turn suppression.
10. Idempotent encrypted persistence: duplicate completion hooks resolve to the
    same derived artifact.

No hook was installed automatically. No watcher, public UI, background screen
collection, or external model send was enabled.

## Live baseline

Read-only forced evaluation ran over the 25 newest Claude files and 25 newest
Codex files on the founder machine. Two Claude files contained no reconstructable
assistant turn, leaving 48 real turns.

| Metric | Claude | Codex | Combined observation |
| --- | ---: | ---: | --- |
| Reconstructed turns | 23 | 25 | 48 |
| Structurally complete turns | 10 | 24 | Incomplete units were downgraded, never silently trusted |
| Trusted briefs | 10 | 23 | 33 |
| Coverage-warning briefs | 13 | 2 | 15 |
| JSON parse errors | 0 | 0 | 0 |
| Unsupported ledger claims | 0 | 0 | 0 |
| Omitted ledger-high items | 0 | 0 | 0 |
| Revalidated displayed anchors | 4,481 | 13,675 | 18,156 |
| Anchor failures | 0 | 0 | 0 |
| Mean source/brief compression | 3.07× | 5.44× | Directionally useful, not founder-approved |
| Worst observed latency | — | — | 1.50 seconds |

`100% ledger-high recall` means the deterministic composer represented every
item that the current extractor itself marked high. It does **not** prove 100%
recall of real human-labeled critical facts. That remains the decisive P0 AI
quality gate.

## What the evidence says

The mechanics pass the initial feasibility gate: provider-native reconstruction
is fast enough, anchors are stable, malformed certainty is avoided, and the
pipeline can run from a completion hook without adding a user step.

The quality gate is still open. Some real turns produce many high-importance
items, particularly long instruction-heavy turns. Arbitrarily truncating them
would make the product look cleaner while breaking its trust promise. The next
experiment should compare model-assisted ranking/grouping over immutable
evidence IDs. The model may select and organize IDs; displayed claims continue
to come from anchored evidence, and deterministic coverage verification remains
mandatory.

External processing remains off by default. Running a Claude/Codex/local-model
bake-off requires a separate destination-named egress approval and no silent
provider fallback.

## Next decision gate

1. Label 20 stress turns first, then a held-out set, with P0/P1 constraints,
   completed versus proposed work, failures, assumptions, and corrections.
2. Compare deterministic, Claude, Codex, and one local ranking/composition
   profile using the same immutable ledger.
3. Require 100% labeled-P0 recall, zero unsupported high-impact claims, exact
   numbers/filenames/negations, and acceptable founder reading time.
4. Only then prototype capsule/anchored presentation. No new AI destination
   screen should be built.

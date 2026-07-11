# Workflow Intelligence — Internal Research Addendum

Status: founder-approved internal research direction, 2026-07-10.

This addendum records the narrow decision required by the Workflow Intelligence
wargame. It does not change the public Octadock product promise.

## Decision

Octadock may collect consented workflow metadata and raw application/agent
content in a separately built **internal developer edition** for the purpose of
debugging, measuring real workflows, and evaluating complete-turn Trusted
Briefs for Codex and Claude Code.

The founder has chosen the `Full developer trace` consent level for their own
internal installation. That choice is local to the internal edition; it does
not authorize collection in a public build.

## Boundaries

- The public application remains explicit-action, local-first, and does not
  acquire background application content.
- Workflow metadata in a future public build remains an unapproved candidate
  requiring a separate product, privacy, and legal decision.
- Undisclosed screen watching, keylogging, hidden provider sends, provider
  fallback, and collection from excluded applications remain prohibited.
- The internal collector must live in a separate assembly/project excluded
  from `Octadock.sln` and all public publish artifacts.
- Raw content must use a separate encrypted store. It must never be written to
  `octadock.db`, normal logs, crash reports, or the core database salvage path.
- Installing a provider hook is itself a consent action. No hook is installed
  automatically by building or running the public application.
- Initial work is observe-only. Prediction, automatic external sends, and
  workspace writes are out of scope for the internal evidence phase.

## First approved implementation slice

1. A public-artifact gate proving the internal assembly is absent.
2. A separate internal command-line harness.
3. A user-bound encrypted raw-content store with bounded retention and complete
   deletion of its database, key, WAL/SHM files, exports, and temporary files.
4. Read-only Claude Code and Codex corpus/adaptor self-tests using provider
   hooks as boundaries and incremental transcript/rollout readers as evidence.
5. A metadata-only corpus sampler over existing local sessions.

This slice does not add an Octadock window, public setting, background watcher,
or live summary surface.

## Revisit gate

No public-facing Workflow Intelligence work begins until the internal corpus
demonstrates useful extraction accuracy, critical-constraint recall, acceptable
latency, and a presentation the founder accepts as native rather than another
Octadock destination.

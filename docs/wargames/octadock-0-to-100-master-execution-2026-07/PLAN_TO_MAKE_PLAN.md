# Plan To Create The 0-To-100 Master Execution Plan

Date: 2026-07-06

## Purpose

This document is not the master execution plan. It is the method and quality
bar for creating that plan.

The final plan must turn all existing Octadock strategy, audits, Fable passes,
founder decisions, and code reality into one coherent 0-to-100 execution map.
It should not split the work into artificial P0/P1/P2 phases. Instead, it
should describe the full journey from current repo state to paid beta, 1.0, and
post-1.0 expansion as one dependency-driven build.

## Core Principle

Build one continuous plan, not a priority theater.

The old P0/P1/P2 language can still be useful internally, but the final plan
should replace it with clearer execution labels:

- `Critical Path Gate`: work that must be complete before accepting money,
  shipping public downloads, or making a product claim.
- `Parallel Now`: work that can move while a critical-path item is in progress.
- `Architecture Now`: a decision or interface that must be shaped now even if
  the full feature ships later.
- `Fast Follow`: important work planned immediately after paid beta once the
  launch path is proven.
- `Deferred`: valid product ideas that are intentionally not part of the first
  commercial launch.

## Required Inputs

The planner must read and reconcile these sources before writing the master
plan:

- `docs/PROJECT-STATE.md`
- `docs/CAPABILITIES.md`
- `docs/ROADMAP.md`
- `docs/design/UI-INVENTORY.md`
- `docs/design/HOMEPAGE-CONCEPT-2026-07-06.md`
- `docs/research/LAUNCH-PRICING-RESEARCH-2026-07-06.md`
- `docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md`
- `docs/wargames/octadock-ui-files-context-2026-07/IMPLEMENTATION_PLAN.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/WARGAME_RESULT.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/CODEX_AUDIT.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/PAYMENT_PROVIDER_DECISION.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/TRIAL_AND_PURCHASE_FLOW.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/ADMIN_PANEL_SPEC.md`
- `docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/WARGAME_RESULT.md`
- `docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/CODEX_AUDIT.md`

The planner must also inspect the codebase directly. A feature is only
considered built when the code exists and the behavior can be verified. Docs,
screenshots, and plans are evidence, not proof.

## Authority Model

When sources disagree, use this order of authority:

1. Explicit founder decisions in the conversation or committed decision docs.
2. Current code reality.
3. Latest Codex audit of a Fable result.
4. Latest Fable wargame result.
5. Current strategy/spec docs.
6. Older roadmap or aspirational docs.

Important current decisions to preserve:

- Stripe is the selected payment provider.
- v1 should avoid first-party accounts and use license key plus purchase email.
- Free trial should be public, no account, no card, then payment later.
- Pro is waitlist only until metering, hosted usage, quotas, and dunning exist.
- Capture Shelf and Context are separate surfaces.
- AI discovery is dropped for now and must not reappear as a hidden dependency.
- Context, file preview, and annotation work should be planned deeply but not
  overpromised in paid-beta checkout copy until shipped.

## Conflict Resolution Rules

The planner must explicitly resolve conflicts instead of silently choosing one
source.

For every conflict, write:

- `Conflict`: the disagreeing claims.
- `Decision`: the source of truth to use.
- `Reason`: why that decision is safer or more current.
- `Plan Impact`: what changes in sequencing, copy, scope, or architecture.

Known conflicts to check:

- Paddle recommendation versus founder's Stripe decision.
- "Fully offline" or "nothing leaves your PC" language versus model downloads,
  BYO cloud routes, activation, updates, and future diagnostics.
- "Open every file type" ambition versus rich preview support arriving in
  phases.
- Full admin panel ambition versus minimal paid-beta admin needs.
- Direct file editing desire versus corruption risk for PDF, Office, archives,
  and design formats.
- UI polish desire versus launch-trust blockers such as signing, installer,
  website, Stripe, license service, and support.

## Workstream Model

The master plan should be organized by workstream, then connected through a
dependency graph.

Required workstreams:

- `Distribution and Trust`: installer, signing, update path, SmartScreen/EDR
  testing, release artifacts, download integrity.
- `Website and Public Funnel`: homepage, pricing, download page, trial copy,
  support contact, privacy/legal pages, refund/update promises.
- `Stripe and Commercial Backend`: products, Checkout, tax posture, webhooks,
  signature verification, idempotency, refunds, disputes, reconciliation.
- `Licensing and Entitlements`: license model, activation, device identity,
  offline grace, revocation, update entitlement, manual key entry.
- `Trial and Gates`: free trial state, post-expiry verb matrix, local-only
  behavior, upgrade prompts, no-card start, one extension.
- `Admin and Operations`: launch-health dashboard, license search, activation
  timeline, webhook/email health, resend/deactivate/revoke, audit log, support
  runbooks.
- `Privacy and Network Egress`: BYO keys, model downloads, activation calls,
  cloud confirmation, telemetry policy, log redaction, user-facing copy.
- `Design System and UI Rewrite`: tokens, cards/rows, dock, capture HUD,
  history/library, pin viewer, settings, website alignment.
- `Files, Preview, Editing, and Annotation`: universal file router, fallback
  previews, PDF, Office, archives, image annotation, writeback safety, backups.
- `Capture Shelf and Context`: separate shelf models, context package export,
  Explorer verbs, mixed capture/file/text bundles, persistence.
- `Testing and Verification`: unit, integration, release-candidate, manual VM,
  mixed-DPI, offline, payment, refund, activation, destructive-save tests.
- `Support and Launch Ops`: support inbox, incident runbooks, refund handling,
  buyer resend flow, beta feedback loop, launch monitoring.
- `Post-Beta Architecture`: Pro waitlist, hosted usage metering, teams,
  accounts, quota enforcement, dunning, enterprise/admin futures.

## Plan-Building Steps

### 1. Establish Current Truth

Create a built/partial/not-built inventory from the codebase and docs.

For every significant capability, record:

- current implementation status;
- file or doc evidence;
- verification method;
- launch claim status: `can claim`, `claim carefully`, or `do not claim`.

### 2. Build The Dependency Graph

Create a graph of what depends on what.

Examples:

- Public paid beta depends on signed installer, website/download page, Stripe
  checkout, license service, activation UI, minimal admin, support contact,
  privacy/legal copy, and verified refund/revocation flow.
- Direct Office/PDF writeback depends on provider investigation, backup model,
  save failure tests, and clear UI state.
- "Open all files" depends only on universal fallback preview, not rich support
  for every file type.
- Context export depends on persistent context model and package manifest, not
  AI sessions.

### 3. Separate Gates From Workstreams

Do not sequence the whole company as one narrow waterfall.

Each work item should have:

- `Gate`: what it blocks, if anything.
- `Can Run With`: workstreams that can proceed in parallel.
- `Must Wait For`: hard dependencies.
- `Risk If Late`: what breaks if it slips.
- `Verification`: how we know it is done.

### 4. Write The Commercial Path First

The master plan must define the exact path from first visit to paid activation:

1. User lands on website.
2. User downloads signed installer.
3. User starts no-card trial.
4. User sees honest trial state and feature gates.
5. User purchases through Stripe.
6. Stripe webhook creates or updates exactly one entitlement.
7. User receives or copies license key.
8. User activates Octadock manually.
9. Admin can verify purchase, webhook, email, activation, refund, revocation,
   and support actions.

If any step is not fully specified, it is a plan gap.

### 5. Write The Product Experience Path Next

The master plan must define the main app experience after launch:

1. Capture and shelf flow.
2. History/library flow.
3. File open and fallback preview flow.
4. Context creation and export flow.
5. Annotation and writeback flow.
6. Settings, billing, privacy, and BYO-key flow.

Each flow must say what ships in paid beta, what is architected for later, and
what copy must avoid promising too early.

### 6. Turn Design Into Implementation Rules

The design system should become operational, not decorative.

The master plan must produce:

- token names for WPF resources and web CSS variables;
- color, typography, spacing, radius, elevation, and motion rules;
- component rules for dock, shelf rows, cards, inspectors, toolbars, modals,
  settings panes, file previews, and admin tables;
- "bad old UI" replacement targets, especially short screenshot shelf cards;
- accessibility checks for contrast, focus, keyboard, density, and text fit.

### 7. Define No-Go Criteria

The plan must include hard launch no-go rules.

At minimum, paid beta cannot open if:

- installer is unsigned or distribution trust is untested;
- payment can succeed without reliable entitlement creation;
- webhook signatures, idempotency, and replay behavior are unverified;
- license key delivery and manual activation are not proven;
- refund/revoke/reinstate cannot be handled by admin;
- public copy promises features that are not built;
- privacy copy hides real network paths;
- support has no way to look up buyer and license state;
- destructive file editing can corrupt originals without backup or recovery.

### 8. Define Done For The Master Plan

The final master plan is done only when it contains:

- one-page executive sequence;
- current-state inventory;
- dependency graph;
- workstream-by-workstream backlog;
- launch gate list;
- paid beta no-go criteria;
- app flow map;
- commercial flow map;
- design system execution map;
- file/context/annotation execution map;
- admin/ops execution map;
- test and verification matrix;
- open decision register;
- deferred scope list;
- first two weeks of concrete implementation tasks.

## Master Plan Output Skeleton

The final document should use this structure:

1. `Executive Readout`
2. `Current Reality`
3. `Locked Decisions`
4. `Open Decisions`
5. `Dependency Graph`
6. `0-To-100 Sequence`
7. `Workstream Plans`
8. `Commercial Launch Path`
9. `Desktop Product Path`
10. `Admin And Operations Path`
11. `Design System Path`
12. `Files, Context, And Annotation Path`
13. `Testing And Verification`
14. `No-Go Criteria`
15. `Risks And Pre-Mortem Responses`
16. `Deferred Scope`
17. `First Implementation Batch`

## Quality Bar

The master plan must pass these checks:

- Every item has a dependency, owner role, acceptance criteria, and verification
  method.
- Every external promise maps to built behavior or a copy correction.
- Every money path has admin visibility.
- Every network path has privacy copy and user-facing consent where needed.
- Every file writeback path has backup, rollback, and corruption tests.
- Every long-lead dependency starts early.
- Every "later" feature still has an architecture decision if it affects v1.
- No AI discovery system is reintroduced.
- No plan item hides behind vague language like "integrate payments",
  "improve UI", "support all files", or "add admin".

## First Planning Session Agenda

Use this agenda when creating the master plan:

1. Read the required inputs.
2. Inspect code reality for payment, licensing, packaging, file preview,
   capture shelf, context, admin, and design-token implementation.
3. Draft current-state inventory.
4. Draft dependency graph.
5. Resolve conflicts and record decisions.
6. Write the 0-to-100 sequence.
7. Expand workstream plans.
8. Add launch gates and no-go criteria.
9. Add first implementation batch.
10. Review the plan against the quality bar.

## Immediate Next Artifact

Create:

`docs/wargames/octadock-0-to-100-master-execution-2026-07/0_TO_100_MASTER_EXECUTION_PLAN.md`

That document should be the execution source of truth for the next coding pass.

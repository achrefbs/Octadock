# Agent Prompt For The 0-To-100 Master Execution Plan

Use this prompt with the planning agent that will create the final master
execution plan.

```text
You are working in the Octadock repository.

Your job is to create the complete 0-to-100 master execution plan for Octadock.
Do not implement product code. Do not create a loose roadmap. Do not produce a
generic strategy document. Produce a concrete, dependency-driven execution plan
that can become the source of truth for the next build pass.

First, read this file fully:

docs/wargames/octadock-0-to-100-master-execution-2026-07/PLAN_TO_MAKE_PLAN.md

Then use it as the contract for the work.

You must create this final artifact:

docs/wargames/octadock-0-to-100-master-execution-2026-07/0_TO_100_MASTER_EXECUTION_PLAN.md

Core objective:

Create one continuous plan from the current repo state to paid beta, 1.0, and
post-1.0 expansion. Do not organize the plan as fake P0/P1/P2 phases. Instead,
organize it around dependencies, launch gates, parallel workstreams, no-go
criteria, verification, and the exact order in which work should become real.

Very important:

- Do not trust the docs blindly. Inspect the codebase and verify what is built,
  partially built, or not built.
- Treat screenshots, roadmap docs, and Fable results as evidence, not proof.
- If a feature is documented but not present in code, mark it as not built.
- If a feature exists but is fragile, incomplete, or unverified, mark it as
  partial and explain the verification gap.
- Do not reintroduce the dropped AI discovery system.
- Preserve the founder decision that Stripe is the payment provider.
- Preserve the decision that v1 avoids first-party accounts and uses license key
  plus purchase email.
- Preserve the decision that the free trial starts without an account and
  without a card.
- Preserve the decision that Pro is waitlist-only until metering, hosted usage,
  quotas, dunning, and cloud consent are built.
- Preserve the decision that Capture Shelf and Context are separate product
  surfaces.

Required input documents:

- docs/PROJECT-STATE.md
- docs/CAPABILITIES.md
- docs/ROADMAP.md
- docs/design/UI-INVENTORY.md
- docs/design/HOMEPAGE-CONCEPT-2026-07-06.md
- docs/research/LAUNCH-PRICING-RESEARCH-2026-07-06.md
- docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md
- docs/wargames/octadock-ui-files-context-2026-07/IMPLEMENTATION_PLAN.md
- docs/wargames/octadock-pricing-accounts-design-2026-07/WARGAME_RESULT.md
- docs/wargames/octadock-pricing-accounts-design-2026-07/CODEX_AUDIT.md
- docs/wargames/octadock-pricing-accounts-design-2026-07/PAYMENT_PROVIDER_DECISION.md
- docs/wargames/octadock-pricing-accounts-design-2026-07/TRIAL_AND_PURCHASE_FLOW.md
- docs/wargames/octadock-pricing-accounts-design-2026-07/ADMIN_PANEL_SPEC.md
- docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/WARGAME_RESULT.md
- docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/CODEX_AUDIT.md

Also inspect the relevant source code for:

- capture and dock behavior;
- capture shelf and pinned media;
- history/library;
- file preview and open behavior;
- annotation and save/writeback behavior;
- OCR, dictation, read-aloud, BYO keys, and cloud/network paths;
- packaging/release scripts;
- settings;
- any existing licensing, account, payment, entitlement, website, admin, or
  backend implementation;
- design tokens, shared styles, and UI resources.

Use this authority model when sources disagree:

1. Explicit founder decisions in committed decision docs or conversation
   summaries.
2. Current code reality.
3. Latest Codex audit of a Fable result.
4. Latest Fable wargame result.
5. Current strategy/spec docs.
6. Older roadmap or aspirational docs.

The final plan must include these sections:

1. Executive Readout
2. Current Reality
3. Locked Decisions
4. Open Decisions
5. Dependency Graph
6. 0-To-100 Sequence
7. Workstream Plans
8. Commercial Launch Path
9. Desktop Product Path
10. Admin And Operations Path
11. Design System Path
12. Files, Context, And Annotation Path
13. Testing And Verification
14. No-Go Criteria
15. Risks And Pre-Mortem Responses
16. Deferred Scope
17. First Implementation Batch

Required workstreams:

- Distribution and Trust
- Website and Public Funnel
- Stripe and Commercial Backend
- Licensing and Entitlements
- Trial and Gates
- Admin and Operations
- Privacy and Network Egress
- Design System and UI Rewrite
- Files, Preview, Editing, and Annotation
- Capture Shelf and Context
- Testing and Verification
- Support and Launch Ops
- Post-Beta Architecture

For every major work item, include:

- current status: built, partial, not built, or unknown;
- evidence: code path or document path;
- dependencies;
- what it blocks;
- what can run in parallel;
- acceptance criteria;
- verification method;
- risk if delayed;
- copy or launch-claim impact.

Use these execution labels instead of P0/P1/P2:

- Critical Path Gate
- Parallel Now
- Architecture Now
- Fast Follow
- Deferred

Resolve conflicts explicitly. At minimum, address:

- Stripe decision versus older Paddle recommendation.
- "Fully offline" or "nothing leaves your PC" copy versus activation, model
  downloads, BYO cloud routes, updates, and future diagnostics.
- "Open every file type" ambition versus phased rich previews with fallback.
- Full admin panel ambition versus minimal paid-beta admin needs.
- Direct file editing desire versus corruption risk for PDF, Office, archives,
  and design formats.
- UI polish desire versus launch-trust blockers such as signing, installer,
  website, Stripe, license service, and support.

The paid-beta commercial path must be specified end to end:

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

The desktop product path must cover:

- Capture and shelf flow.
- History/library flow.
- File open and fallback preview flow.
- Context creation and export flow.
- Annotation and writeback flow.
- Settings, billing, privacy, and BYO-key flow.

The plan must include hard paid-beta no-go criteria. At minimum, paid beta
cannot open if:

- the installer is unsigned or distribution trust is untested;
- payment can succeed without reliable entitlement creation;
- webhook signatures, idempotency, and replay behavior are unverified;
- license key delivery and manual activation are not proven;
- refund, revoke, and reinstate cannot be handled by admin;
- public copy promises features that are not built;
- privacy copy hides real network paths;
- support has no way to look up buyer and license state;
- destructive file editing can corrupt originals without backup or recovery.

Quality bar:

- Be concrete. Avoid vague tasks like "integrate payments", "improve UI",
  "support all files", or "add admin".
- Every important task must have dependencies, acceptance criteria, and
  verification.
- Every public product claim must map to built behavior or a required copy
  correction.
- Every money path must have admin visibility.
- Every network path must have privacy copy and user-facing consent where
  needed.
- Every file writeback path must have backup, rollback, and corruption tests.
- Every long-lead dependency must start early.
- Every deferred feature that affects v1 architecture must still have an
  architecture decision now.

Output requirements:

- Write the final plan to:
  docs/wargames/octadock-0-to-100-master-execution-2026-07/0_TO_100_MASTER_EXECUTION_PLAN.md
- Include concise tables where they make the plan easier to execute.
- Include a Mermaid dependency graph if useful.
- Use direct repository paths as evidence.
- Keep the tone direct, practical, and founder-readable.
- Do not make implementation code changes.
- If you discover missing information that blocks a reliable plan, create an
  "Open Decisions" section with the exact question, why it matters, and the
  safest default recommendation.

Before finishing:

- Check that the plan satisfies PLAN_TO_MAKE_PLAN.md.
- Check that every section listed above exists.
- Check that no AI discovery work has slipped back in.
- Check that Stripe, no-card trial, no first-party v1 accounts, and separate
  Capture Shelf/Context decisions are preserved.
- Check that the first implementation batch is actionable enough for a coding
  agent to start immediately.
```

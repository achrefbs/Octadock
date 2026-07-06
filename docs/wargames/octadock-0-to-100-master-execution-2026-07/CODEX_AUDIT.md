# Codex Audit - 0-To-100 Master Execution Plan

Date: 2026-07-06

Source artifact reviewed:

- `.claude/worktrees/pensive-cray-31e0af/docs/wargames/octadock-0-to-100-master-execution-2026-07/0_TO_100_MASTER_EXECUTION_PLAN.md`

Imported artifact:

- `docs/wargames/octadock-0-to-100-master-execution-2026-07/0_TO_100_MASTER_EXECUTION_PLAN.md`

## Verdict

Adopt the master plan as the execution source of truth.

We are ready to start the 0-to-100 build from this plan. We are not ready to
launch or charge money yet, and the plan correctly says that. The commercial
layer is still greenfield, but the work is now sequenced well enough to execute:
long-lead trust/procurement first, commercial spine next, then trial/gates,
admin visibility, privacy/copy fixes, file safety, and finally broader UI/file
polish.

The only import correction is that the plan was generated from an older worktree
where `PLAN_TO_MAKE_PLAN.md` and the launch pre-mortem `CODEX_AUDIT.md` were
missing. On current `main`, both files exist. I corrected that caveat in the
imported plan and treated this audit as the adoption note.

## What I Checked

- The expected plan was not present on `main`; it existed only in
  `.claude/worktrees/pensive-cray-31e0af`.
- The plan contains all 17 required sections from
  `PLAN_TO_MAKE_PLAN.md` and `AGENT_PROMPT.md`.
- The plan preserves the locked founder decisions:
  - Stripe is the payment provider.
  - Free trial starts without account or card.
  - v1 avoids first-party accounts.
  - Pro remains waitlist-only.
  - Capture Shelf and Context remain separate.
  - AI discovery / AI Sessions are not reintroduced.
- Source spot-checks on current `main` confirm the plan's main code-reality
  claims:
  - No source/backend project exists for Stripe, webhooks, licensing, trial,
    entitlements, machine identity, or asymmetric entitlement signing.
  - `build/release.ps1` still publishes framework-dependent zip artifacts,
    with no signing, installer, `win-x64` runtime identifier, self-contained
    publish, or single-file publish.
  - The "fully offline" dictation tooltip and "Summarizing with local AI" cloud
    label still exist.
  - Direct overwrite save paths still exist in shelf/history/pin/file-preview
    save operations.
  - `FilePreviewService` blocks UNC in preview, while add-to-shelf/pin paths
    still need the same guard.
  - `OPENAI_API_KEY` is still adopted by the OpenAI STT provider.
- Official external docs checked:
  - Microsoft currently documents that EV no longer bypasses SmartScreen, that
    Artifact Signing is the recommended non-Store signing path, and that
    reputation still builds over time.
  - Stripe currently documents webhook signature verification through the
    `Stripe-Signature` header and `constructEvent`.
  - Stripe Tax currently documents AI product tax codes and EU VAT/OSS
    considerations.
  - Stripe Managed Payments changelog currently says Managed Payments reached
    general availability on April 22, 2026, and added AI product tax code
    eligibility on June 2, 2026.

## Why This Plan Is Good Enough To Execute

The plan solves the earlier P0/P1/P2 problem. It does not pretend we can build
everything in one undifferentiated push, but it also does not delay important
work behind fake phases. It separates:

- `Critical Path Gate`: what blocks paid beta.
- `Parallel Now`: work that can safely proceed alongside the gates.
- `Architecture Now`: decisions that must be made before schemas, license
  formats, or public promises harden.
- `Fast Follow`: valuable work that should follow beta but not block it.
- `Deferred`: valid ideas that would slow or destabilize beta.

That is the right model for "0 to 100 in one pass": a single continuous plan,
ordered by dependency and risk.

## Required Corrections Already Applied

1. Imported the master plan from the worktree into `main`.
2. Replaced the stale "missing inputs" paragraph with an import note.
3. Replaced the two stale missing-input open-decision rows with one import
   caveat row.

## Remaining Caveats

These do not require another Fable pass. They are execution inputs or first-batch
actions:

- Signing path: Azure Artifact Signing is a good default, but eligibility and
  identity validation still need to be started in the actual account. Microsoft
  documents geographic limits, especially for individual developers.
- Tax/MoR posture: Stripe Managed Payments appears viable enough to investigate
  first, but the Stripe dashboard must confirm eligibility. If not eligible,
  use the plan's safer restricted-country fallback until tax registration is
  ready.
- Hosting/domain/email: the plan assumes `octadock.com`, `api.*`, and `mail.*`
  style infrastructure. The actual domain/provider decisions still need to be
  executed.
- Admin hosting/auth: Cloudflare Access + WebAuthn is a reasonable default, but
  it still needs a concrete deployment target once the license service stack is
  chosen.
- First implementation batch: items 1 to 3 are founder/procurement actions, not
  code tasks. They should start before the coding sprint.

## Decision

Proceed.

Use `0_TO_100_MASTER_EXECUTION_PLAN.md` as the source of truth for execution.
The next step is not another strategy pass. The next step is to turn the First
Implementation Batch into concrete tasks and start with the long-lead actions:

1. Signing identity validation.
2. DNS and mail subdomain setup.
3. Stripe Managed Payments/tax eligibility decision.
4. Self-contained signed installer work.
5. Copy/privacy fixes.
6. Model-download consent.
7. License-service scaffold.
8. Entitlement envelope and device identity spec.
9. SafeFileWriter.
10. Trial clock and gate matrix.

## Sources Checked

- Microsoft SmartScreen reputation:
  https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- Microsoft code signing options:
  https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- Stripe webhook signature verification:
  https://docs.stripe.com/webhooks/signature
- Stripe Tax for AI:
  https://docs.stripe.com/tax/ai
- Stripe Managed Payments changelog:
  https://docs.stripe.com/payments/managed-payments/changelog

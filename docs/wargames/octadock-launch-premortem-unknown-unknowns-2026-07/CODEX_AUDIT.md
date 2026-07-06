# Codex Audit - Launch Pre-Mortem / Unknown-Unknowns Result

Date: 2026-07-06

Source artifact reviewed:

- `.claude/worktrees/pensive-cray-31e0af/docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/WARGAME_RESULT.md`

## Verdict

Use this Fable result as the current launch-readiness baseline. It is the strongest pass so far because it does not merely produce a longer roadmap; it identifies concrete launch failure modes, maps them to repo evidence, and converts them into no-go criteria, admin alerts, support runbooks, and a corrected implementation sequence.

The result's central conclusion is sound: Octadock is not paid-beta ready yet. The local app has meaningful built capability, but the commercial launch system is still mostly unbuilt: website/download host, signed installer, Stripe webhook-backed license service, key delivery, activation UI, trial gate, legal/privacy pages, minimal admin, and launch alerts.

## What I Spot-Checked

- `main` is clean and synced before importing the result.
- The result was produced as an untracked file in the `pensive-cray-31e0af` worktree.
- The result follows the requested `OUTPUT_TEMPLATE.md` structure.
- The repo has no server-side Stripe/webhook/license-service implementation outside planning docs.
- The repo has no web project or download/pricing site implementation.
- Release packaging is still zip-based and documented as framework-dependent; there is no signing step visible in `build/release.ps1`, project files, or CI.
- The code really does contain the trust-sensitive paths Fable highlighted:
  - HuggingFace model fetch in `ParakeetModelStore.cs`.
  - "fully offline" dictation tooltip in `DockPill.cs`.
  - "local AI" read-aloud/explain language in `ReadAloudService.cs`.
  - env-var-only OpenAI/ElevenLabs BYO key paths.
- Official Stripe docs confirm that Managed Payments reached general availability on April 22, 2026, that eligible AIaaS tax codes were added June 2, 2026, and that Stripe recommends verifying webhook signatures with the `Stripe-Signature` header and `constructEvent()`.
- Stripe Customer Portal docs emphasize account/subscription/invoice management, so Fable's "prove one-time buyer portal behavior before relying on it" warning is reasonable.

## Most Important Takeaways

1. Signing and distribution must move to the front of the plan.

   A paid beta cannot depend on an unsigned, zip-based, framework-dependent Windows build. The pass correctly treats installer/signing/SmartScreen reputation as a launch funnel blocker, not polish.

2. Stripe is still fine, but the Stripe swap changed the launch obligations.

   We need a live Stripe decision record for tax posture, Managed Payments eligibility, Stripe Tax scope, or restricted launch geography. This is not only an engineering integration question.

3. The admin panel needs to be smaller but earlier.

   Fable is right that the current admin spec is too large for P0. Paid beta needs a minimal launch-health panel: purchase-to-license reconciliation, webhook staleness, email delivery, activation failures, refunds/disputes, license lookup, resend, deactivate, revoke/reinstate, and audit rows. Role-heavy dashboards can wait.

4. The license path needs a hard no-go bar.

   Before paid beta, a real purchase must create exactly one license, render a key on the success page or reliable email, activate from manual key entry, survive duplicate webhooks, reject spoofed webhooks, and revoke/refund correctly.

5. The privacy copy must be narrowed before launch.

   "Nothing leaves your PC unless you send it" is too broad while model downloads, cloud explain routes, OpenAI/ElevenLabs BYO paths, activation, and future diagnostics exist. Safer copy should be scoped to captures and explicit user-triggered cloud routes.

6. Context, PDF/Office/archive preview, and file editing claims must be kept out of paid-beta checkout copy until shipped.

   The result correctly flags Context as planned, not implemented. It also catches that "open every file type" needs honest fallback wording.

7. The next implementation sequence should start with long-lead and trust work.

   Recommended first moves: signing identity, Stripe Managed Payments/tax decision, DNS/mail setup, static download page, release packaging, license service envelope/API, manual key entry, trial gate matrix, and minimal admin alerts.

## Caveats

- Some legal/tax conclusions are risk findings, not legal advice. Treat them as launch blockers that need founder/legal/accounting confirmation.
- SmartScreen/EDR behavior must be proven by real signed/unsigned release testing. The repo evidence supports the risk, but only a release-candidate test can quantify it.
- OCR/package-identity and mixed-DPI capture claims need clean-VM/manual hardware verification.
- Stripe Customer Portal one-time purchase behavior should be tested in the actual Stripe account before we design around it.

## Recommended Next Step

Create a founder decision record that supersedes conflicting docs and locks:

- tax/MoR posture;
- signing/distribution plan;
- support email provider and domain;
- device identity definition;
- beta buyer update entitlement;
- post-expiry verb matrix;
- Context/PDF/Office launch copy scope;
- minimal admin P0 scope;
- privacy/network-egress copy.

Then convert the Fable P0 checklist into implementation tickets.

## Official Sources Checked

- Stripe Managed Payments changelog: https://docs.stripe.com/payments/managed-payments/changelog
- Stripe webhook signature verification: https://docs.stripe.com/webhooks/signature
- Stripe Customer Portal: https://docs.stripe.com/customer-management
- Stripe pricing: https://stripe.com/pricing

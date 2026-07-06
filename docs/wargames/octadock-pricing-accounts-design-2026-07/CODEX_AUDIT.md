# Codex Audit - Pricing, Accounts, Gates, and Design Wargame

Date: 2026-07-06

Source artifact reviewed:

- `.claude/worktrees/beautiful-babbage-03aef3/docs/wargames/octadock-pricing-accounts-design-2026-07/WARGAME_RESULT.md`

## Verdict

Use the Fable pass as the working foundation, but do not treat it as final until the payment-provider correction below is resolved. The product strategy, account minimization, entitlement model, Pro waitlist, BYO-key posture, and design-system direction are strong. The biggest issue is that the Stripe Managed Payments analysis is stale/misread against current official Stripe docs.

## What Is Strong

- The output covers every required section from `OUTPUT_TEMPLATE.md`: pricing snapshot, packaging, account/payment flows, entitlement states, account requirement matrix, feature gates, metering, provider recommendation, entitlement architecture, full design system, homepage corrections, risks, backlog, and open questions.
- The recommendation to keep v1 local-first and avoid first-party accounts is sound. "No account" should mean no password/profile/sign-in system, not no license identity.
- The Local license path is coherent: $49 paid beta, $59 at 1.0, 12 months of updates, optional $19/year update renewal, app keeps working after update entitlement ends.
- Pro should remain waitlisted until hosted metering, quota enforcement, dunning, top-ups, and cloud-send consent are built.
- BYO keys should be available to every tier and stored with Windows Credential Manager/DPAPI rather than SQLite/env-only settings.
- The design system is concrete enough to implement: exact colors, semantic tokens for WPF and web, contrast targets, typography, spacing/radius/elevation/motion, Lucide icon migration, surface-by-surface rules, and a token migration plan.
- The design claims are grounded in the repo. Current code really does use `Octadock.Brush.Surface`, `SurfaceAlt`, `Accent`, `TextMuted`, `FocusRing`, `Corner.Large`, Segoe MDL2 glyphs, and a shared `ToolTip` style in `src/Octadock.App/Resources/Themes/Shared.xaml`.
- It correctly preserves the dropped AI discovery decision. Future AI context is gated as not-v1/waitlist language, not a shipped claim.

## Required Corrections Before Finalizing

1. Stripe Managed Payments is no longer a "preview later" item.

   Official Stripe docs now say Managed Payments reached general availability on April 22, 2026, and gained more eligible AIaaS tax codes on June 2, 2026. The Fable result says to revisit when Stripe Managed Payments exits preview; that is stale as of this audit.

2. Stripe Managed Payments pricing is not "3.5% total".

   Stripe's pricing page describes Managed Payments as 3.5% per successful Managed Payments transaction in addition to Payments fees. Fable's wording implies a standalone MoR rate, which underestimates cost and changes the provider comparison.

3. Paddle is still a plausible v1 default, but not fully proven.

   Paddle's 5% + 50c MoR fee and under-$10 custom-pricing note are verified. The recommendation is reasonable for a solo Windows app, but before checkout implementation we need a short P0 vendor decision table comparing Paddle, Stripe Managed Payments, and Lemon Squeezy for the actual Octadock SKUs: $49 beta, $59 local, $19 renewal, $10 Pro, and $5/$10 top-ups.

4. Lemon Squeezy risk should be softened from "verified platform sunset" to "strategic platform risk".

   Lemon Squeezy pricing and License API pages are live and its license API remains a real advantage. Stripe acquisition/successor risk is still relevant, but the final plan should not overstate it as an official shutdown unless a current official source says so.

5. The license service is account-adjacent and needs privacy/legal wording.

   Even without first-party accounts, Octadock will store purchase email, license keys, activation/device hashes, revocation state, and resend logs. The product can still say "no sign-in required", but legal/privacy docs must describe license-service data retention, support access, device deactivation, refund/revocation, and deletion requests.

6. Pro economics need a measured beta checkpoint.

   The 300 transcription minutes/month and 20k premium TTS characters/month are plausible, but they assume average usage, not worst-case usage. Before selling Pro, beta should provide either explicit opt-in usage telemetry, local-only aggregate diagnostics, or support/manual sampling that does not conflict with the "no behavioral telemetry" posture.

## Missing Addendum Needed

Create a small `PAYMENT_PROVIDER_DECISION.md` before implementation starts. It should include:

- Net received for $49, $59, $19, $10, $5, and $10 SKUs under Paddle, Stripe standard, Stripe Managed Payments, and Lemon Squeezy.
- Whether each provider supports one-time licenses, subscriptions, renewals, tax/MoR, customer portal, usage/top-up SKUs, refunds, chargebacks, license keys, webhook reliability, and account-less checkout.
- Country/eligibility constraints for Stripe Managed Payments, including digital app/AIaaS tax code eligibility.
- Engineering impact: custom license service required, webhook types, migration risk, support workflow, and provider lock-in.
- A yes/no recommendation for paid beta checkout.

## Implementation Defaults To Keep Unless Overruled

- Public free 14-day trial, no account and no card, with one 7-day extension.
- Local license: $49 beta, $59 at 1.0, 3 devices, 12 months of updates, $19/year optional update renewal, indefinite offline validity after activation.
- No first-party auth in v1. Use license key + purchase email + provider portal.
- Pro is a waitlist in paid beta. No Pro checkout until metering and dunning exist.
- BYO-key cloud use is available to all tiers and clearly labeled as user-vendor billing.
- Cloud-send confirmation is mandatory before any cloud route.
- "Obsidian Instrument" design direction, with teal for local interaction and violet reserved only for cloud/Pro/commercial surfaces.
- Capture Shelf and Context remain separate surfaces. Context should be titled "Context", not "Context Shelf", and should stay local/export-focused until AI context exists.

## Next Work Order

1. Write and ratify `PAYMENT_PROVIDER_DECISION.md`.
2. Convert the design tokens into WPF resources and matching web CSS variables.
3. Implement Account & Billing shell without auth: trial status, key entry, device list placeholder, update entitlement card, Pro waitlist.
4. Build the license-service spec/API before writing the service.
5. Move BYO keys from env-only behavior toward Credential Manager with explicit test coverage that secrets never enter logs or SQLite.
6. Turn the design-system surface rules into implementation tickets for the dock, shelf rows, context panel, pin viewer, history/library, preview, annotation editor, settings, and homepage.

## Official Sources Checked

- OpenAI API pricing: https://developers.openai.com/api/docs/pricing
- ElevenLabs API pricing: https://elevenlabs.io/pricing/api
- ElevenLabs plan pricing: https://elevenlabs.io/pricing
- Paddle pricing: https://www.paddle.com/pricing
- Stripe pricing: https://stripe.com/pricing
- Stripe Managed Payments changelog: https://docs.stripe.com/payments/managed-payments/changelog
- Stripe Managed Payments checkout migration docs: https://docs.stripe.com/payments/managed-payments/update-checkout
- Lemon Squeezy pricing: https://www.lemonsqueezy.com/pricing
- Lemon Squeezy License API: https://docs.lemonsqueezy.com/api/license-api
- CleanShot pricing: https://cleanshot.com/pricing
- TechSmith Snagit store: https://www.techsmith.com/store/snagit
- ShareX homepage: https://getsharex.com/

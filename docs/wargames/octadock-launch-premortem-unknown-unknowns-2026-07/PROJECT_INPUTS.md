# Project Inputs

Read the files below before running the pass. Treat docs as evidence, not truth. If a doc conflicts with code, call it out.

## Current Planning Packet

- `docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/README.md`
- `docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/FABLE_PROMPT.md`
- `docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/WARGAME_PLAN.md`
- `docs/wargames/octadock-launch-premortem-unknown-unknowns-2026-07/OUTPUT_TEMPLATE.md`

## Locked Commercial / Launch Decisions

- `docs/wargames/octadock-pricing-accounts-design-2026-07/PAYMENT_PROVIDER_DECISION.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/TRIAL_AND_PURCHASE_FLOW.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/ADMIN_PANEL_SPEC.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/CODEX_AUDIT.md`

## Prior Wargame Results

- `docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md`
- `docs/wargames/octadock-ui-files-context-2026-07/IMPLEMENTATION_PLAN.md`
- `docs/wargames/octadock-ui-files-context-2026-07/PROJECT_CONTEXT.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/WARGAME_RESULT.md`
- `docs/wargames/octadock-pricing-accounts-design-2026-07/PROJECT_INPUTS.md`

## Research / Product / Design

- `docs/PROJECT-STATE.md`
- `docs/CAPABILITIES.md`
- `docs/ROADMAP.md`
- `docs/design/UI-INVENTORY.md`
- `docs/design/HOMEPAGE-CONCEPT-2026-07-06.md`
- `docs/research/LAUNCH-PRICING-RESEARCH-2026-07-06.md`
- `docs/brand/README.md`

## Specs / Architecture

- `docs/specs/prd.md`
- `docs/specs/architecture-adr.md`
- `docs/specs/api-and-data-spec.md`
- `docs/specs/implementation-backlog.md`
- `docs/specs/octadock-build-plan.md`
- `docs/ARCHITECTURE.md`
- `docs/TESTING.md`
- `docs/VERSIONING.md`

## Design Skills / Agent Guidance

- `CLAUDE.md`
- `.claude/skills/impeccable/SKILL.md`
- `.claude/skills/emil-design-eng/SKILL.md`
- `.claude/skills/review-animations/SKILL.md`
- `.claude/skills/animation-vocabulary/SKILL.md`

These are not product requirements. They are design-review aids and may help critique the web/frontend direction.

## Code Areas To Inspect For Reality Checks

Do not read every line unless needed. Inspect these areas when a claim depends on implementation reality:

- `src/Octadock.App/`
- `src/Octadock.Core/`
- `src/Octadock.Platform.Windows/`
- `src/Octadock.Cli/`
- `src/Octadock.App/Resources/Themes/Dark.xaml`
- `src/Octadock.App/Resources/Themes/Light.xaml`
- `src/Octadock.App/Resources/Themes/Shared.xaml`
- `src/Octadock.App/CaptureUx/`
- `src/Octadock.App/Preview/`
- `src/Octadock.App/Editing/`
- `src/Octadock.App/Settings/`
- `src/Octadock.Platform.Windows/Stt/`
- `src/Octadock.Platform.Windows/Tts/`

## External Sources To Re-Verify If Browsing Is Available

These are unstable and should be checked live before making claims:

- Stripe pricing: `https://stripe.com/pricing`
- Stripe Checkout: `https://docs.stripe.com/payments/checkout`
- Stripe Customer Portal: `https://docs.stripe.com/customer-management`
- Stripe Managed Payments changelog: `https://docs.stripe.com/payments/managed-payments/changelog`
- OpenAI API pricing: `https://developers.openai.com/api/docs/pricing`
- ElevenLabs pricing/API pricing: `https://elevenlabs.io/pricing`, `https://elevenlabs.io/pricing/api`
- CleanShot pricing: `https://cleanshot.com/pricing`
- Snagit pricing/store: `https://www.techsmith.com/store/snagit`
- ShareX homepage: `https://getsharex.com/`

If browsing is not available, mark all current pricing/compliance claims as unverified and list them as assumptions.


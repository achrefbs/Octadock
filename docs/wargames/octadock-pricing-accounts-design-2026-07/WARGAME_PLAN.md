# Wargame Plan

## Objective

Pressure-test Octadock's launch business model, account system, feature gates,
hosted usage model, homepage positioning, and design system before execution.

The output should answer:

- What do we sell?
- What is free/trial/local/pro/team?
- What requires an account?
- What consumes cloud credits?
- What can use BYO-key?
- What should the app look like everywhere?
- What design tokens must engineering implement?

## Roles

- Founder/Product Owner: protects strategy and scope.
- Indie Desktop Software Buyer: tests willingness to pay and account friction.
- Windows Power User: tests local-first workflows and offline expectations.
- Privacy-First User: challenges cloud sends, telemetry, accounts, and package
  export.
- Pricing Strategist: checks packaging, price points, and competitive position.
- Payments/Tax Operator: challenges Lemon Squeezy, Paddle, Stripe, refunds,
  taxes, chargebacks, subscriptions, renewals.
- Entitlement Engineer: challenges license activation, offline grace, device
  limits, BYO-key, Pro gates, and Team extensibility.
- Account/Auth Engineer: challenges sign-in, account creation, passwordless,
  license recovery, token storage, and offline behavior.
- Support Lead: challenges refund requests, confused upgrades, lost licenses,
  expired renewals, account lockouts, and entitlement edge cases.
- Brand Designer: challenges homepage story, octopus symbol, palette, and
  product coherence.
- Product Designer: challenges app-wide interaction patterns, density, and
  component reuse.
- Accessibility Reviewer: challenges contrast, keyboard, reduced motion,
  high-contrast mode, and screen reader labels.
- Marketing Copy Reviewer: challenges claims, overpromises, and pricing clarity.

## Scenarios

### 1. Paid Beta or Waitlist

The founder wants to launch soon. Decide whether v1 should be waitlist-only,
paid beta, Local-license checkout, or free trial.

Stress:

- Does paid beta create support burden before core UI polish lands?
- Does waitlist delay learning?
- What should the homepage CTA say?
- Should exact pricing appear before product proof?

### 2. Local License Purchase

A Windows power user buys Octadock for local capture, pins, annotation, OCR,
speech, and file preview.

Stress:

- Is an account required?
- Can the app activate offline after purchase?
- How many devices?
- What happens after update entitlement expires?
- Which features must never be gated behind Pro?

### 3. Pro Subscription

A user subscribes to Pro for cloud transcription, premium voices, summaries, and
future AI context features.

Stress:

- Which features actually exist in v1?
- What usage quotas are included?
- What happens when credits run out?
- How should cloud sends be made explicit?
- What cancellation behavior is fair?

### 4. BYO-Key Power User

A user owns their OpenAI or ElevenLabs key and wants to avoid Octadock reselling
cloud usage.

Stress:

- Is BYO-key free, Local-only, Pro-only, or paid-plan-only?
- Does BYO-key require an Octadock account?
- How does the UI separate BYO usage from Octadock Pro credits?
- What support burden does BYO create?

### 5. Account Requirement

A privacy-first buyer wants the local app with no sign-in.

Stress:

- What can work fully offline?
- What requires account/auth?
- What requires internet but no account?
- Does license recovery require email/account?
- How does Octadock avoid feeling like cloudware?

### 6. Usage Metering and Abuse

A Pro user uploads hours of cloud transcription or premium TTS usage.

Stress:

- What exact meter events must be tracked?
- What hard limits, soft warnings, and top-ups exist?
- How do BYO-key and Pro credits interact?
- How do we prevent runaway vendor bills?

### 7. Payment Provider Choice

Compare Lemon Squeezy, Paddle, and Stripe for launch.

Stress:

- Tax compliance.
- Subscriptions.
- One-time licenses.
- License keys and device activations.
- Usage credits/top-ups.
- Refunds and chargebacks.
- Future Team plan.

### 8. Feature Gate Matrix

Map every major feature to Free/Beta, Local License, Pro, BYO-key, Team Later,
and Not In V1.

Stress:

- Is the gate fair?
- Is the gate easy to explain?
- Does the implementation need entitlements from day one?
- Are promised features actually built?

### 9. Homepage Messaging

Use the homepage concept to test the public product story.

Stress:

- Is "Octadock" enough as H1?
- Does the octopus symbol feel premium or distracting?
- Does pricing language overpromise cloud/AI?
- Should Context be "coming soon"?
- Does the page explain why this is worth paying for when ShareX is free?

### 10. Design System Lock

Define the final app/website design system.

Stress:

- Are colors specific enough to implement?
- Does the palette pass contrast?
- Does it avoid a one-note teal-on-navy look?
- Does it work in WPF and web?
- Does it have reduced-transparency, high-contrast, and light-mode answers?
- Are radii, spacing, type, elevation, icons, and motion specified?

### 11. App-Wide Component Language

Apply the design system to Dock, Capture Shelf, Context, History, File Preview,
Annotation Editor, Settings, HUD, and Homepage.

Stress:

- Which components are canonical?
- Which existing UI should be deprecated?
- Are cards, panels, rows, action rails, tabs, inspectors, and buttons
  consistently defined?
- Are icons from one family?

### 12. Launch Plan Integrity

Combine business and design into a final launch plan.

Stress:

- Is v1 sellable without overpromising?
- Are the gates implementable?
- Are pricing and design aligned with positioning?
- What must be built before paid beta?
- What must be delayed until Pro/cloud infrastructure exists?

## Required Outputs

Use `OUTPUT_TEMPLATE.md`.

The result must include:

- verified pricing table;
- recommended launch packaging;
- account requirement matrix;
- feature gate matrix;
- hosted usage meters and quota recommendation;
- payment provider recommendation;
- entitlement architecture requirements;
- final design token proposal;
- component/system rules;
- homepage messaging corrections;
- P0/P1 backlog changes.


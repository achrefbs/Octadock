# Prompt to Paste Into Fable

You are running a second deep wargame for Octadock.

Use the repository I provide as your source packet. Read this folder first, then
read every source file referenced by `PROJECT_INPUTS.md`.

Your task is to pressure-test and solidify two things in a way engineering and
design can execute directly:

1. A **payments, accounts, licenses, entitlements, and usage-gates product
   specification**.
2. A **complete Octadock app + website design-system blueprint**.

Do not answer with vague strategy. Produce concrete decisions, matrices,
flows, UI states, design tokens, and implementation constraints.

Do not redo the first feature wargame unless it affects pricing, accounts,
gates, or design. Build on the prior result in
`docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md`.

Important constraints:

- Octadock is Windows-first and local-first.
- Local capture, shelf, annotation, pins, local history, local OCR, local
  speech/dictation where available, read aloud, file preview, and local tools
  should feel usable without cloud dependence.
- Hosted AI, cloud transcription, premium voices, future hosted sharing, and
  team/admin features need metering, entitlements, and probably accounts.
- BYO-key is important for power users.
- Do not sell unlimited hosted AI inside a one-time license.
- Do not resurrect AI discovery or AI Sessions.
- The visual system must cover the desktop app and homepage, with mapped token
  names for WPF and web CSS variables.
- The visual direction is dark obsidian glass with teal/cyan accents, but it
  must not become a one-note dark-blue/teal product. Add enough neutral,
  semantic, and secondary accent structure to make the app readable at scale.

## Required Account / Payment / Gate Work

You must draft the actual product behavior for accounts and payments, not just
say "verify pricing".

Define:

- Whether Octadock launches as waitlist-only, free trial, paid beta, Local
  license, Pro subscription, or a combination.
- Whether a user can install, trial, activate, and use the local app without an
  account.
- Which actions require no account, a Local license, Pro, BYO-key, or Team later.
- Exact entitlement states: trial, active local license, expired update
  entitlement, Pro active, Pro past due, Pro canceled, offline grace, license
  revoked, device limit reached, BYO-key configured, credits exhausted.
- User flows for:
  - first install;
  - trial start;
  - Local license purchase;
  - activation on a second device;
  - offline usage;
  - Pro upgrade;
  - Pro cancellation;
  - hosted credits exhausted;
  - BYO-key setup;
  - refund/revocation;
  - account recovery.
- UI surfaces needed:
  - first-run activation screen;
  - Settings > Account & Billing;
  - Settings > Cloud Providers / BYO Keys;
  - usage meter;
  - upgrade modal/banner;
  - cloud-send confirmation;
  - license expired/update renewal state;
  - no-internet/offline state.
- What data must be stored locally, what must be stored server-side, and what
  events must be metered.
- What must be implemented before any paid beta.

## Required Design-System Work

The design answer must be a real design system, not mood language.

Produce:

- A named visual direction for Octadock.
- Brand principles for the app and homepage.
- Primitive color palette with exact hex values.
- Semantic color tokens with exact WPF token names and web CSS variable names.
- Dark theme and light theme values.
- High-contrast and reduced-transparency rules.
- Contrast expectations for primary text, secondary text, metadata, borders,
  disabled text, focus rings, and accent text.
- Typography ramp with font, size, weight, line-height, and usage.
- Spacing scale and layout grid.
- Radius scale for dense rows, panels, dialogs, dock capsules, and marketing
  surfaces.
- Border, shadow, blur, acrylic/glass, and elevation rules.
- Motion rules: durations, easing, hover/press/selection transitions, panel
  entrance, reduced-motion fallback.
- Iconography decision: one icon family or explicit mapping from current WPF
  glyphs to the chosen system.
- Component specs for:
  - Dock capsule;
  - bottom action dock;
  - Capture Shelf row;
  - Context row/panel;
  - action rail;
  - pin/media viewer;
  - file preview surface;
  - history/library card and inspector;
  - capture HUD tile;
  - annotation toolbar;
  - settings sidebar/tab;
  - account/billing page;
  - pricing card;
  - cloud/Pro gate banner;
  - usage meter;
  - toast/notification;
  - modal/dialog.
- States for each major component:
  - default;
  - hover;
  - pressed;
  - focus;
  - selected;
  - disabled;
  - loading;
  - error;
  - warning;
  - gated/locked;
  - offline;
  - quota exhausted;
  - synced/saved.
- Surface-by-surface application rules for the app:
  - Dock;
  - Capture Shelf;
  - Context;
  - History;
  - File Preview;
  - Pin Viewer;
  - Annotation Editor;
  - Settings;
  - Account/Billing;
  - Capture HUD;
  - Homepage.
- A migration plan from current tokens in `Dark.xaml`, `Light.xaml`, and
  `Shared.xaml` to the final system.

You must explicitly say what to keep, what to replace, and what new tokens or
components engineering must add.

Before returning the final result:

- Verify public pricing using official pricing pages listed in
  `LIVE_PRICING_SOURCES.md`.
- Mark any pricing value as "verified", "ambiguous", or "needs owner decision".
- If browsing is not available, explicitly say the pricing was not verified and
  treat the repo research as untrusted.

Run the wargame with these roles:

- Founder/Product Owner
- Indie Desktop Software Buyer
- Windows Power User
- Privacy-First User
- Pricing Strategist
- Payments/Tax Operator
- Entitlement Engineer
- Account/Auth Engineer
- Support Lead
- Brand Designer
- Product Designer
- Accessibility Reviewer
- Marketing Copy Reviewer

Return the result using `OUTPUT_TEMPLATE.md`.

The highest-value output is a final decision matrix plus an implementable design
system:

- Which features require no account.
- Which features require a local license.
- Which features require Pro.
- Which features consume cloud credits.
- Which features allow BYO-key.
- Which features should be unavailable until the user explicitly opts into cloud
  usage.
- Which account/payment screens and backend states must exist before paid beta.
- Which colors, type, spacing, radius, elevation, icon, motion, and component
  rules every app surface must use.
- Which current UI patterns must be deprecated.

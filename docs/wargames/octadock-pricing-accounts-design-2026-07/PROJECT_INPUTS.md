# Project Inputs

## New Research Added by Founder

Read these first:

- `docs/research/LAUNCH-PRICING-RESEARCH-2026-07-06.md`
- `docs/design/HOMEPAGE-CONCEPT-2026-07-06.md`

The pricing research proposes:

- one-time Local license;
- Pro subscription for hosted usage and premium services;
- BYO-key for power users;
- Team/Business later;
- beta checkout around a USD 59 Local license and USD 9-12/month Pro.

The homepage concept proposes:

- product positioning as a Windows command surface for turning screen content
  into useful context;
- a premium 3D translucent blue/cyan octopus visual;
- local-first messaging;
- clear pricing story: "Start local. Add Pro when you want cloud accuracy and AI
  context."

## Prior Wargame Result

Read:

- `docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md`

Important outputs from the first pass:

- V1 writeback should be images only through a revision layer.
- Context items need ownership semantics.
- Explorer bulk behavior needs a realistic v1/v2 split.
- Universal file fallback needs executable/script guards.
- Context exports need preview/redaction controls.
- The Context Shelf may need a better user-facing name, possibly just
  "Context".
- Command Deck redesign should move later.

This second pass should incorporate those results into pricing and gates. For
example, do not charge for PDF writeback in v1 if PDF writeback is not actually
in v1.

## Current App Design Context

Read:

- `docs/design/UI-INVENTORY.md`
- `docs/brand/README.md`
- `src/Octadock.App/Resources/Themes/Dark.xaml`
- `src/Octadock.App/Resources/Themes/Light.xaml`
- `src/Octadock.App/Resources/Themes/Shared.xaml`

Current token facts:

- Dark default theme:
  - base surface: `#0C1220`
  - raised surface: `#141E32`
  - border: `#25334E`
  - text: `#F2F6FC`
  - muted text: `#8DA0BC`
  - accent: `#2DD4BF`
  - accent gradient end: `#38BDF8`
- Light theme exists:
  - base surface: `#F3F6FB`
  - raised surface: `#FFFFFF`
  - border: `#DAE2EE`
  - text: `#0D1526`
  - muted text: `#5B6B84`
  - accent: `#0F766E`
- Current app uses Segoe UI Variable / Segoe UI and a 20/16/14/13 px type ramp.
- Current shared card radius includes 12 px, but the redesign should decide
  where to tighten radii for dense utility surfaces.
- Some preview-card styling still has hardcoded colors in code.
- The design inventory says the app needs a full token sheet, icon set, and
  per-window mockups.

## Design References

Read/inspect:

- `docs/wargames/octadock-ui-files-context-2026-07/reference-images/`
- `docs/design/ui/`

Design direction:

- dark glass utility UI;
- teal/cyan as primary accent;
- dense but organized operational surfaces;
- row-first shelf, not oversized cards;
- inspector panels and metadata strips;
- no AI Sessions panels;
- no generic marketing card clutter;
- website hero can use a premium octopus asset, but app UI should remain a
  serious Windows utility.

## Current Product Scope Assumptions

Use these defaults unless the wargame argues against them:

- Free/waitlist build may exist during beta, but final commercial shape should
  include a Local license and Pro subscription.
- Local app should keep working if update renewal expires.
- Pro should be required for Octadock-hosted cloud usage.
- BYO-key should not consume Octadock credits.
- Accounts should be required for Pro and license activation if the final
  payment provider requires it, but account-free local trial should be evaluated.
- Team plans are not v1, but entitlement schema should not block them later.


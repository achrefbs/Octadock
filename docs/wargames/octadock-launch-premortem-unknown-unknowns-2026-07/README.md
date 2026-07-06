# Octadock Launch Pre-Mortem / Unknown-Unknowns Packet

Created: 2026-07-06

## Purpose

This packet is for a final deep Fable pass before implementation planning turns into execution.

The task is not to admire the current plan. The task is to find what could make Octadock fail even if the current plan looks coherent.

Use this packet to run a launch pre-mortem, blind-spot hunt, and repair plan for:

- paid beta launch;
- Stripe checkout and license service;
- free trial and Local license conversion;
- admin panel readiness;
- local-first privacy promises;
- UI/files/context implementation;
- account/billing/support operations;
- app and website design-system rollout;
- future Pro/cloud/team extensibility.

## Read Order

1. `FABLE_PROMPT.md`
2. `PROJECT_INPUTS.md`
3. `WARGAME_PLAN.md`
4. `OUTPUT_TEMPLATE.md`

Then read every referenced source file in `PROJECT_INPUTS.md`.

## Expected Output

Fable should return one document using `OUTPUT_TEMPLATE.md`.

The result should include:

- critical unknown unknowns;
- assumptions that need proof;
- 10-15 launch failure scenarios;
- risk register;
- customer promise map;
- admin/readiness gaps;
- no-go criteria;
- mitigation plan;
- revised sequencing and backlog corrections.

## Current Locked Decisions

These are not sacred, but they are the current working decisions. Challenge them only if the challenge is material.

- Stripe is the selected payment provider.
- Download is free; trial starts on first app run.
- Paid beta has no permanent free tier.
- Free trial is 14 days, full local app, no account, no card.
- Local license is `$49` beta, `$59` at 1.0, one-time payment, 3 devices, 12 months updates.
- Pro is waitlist-only until hosted metering, quota, top-up, and dunning infrastructure exists.
- No first-party accounts in v1; use license key + purchase email + Stripe portal.
- Admin panel is P0 for paid beta readiness.
- Local-first and no-silent-upload promises are core brand constraints.
- Capture Shelf and Context are separate surfaces.
- AI discovery / AI Sessions remain dropped and out of scope.


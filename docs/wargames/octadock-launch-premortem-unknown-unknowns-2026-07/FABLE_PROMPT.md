# Prompt to Paste Into Fable

You are running a deep launch pre-mortem and unknown-unknowns wargame for Octadock.

Use the repository I provide as your source. First read this folder, then read every file referenced by `PROJECT_INPUTS.md`.

Your task is not to repeat the roadmap. Your task is to find the failure modes that are not obvious yet.

Assume the team believes the current plan is good. Your job is to be the uncomfortable second brain:

- Find contradictions.
- Find hidden dependencies.
- Find operational gaps.
- Find privacy/copy promises that could become dangerous.
- Find support nightmares.
- Find launch day breakpoints.
- Find places where the docs sound confident but the implementation may not support them.
- Find what we do not know that we do not know.

Then repair the plan.

## Current Context

Octadock is a Windows-first local desktop utility for capture, shelf, pins, annotation, history, OCR, speech, read-aloud, file preview, and Context packaging/export.

Current plan:

- free download;
- 14-day full local trial on first run;
- no account and no card for trial;
- Local license sold through Stripe Checkout;
- `$49` paid beta, `$59` at 1.0;
- app keeps working after update entitlement ends;
- no permanent free tier in paid beta;
- no first-party accounts in v1;
- license key + purchase email + Stripe Customer Portal;
- Pro waitlist only until hosted metering exists;
- admin panel before paid beta;
- local-first privacy promise;
- no AI discovery / AI Sessions.

## Method

Run the pass in two phases.

### Phase 1: Break The Plan

Be adversarial and concrete. Do not solve yet. Produce a hard failure inventory.

Use these techniques:

- Pre-mortem: "It is 90 days after launch and Octadock failed. Why?"
- Inversion: "What would we do if we wanted launch to fail?"
- Assumption inversion: "What if the opposite of this assumption is true?"
- Dependency tracing: "What hidden service/person/process must work?"
- P95 user stress: weird Windows installs, corporate machines, antivirus, DPI, offline laptops, clock skew, non-US buyers, typoed email, lost license, second device, refund, bad update.
- Promise audit: every public claim must map to product proof.
- Support simulation: predict the tickets before they happen.
- Admin visibility audit: if something breaks, can the founder see it without guessing?

### Phase 2: Repair The Plan

For each critical failure mode, create concrete mitigations:

- implementation change;
- product copy change;
- admin panel metric or alert;
- support runbook;
- no-go launch criterion;
- owner decision;
- backlog sequencing change.

## Roles To Simulate

Use these roles and make them disagree:

- Skeptical Founder: protects cash, speed, and scope.
- First-Time Buyer: asks "what is this and why pay?"
- Windows Power User: stresses local workflows, keyboard, Explorer, offline, multi-monitor, DPI.
- Privacy-First Buyer: attacks "local-first", BYO keys, diagnostics, admin stats, no account claims.
- Launch Operator: worries about download, installer, signing, Stripe, webhooks, email, support, refunds.
- License/Entitlement Engineer: attacks activation, signed entitlements, device limits, offline grace, update entitlement.
- Admin/Ops Lead: asks "can I see and fix this at launch?"
- Support Lead: predicts tickets and confusion.
- Security Reviewer: attacks admin panel, webhook spoofing, key storage, signing keys, license tampering.
- Legal/Compliance Reviewer: attacks tax, privacy policy, refund terms, no-account wording, telemetry consent.
- Product Designer: attacks UI polish, trial gates, admin panel usability, account/billing states.
- Accessibility Reviewer: attacks keyboard, high contrast, screen reader labels, reduced motion.
- Competitor Analyst: compares ShareX, Snagit, CleanShot, PowerToys, OneNote, Windows Snipping Tool.
- Future-Pro Architect: asks whether today's choices block Pro, Team, hosted AI, or accounts later.

## Hard Constraints

- Do not resurrect AI discovery or AI Sessions.
- Do not propose selling Pro before hosted metering exists.
- Do not propose unlimited hosted AI in the one-time Local license.
- Do not weaken local-first promises just to get prettier metrics.
- Do not collect user screenshots, OCR text, clipboard text, audio, filenames, file paths, context package contents, or BYO keys in the admin panel.
- Do not require account creation for the v1 local app.
- Do not treat docs as truth. If a doc claims something is built, inspect code or mark it unverified.
- If you cannot verify a claim from repo/code/current external source, mark it as an assumption.

## Required Analysis Areas

You must cover:

- download and website first impression;
- installer, signing, SmartScreen, antivirus, and update flow;
- first run, trial start, trial expiry, and purchase timing;
- Stripe checkout, webhooks, refunds, disputes, tax/MoR/Managed Payments, Customer Portal;
- license service, signed entitlements, license email delivery, activation, second device, offline use;
- admin panel P0 readiness and alerting;
- support load and runbooks;
- privacy, diagnostics, telemetry, BYO keys, cloud-send consent;
- local file preview and annotation promises;
- Context package/export promises;
- design-system migration and UI polish risk;
- accessibility and high-contrast risk;
- competitor positioning;
- future Pro/team extensibility;
- launch sequencing and no-go criteria.

## Output Rules

Return the result using `OUTPUT_TEMPLATE.md`.

Be specific. Every major risk must have:

- failure story;
- likelihood;
- impact;
- risk level;
- early warning signal;
- mitigation;
- owner;
- launch gating effect.

Do not stop at "monitor this". Say exactly what to monitor, where it appears, and what action the founder takes.

End with a concise founder decision brief:

- what is ready;
- what is not ready;
- what must be done before paid beta;
- what can wait;
- what decisions the founder must make now.


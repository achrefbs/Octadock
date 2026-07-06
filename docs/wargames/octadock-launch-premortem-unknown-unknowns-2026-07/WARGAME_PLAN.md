# Wargame Plan

## Objective

Find the hidden ways Octadock's paid beta launch can fail, then repair the plan before implementation starts.

The output should answer:

- What could go wrong that is not already obvious?
- What assumptions are carrying too much weight?
- Which claims cannot be safely made yet?
- What needs to be visible in the admin panel?
- What breaks on launch day?
- What should stop us from launching?
- What must be built or changed before paid beta?

## Phase 1: Break The Plan

Run every scenario as if you are trying to disprove the plan.

Rules:

- Be concrete.
- Prefer failure stories over abstract warnings.
- Name hidden dependencies.
- Identify contradictions between docs, code, product copy, and business model.
- Do not offer mitigations until the failure has been stated clearly.

## Phase 2: Repair The Plan

Convert failures into:

- no-go criteria;
- P0 implementation work;
- P1/P2 deferrals;
- admin panel metrics and alerts;
- support runbooks;
- privacy/copy changes;
- product sequencing changes;
- founder decisions.

## Scenarios

### 1. Website To Download

A first-time user lands on the homepage.

Stress:

- Do they understand what Octadock is in 5 seconds?
- Does "free trial" accidentally read as "free forever"?
- Does the pricing story make the one-time Local license feel fair?
- Does the page overpromise Context, file editing, AI, or cloud features?
- Does the design look premium enough to overcome ShareX/PowerToys being free?
- What happens if the download link, code signing, or installer hosting breaks?

### 2. Installer And Windows Trust

A buyer downloads on a normal Windows 10/11 machine.

Stress:

- SmartScreen warning.
- Antivirus false positive.
- Corporate endpoint restrictions.
- Missing runtime dependencies.
- Installer blocked offline.
- Update/certificate expiration.
- Confusion between beta and stable.
- No admin rights.

### 3. First Run And Trial

A user opens Octadock for the first time.

Stress:

- Does the app start the trial without internet?
- Does trial copy respect local-first trust?
- How do we avoid "where do I pay?" confusion?
- What happens with clock skew, reinstall, `%LOCALAPPDATA%` wipe, RDP, VM, or multiple Windows accounts?
- What exact state is stored locally?

### 4. Trial Expiry

The trial ends.

Stress:

- User thinks their data is locked.
- Gate appears during urgent work.
- Existing captures/pins/context files are confusingly view-only.
- User cannot find "enter license key".
- User expects a permanent free tier.
- User gets angry about disabled local features.

### 5. Stripe Checkout

A user clicks Buy Local.

Stress:

- Checkout succeeds but webhook fails.
- Stripe email typo.
- User pays but never receives a license.
- Duplicate checkout creates duplicate licenses.
- Refund/dispute arrives before activation.
- Stripe tax/Managed Payments settings are wrong.
- Customer Portal expectations conflict with no first-party account.
- Non-US buyer sees tax/currency surprises.

### 6. License Activation

A user activates the Local license.

Stress:

- License email delayed.
- User enters wrong key.
- Activation server down.
- Device limit reached.
- Machine hash changes after OS update.
- Offline laptop.
- Revoked/refunded license.
- Signed entitlement file corrupt.
- Support needs to fix it quickly.

### 7. Admin Panel Launch Readiness

It is launch day and something is broken.

Stress:

- Can the founder see downloads, checkout, webhooks, license issue, email delivery, activation, refunds, and disputes in one place?
- Can support resend a key?
- Can support deactivate a device?
- Are sensitive admin actions audited?
- Are there alerts for webhook failures, activation failure spikes, email delivery failures, and refund spikes?
- Are we collecting too little to operate or too much to preserve trust?

### 8. Support Load

First 100 paid users arrive.

Stress:

- Top 25 tickets.
- Which tickets require manual DB access if admin panel is weak?
- Which tickets need prewritten support macros?
- What needs to be in docs/FAQ before launch?
- What can be self-serve?

### 9. Privacy And Trust

A privacy-first buyer reads the homepage and app UI.

Stress:

- "No account required" vs license-service email/device records.
- "No silent uploads" vs crash reports/diagnostics/admin stats.
- BYO keys stored in env vars vs Credential Manager.
- Cloud-send confirmation clarity.
- Admin panel data boundaries.
- Support bundles.

### 10. File Preview / Annotation / Context Promises

A user tries the advanced features that justify paying.

Stress:

- "Open every file type" fallback disappointment.
- PDF/Office editing expectations vs preview/annotate-on-copy reality.
- Image annotation writeback risk.
- Archive handling.
- Unknown file types.
- External file integration and Explorer "Open with Octadock".
- Context export/redaction reliability.

### 11. UI Polish And Design System

The UI redesign starts.

Stress:

- Token migration creates inconsistent old/new UI.
- Shelf card redesign misses short-screenshot edge cases.
- Context and Capture Shelf are confused.
- Dense dashboard surfaces become too decorative.
- Dark glass harms readability/high contrast.
- Icon migration breaks WPF glyph rendering.
- Homepage design does not match app reality.

### 12. Local Speech / OCR / BYO Keys

Users rely on local and BYO speech/OCR.

Stress:

- Local model availability.
- OpenAI/ElevenLabs key errors.
- Secret leakage in logs.
- Provider pricing changes.
- Cloud fallback unclear.
- BYO support burden.
- Offline behavior mismatch.

### 13. Future Pro / Team

The Local beta succeeds and Pro becomes tempting.

Stress:

- Today's schema blocks Pro usage metering.
- No hosted proxy architecture.
- No quota ledger.
- No dunning state.
- No team/org field.
- Local buyers feel cheated by later Pro.
- Subscription "earns local license" rule creates accounting/support ambiguity.

### 14. Competitive Reality

A skeptical user compares Octadock to alternatives.

Stress:

- ShareX is free.
- Snagit is established.
- CleanShot is polished.
- Windows Snipping Tool and PowerToys are free/default.
- OneNote/Teams already capture/annotate enough for some users.
- What exact job does Octadock win?

### 15. Founder Bandwidth

The plan is too large for one founder/team.

Stress:

- Which P0s are truly paid-beta blockers?
- What can be manual at first?
- What cannot be manual because trust or money breaks?
- What should be cut even if desirable?
- Which 3-5 things matter most for beta success?

## Required Outputs

Use `OUTPUT_TEMPLATE.md`.

The highest-value output is not a longer roadmap. It is:

- an assumption ledger;
- a ranked risk register;
- failure scenarios;
- no-go criteria;
- admin metrics/alerts;
- support runbooks;
- revised implementation sequence.


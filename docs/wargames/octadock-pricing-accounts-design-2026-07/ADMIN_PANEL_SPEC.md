# Admin Panel Spec

Date: 2026-07-06

## Decision

Octadock needs a serious admin panel before paid beta. The admin panel is the founder/operator cockpit for launch health, revenue, trials, license activation, support, refunds, release health, and future Pro metering.

It should show **every useful stat we can collect without breaking the local-first promise**.

The guiding rule:

> Collect operational truth, not private user content.

## Problem Statement

The last project had an admin panel with no useful stats, which made launch, support, and business decisions blind. Octadock cannot launch paid beta with Stripe, license activation, trials, downloads, updates, and support all happening in separate black boxes. The admin panel must show whether the business and product are healthy without collecting screenshots, file contents, OCR text, clipboard text, audio, filenames, window titles, or private local activity.

## Goals

- Give the founder a same-day answer to: "Are people downloading, trying, buying, activating, and keeping Octadock?"
- Detect launch problems quickly: failed checkout, failed webhooks, missing license emails, activation errors, update failures, crash spikes, refund/dispute spikes.
- Make support fast: find a customer by email/license/Stripe ID, inspect entitlement state, resend keys, deactivate devices, and see the non-content event timeline.
- Preserve trust: admin stats must align with "local-first", "no sign-in required", and "no silent uploads".
- Prepare for Pro later: hosted usage, quotas, COGS, subscriptions, dunning, and top-ups should slot into the same admin console.

## Non-Goals

- No user content viewer. The admin panel must never show captures, screenshots, OCR text, clipboard contents, audio, file contents, paths, filenames, context packages, or window titles.
- No hidden behavioral surveillance. Detailed app usage telemetry must be opt-in, aggregated, or tied only to explicit support/diagnostic flows.
- No full CRM in v1. Notes and support status are fine; sales pipelines and marketing automation are later.
- No team admin console for customers in v1. This is Octadock internal admin, not a Team-plan customer portal.
- No remote control of user devices. Admin can manage entitlements, not reach into local machines.

## Data Collection Boundaries

### Always Allowed Operational Data

These are required to run the business:

- Stripe customer/session/payment/subscription/refund/dispute IDs.
- Purchase email.
- License key hash and key suffix.
- License state: active, revoked, refunded, update entitlement date, beta lock.
- Activation count, device hash, friendly device name if user provides one, OS/version/build, last entitlement refresh time.
- License-service event timeline: issued, activated, deactivated, refreshed, revoked, resend requested.
- Webhook processing state: received, processed, failed, retried.
- Download counts and installer version counts from the website/CDN.
- Support actions performed by admins.

### Allowed With Explicit User Consent

- Crash reports.
- Diagnostic bundles.
- App health counters such as startup failures, activation failure codes, update failure codes, preview provider errors, OCR provider failures, local STT provider failures.
- Anonymous feature counters only if the privacy page clearly says so and the app setting controls it.

### Never Collect

- Screenshots or capture thumbnails.
- OCR text.
- Clipboard contents.
- Dictation audio or transcripts.
- File contents, filenames, or paths.
- Window titles and app document titles.
- Context package contents.
- BYO API keys or provider responses.
- Raw local logs unless the user explicitly exports/uploads a support bundle.

## Primary Admin Surfaces

### 1. Launch Overview

Purpose: one screen that answers "is the launch alive?"

Stats:

- Downloads today / 7d / 30d.
- Installer starts if available.
- Trial starts.
- Purchases.
- Checkout conversion.
- Activation conversion.
- Revenue net/gross.
- Refunds/disputes.
- License activation failures.
- Webhook failures.
- Crash reports / opt-in diagnostics.
- Current app version adoption.
- Pro waitlist signups.

Controls:

- Date range: today, 7d, 30d, custom.
- Compare to previous period.
- Filter by app version, channel, country if available from Stripe/CDN, source campaign if available.

P0 acceptance:

- Founder can see within 30 seconds whether downloads, purchases, webhooks, and activations are functioning.

### 2. Revenue And Stripe

Stats:

- Gross revenue, estimated net revenue, fees estimate.
- Sales count by SKU: Local beta, Local 1.0, renewals, future Pro, future top-ups.
- Checkout sessions started/completed/expired.
- Payment success/failure rate.
- Refund count/value.
- Dispute count/value.
- Tax/Managed Payments status once configured.
- Average order value.
- Revenue by day/week/month.
- Coupon usage if introduced later.

Records:

- Stripe customers.
- Checkout sessions.
- Payments.
- Refunds.
- Disputes.
- Subscription states later.

Actions:

- Open Stripe customer/session/payment in Stripe Dashboard.
- Mark internal support status.
- Link Stripe object to license object.

P0 acceptance:

- Every paid checkout maps to exactly one license or a visible exception.
- Every refund/dispute maps to a license revocation/downgrade state or visible pending action.

### 3. Trial Funnel

Stats:

- Website visits if analytics are configured.
- Downloads.
- First runs / trial starts.
- Day-11 reminder impressions if telemetry is allowed.
- Trial extensions.
- Trial expiries.
- Trial-to-purchase conversion.
- Time from first run to purchase.
- Purchase source: website pricing, in-app Settings, trial reminder, expired gate.

Privacy note:

- Trial starts are local by default. Server-side trial stats require either a non-identifying activation/check-in event, opt-in diagnostics, or inference from license activations and downloads. Do not silently add app telemetry just to make this chart prettier.

P0 acceptance:

- For paid beta, downloads, purchases, activations, and license issue timestamps are enough. Detailed trial-behavior telemetry can wait.

### 4. Licenses And Entitlements

Stats:

- Total licenses issued.
- Active licenses.
- Revoked/refunded licenses.
- Update-entitlement expiry distribution.
- Activations per license.
- Device-limit reached count.
- Resend-key requests.
- Activation failure rate by reason.
- Offline refresh age distribution if available.

Search:

- Email.
- License key suffix.
- Stripe customer ID.
- Stripe checkout/payment ID.
- Device hash suffix.

License detail:

- Purchase email.
- Stripe objects.
- SKU.
- License state.
- Update entitlement date.
- Device activations.
- Event timeline.
- Support notes.
- Security flags.

Actions:

- Resend license key.
- Deactivate selected device.
- Revoke license.
- Reinstate license.
- Issue replacement key.
- Extend update entitlement.
- Add support note.

P0 acceptance:

- Support can solve "I lost my key", "second device failed", "payment succeeded but activation failed", and "refund processed" from this screen.

### 5. Activation And License Service Health

Stats:

- Activation requests.
- Activation success rate.
- Activation failure reasons: invalid key, revoked, device limit, signature failure, network failure, server error.
- Entitlement refresh requests.
- Signing service health.
- Webhook lag.
- Webhook retry queue.
- License email delivery success/failure.
- API latency and error rate.

Alerts:

- Activation success rate drops below threshold.
- Webhook queue age exceeds threshold.
- License email delivery failure spike.
- Signing key service unavailable.

P0 acceptance:

- A broken checkout-to-license-to-activation chain is visible before support tickets pile up.

### 6. Downloads, Releases, And Updates

Stats:

- Downloads by version/channel.
- Latest version adoption.
- Update check volume.
- Update success/failure rate if update telemetry is explicitly allowed or inferred server-side.
- Code-signing/certificate status.
- Installer error reports if opt-in.
- Windows version distribution if opt-in or from activation metadata.

Actions:

- Mark release as current/staged/paused.
- See rollout health.
- Link release notes.
- Pause update rollout if a release is bad.

P1 acceptance:

- Founder can tell if a new release is causing activation/update/crash problems.

### 7. Support Inbox And Customer Timeline

Purpose: connect business records to user support without a separate heavy CRM.

Stats:

- Open support cases.
- Case types: activation, license, billing, file preview, annotation, OCR, speech, installer, crash, refund.
- First response time.
- Resolution time.
- Repeat-contact count.

Customer timeline:

- Checkout completed.
- License issued.
- License email sent.
- Activation attempts.
- Device deactivations.
- Refund/dispute events.
- Support notes.
- Uploaded diagnostic bundle references if the user explicitly uploads one.

Actions:

- Add internal note.
- Tag case type.
- Copy support-safe customer summary.
- Resend key.
- Deactivate device.
- Open Stripe.

P1 acceptance:

- Founder can answer a support email without manually checking Stripe, logs, and database separately.

### 8. Product Health Metrics

Purpose: know whether users are succeeding, without collecting private content.

Privacy-safe stats:

- App version.
- OS version.
- Startup success/failure.
- Capture command failure count by error code.
- OCR provider availability/failure count.
- STT provider availability/failure count.
- File preview provider failure count by file type group, not filename.
- Annotation save failure count.
- Settings migration errors.
- Local database migration errors.

Opt-in only stats:

- Feature counters: capture, annotate, pin, OCR, read-aloud, dictate, file preview, Context export.
- Time-to-first-capture.
- Trial gate impressions.
- Conversion prompt source.

P1 acceptance:

- Any app-originating metric has a privacy classification: required operational, opt-in diagnostic, aggregated anonymous, or forbidden.

### 9. Pro Waitlist And Future Metering

Paid beta:

- Pro waitlist signups.
- Source: app gate, pricing page, homepage.
- Requested use case if user provides it.
- Region/time.

Future Pro:

- Hosted transcription minutes.
- High-accuracy transcription minutes.
- Premium TTS characters.
- AI/context credits.
- User-visible quota usage.
- Vendor COGS estimate.
- Gross margin by plan.
- Quota warnings.
- Hard stops.
- Top-up purchases.
- Dunning states.
- Abuse flags.

P2 acceptance:

- Pro cannot launch until the admin panel can show usage, quota, COGS, and dunning state clearly.

### 10. Security, Access, And Audit

Admin roles:

- Owner: everything.
- Admin: operations and support actions.
- Support: read customer/license, resend key, deactivate device, add notes.
- Read-only: dashboards only.
- Finance: revenue/refunds, no device actions.

Security requirements:

- MFA required for all admin users.
- Session timeout.
- IP/rate limiting.
- Audit log for every sensitive action.
- PII redaction by role.
- No license key plaintext display after issuance; show suffix only.
- Secret management for Stripe webhook secrets and entitlement signing keys.

Audit log events:

- Admin login/logout.
- License viewed.
- License key resent.
- Device deactivated.
- License revoked/reinstated.
- Refund/dispute processed.
- Support note added/edited.
- Role changed.
- Export downloaded.

P0 acceptance:

- No support/admin action that changes customer entitlement can happen without an immutable audit log row.

## Dashboard Metrics Inventory

| Area | Metric | Source | Phase | Privacy Class |
| --- | --- | --- | --- | --- |
| Launch | Downloads | Website/CDN | P0 | Anonymous aggregate |
| Launch | Purchases | Stripe webhook/API | P0 | Operational |
| Launch | Activations | License service | P0 | Operational |
| Launch | Activation failure rate | License service | P0 | Operational |
| Revenue | Gross/net revenue | Stripe | P0 | Operational |
| Revenue | Refunds/disputes | Stripe | P0 | Operational |
| Revenue | Checkout completion rate | Stripe | P0 | Operational |
| License | Active/revoked licenses | License DB | P0 | Operational |
| License | Device count per license | License DB | P0 | Operational |
| License | Resend-key requests | License DB | P0 | Operational |
| Webhooks | Failed/retried events | License service | P0 | Operational |
| Email | License email delivery | Email provider | P0 | Operational |
| Release | Version downloads | Website/CDN | P0 | Anonymous aggregate |
| Release | Version activation share | License service | P0 | Operational |
| Support | Case category counts | Admin/support DB | P1 | Operational |
| App Health | Crash reports | User opt-in | P1 | Opt-in diagnostic |
| App Health | Error-code counts | User opt-in or support bundle | P1 | Opt-in diagnostic |
| Product | Feature counters | Explicit opt-in only | P1 | Opt-in/aggregate |
| Pro | Usage by meter | Hosted proxy | P2 | Operational for hosted service |
| Pro | Vendor COGS | Hosted proxy/vendor invoices | P2 | Operational |

## P0 Before Paid Beta

- Admin login with MFA.
- Owner/admin/support/read-only roles.
- Launch overview dashboard.
- Stripe checkout/payment/refund/dispute ingestion.
- License search and detail view.
- License issue/activation/deactivation/revocation timeline.
- Resend license key.
- Deactivate device.
- Webhook processing dashboard and retry visibility.
- License email delivery status.
- Immutable audit log for sensitive actions.
- Export basic CSV for revenue/licenses.

## P1 After Paid Beta Starts

- Support inbox and customer timeline.
- Release/update health.
- Alerting for failed webhooks, activation drops, refund spikes, email delivery failures.
- Opt-in crash/diagnostic report ingestion.
- Privacy-safe app health metrics.
- Better charts: cohorts, conversion over time, time-to-purchase.
- Role-based PII redaction polish.

## P2 Before Pro Launch

- Pro subscription dashboard.
- Hosted usage meters.
- Vendor COGS dashboard.
- Quota warnings and hard-stop visibility.
- Dunning/past-due dashboard.
- Top-up purchase ledger.
- Abuse/anomaly detection.
- Team/org fields if Team enters planning.

## Open Questions

| Question | Owner | Blocking | Suggested Default |
| --- | --- | --- | --- |
| Where will the admin panel live? | Engineering | Yes | Web app inside the license-service project |
| Which auth provider protects admin access? | Engineering | Yes | Start with a small allowlisted admin auth + MFA; revisit managed auth if team grows |
| Which email provider sends license keys? | Engineering | Yes | Choose before license service implementation |
| Do we collect any anonymous product counters by default? | Founder/legal | Yes | No for beta; only operational/license data plus explicit opt-in diagnostics |
| Can support extend a trial remotely? | Founder | No | Not in P0 because trial is local; solve with courtesy license if needed |
| What is the privacy retention period? | Legal/founder | Yes | Keep operational payment/license records as legally required; diagnostic bundles expire automatically |

## Implementation Notes

- Build the admin panel with the license service, not inside the desktop app.
- Treat Stripe as the payment source of truth and the Octadock license DB as the entitlement source of truth.
- Every admin stat needs a named data source and privacy class.
- Prefer read-only dashboards first; add mutation actions only where support requires them.
- Never solve analytics curiosity by weakening the local-first product promise.

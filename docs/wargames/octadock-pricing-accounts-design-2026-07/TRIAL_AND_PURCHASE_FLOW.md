# Trial And Purchase Flow

Date: 2026-07-06

## Decision

Octadock does **not** charge at download.

The paid beta flow is:

1. User downloads Octadock for free.
2. User installs and opens the app.
3. A 14-day full local trial starts on first run.
4. User can buy the Local license anytime during the trial, after the trial expires, or from the pricing page if they already know they want it.
5. After checkout, Stripe sends Octadock a webhook, Octadock issues a license key, and the app activates with that key.

There is no permanent free tier in paid beta. There is a free trial plus a post-expiry view/export mode so users never lose access to their own data.

## Why Pay Later

Paying at download would add friction before the user understands the value. Octadock needs the user to feel the dock, shelf, pins, capture flow, file preview, local speech, and Context workflow on their own desktop. The local utility experience is the sale.

The right commercial moment is after the user has created useful local history, pinned something, annotated something, or used speech/file preview enough to trust it.

## Plans In Paid Beta

| Plan | Price | Status | Purpose |
| --- | ---: | --- | --- |
| Free Trial | $0 | 14 days | Lets the user try the full local app with no account and no card |
| Local License | $49 beta | Purchasable | Own the local app, 3 devices, 12 months of updates |
| Pro | n/a | Waitlist only | Future hosted/cloud features after metering exists |

At 1.0, Local becomes `$59`. The app keeps working after the 12-month update entitlement ends; the user can renew updates later for `$19/year`.

## User Journey

### 1. Website

Primary CTA:

- `Download free trial`

Secondary CTAs:

- `See plans`
- `Buy Local license` only in pricing/account areas, not as the main hero action

The first viewport should not demand payment. Pricing can appear below the hero.

Acceptance criteria:

- The download button never opens Stripe.
- The download button never asks for email.
- The hero does not imply a permanent free tier.
- Pricing copy says "14-day free trial" and "$49 beta one-time Local license".

### 2. Installer

The installer is free and does not require login, email, payment, or a license key.

Acceptance criteria:

- A fresh user can install without network access.
- No Stripe page appears during install.
- No account creation appears during install.

### 3. First Run

On first run, Octadock starts the local trial automatically.

The first-run copy should say:

- `14 days, all local features`
- `No account. No card. Nothing leaves your PC unless you send it.`

Controls:

- Primary: `Start trial`
- Secondary: `I have a license key`

Acceptance criteria:

- Trial state is local and signed.
- No network request is required to start trial.
- A user with a purchased key can activate immediately.

### 4. During Trial

The user can buy at any time, but the app should not nag early.

Recommended timing:

- Days 1-10: only quiet trial status in Settings.
- Day 11: dismissible inline reminder: `3 days left in your trial`.
- Day 14: trial expired gate appears on creation surfaces.

Always-available purchase entry points:

- Settings > Account & Billing
- Trial reminder banner
- Trial-expired gate
- Pricing page

Acceptance criteria:

- Buying is never required to continue using the app during active trial.
- Trial reminders are inline, not launch modals.
- Existing user work is not interrupted by a payment prompt.

### 5. Trial Expired

After trial expiry, Octadock enters view/export mode.

Allowed:

- Open the app.
- View existing captures/history/pins/context packages.
- Copy/export existing data.
- Enter a license key.
- Buy Local.

Blocked until Local is active:

- New captures.
- New OCR.
- New dictation/read-aloud jobs.
- New file previews.
- New pins.
- New Context package creation.

Acceptance criteria:

- User data is never locked or deleted.
- Trial-expired state is an inline gate, not a launch-blocking modal.
- Gate offers `Buy Local` and `Enter license key`.

### 6. Purchase

When the user clicks `Buy Local`, Octadock opens Stripe Checkout in the browser.

Stripe collects payment and email. Octadock receives the successful checkout webhook and issues the Local license.

Activation options:

- Preferred: success page offers `Open Octadock and activate`.
- Fallback: email includes license key and activation instructions.

Acceptance criteria:

- Octadock never handles card details.
- A completed Stripe Checkout creates exactly one license.
- License activation does not require first-party sign-in.
- License works on up to 3 devices.

## Entitlement States

| State | Meaning | User Can Do |
| --- | --- | --- |
| Trial active | User is evaluating | Full local app |
| Trial extended | User clicked one 7-day extension | Full local app |
| Trial expired | User has not purchased | View/export existing data, buy, activate |
| Local active | User bought the app | Full local app and BYO-key cloud routes |
| Updates expired | 12 months elapsed | Keep using installed app; renewal unlocks newer versions |
| Revoked/refunded | Purchase reversed | Trial-expired behavior, data intact |

## Copy Rules

Use:

- `Free trial`
- `Local license`
- `One-time payment`
- `No account required`
- `Buy when you are ready`

Avoid:

- `Free tier`
- `Free forever`
- `Subscribe to use Octadock`
- `Pay to download`
- `Create account`

## Implementation Notes

- Trial starts on first app run, not at download time.
- Trial should be machine-local for beta. Accept reset risk rather than adding account friction.
- Purchase can happen from web or app, but the user's first experience should be the free download.
- The Stripe product being sold in beta is `Octadock Local`, not Pro.
- Pro must remain waitlist-only until hosted metering and dunning exist.

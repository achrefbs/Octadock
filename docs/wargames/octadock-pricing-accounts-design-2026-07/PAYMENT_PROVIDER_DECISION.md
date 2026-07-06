# Payment Provider Decision - Stripe

Date: 2026-07-06

## Decision

Use **Stripe** for Octadock payments.

The founder already has a Stripe account, and the implementation should optimize for speed: Stripe Checkout for purchases, Stripe Billing + Customer Portal for future subscriptions, Stripe webhooks feeding an Octadock license service, and no first-party user account system in v1.

This replaces Fable's Paddle recommendation.

## Stripe Mode

### V1 Paid Beta

Use **Stripe Checkout on standard Stripe Payments**:

- One-time Local beta license: `$49`
- One-time Local 1.0 license later: `$59`
- Optional update renewal: `$19/year` as a one-time renewal SKU
- Pro: waitlist only during paid beta
- Top-ups: not for beta; revisit when Pro metering exists

Payment timing is defined in `TRIAL_AND_PURCHASE_FLOW.md`: users download first, trial starts on first run, and payment happens later when the user chooses `Buy Local` during the trial, after expiry, or from the pricing page.

Why this is the fastest path:

- Stripe account already exists.
- Hosted Checkout keeps card handling out of Octadock.
- Checkout supports one-time and subscription flows.
- Customer Portal can handle subscription/account billing later.
- Stripe webhooks are enough to issue and revoke Octadock license entitlements.

### Managed Payments

Stripe Managed Payments is **not blocked by preview status anymore**. Stripe docs say it reached general availability on April 22, 2026, and AIaaS tax codes were added June 2, 2026.

Do not make it the default implementation assumption until the Stripe Dashboard confirms Octadock's account, country, and product tax codes are eligible. If eligible, it can be enabled later without changing the app entitlement model.

Important pricing note: Stripe lists Managed Payments as **3.5% in addition to Payments fees**, not as a standalone 3.5% total MoR fee.

## Account Model

Octadock v1 still has **no first-party accounts**.

The user identity model is:

- Customer pays through Stripe Checkout.
- Stripe collects email and payment details.
- Octadock receives checkout/subscription/refund events through webhooks.
- Octadock license service issues a license key and signed entitlement.
- App activation uses license key + purchase email, not sign-in.
- Subscription management later opens Stripe Customer Portal.

Marketing wording should be:

- "No sign-in required"
- "License key activation"
- "Billing handled by Stripe"

Avoid:

- "No account data at all"
- "Octadock stores nothing"

Octadock will store license-service data: purchase email, license key hash, Stripe customer/session/subscription IDs, activation/device hashes, entitlement state, revocation state, and resend logs.

## Net Revenue Snapshot

Assumptions for rough implementation planning:

- Standard US card baseline: `2.9% + $0.30`.
- Subscription Billing adds `0.7%` of billing volume.
- Stripe Tax, if used, adds additional cost depending on setup and registration state.
- Managed Payments adds `3.5%` on top of Payments fees.
- Rates vary by Stripe account country, card type, payment method, currency conversion, tax setup, and custom pricing. Verify in the Stripe Dashboard before publishing final margin claims.

| SKU | Gross | Stripe Standard Net | Stripe Standard + Billing Net | Stripe Managed Payments Net | Notes |
| --- | ---: | ---: | ---: | ---: | --- |
| Local beta license | $49 | $47.28 | n/a | $45.56 | One-time Checkout |
| Local 1.0 license | $59 | $56.99 | n/a | $54.92 | One-time Checkout |
| Update renewal | $19 | $18.15 | n/a | $17.48 | One-time renewal SKU |
| Pro monthly | $10 | n/a | $9.34 | $8.99 | Future subscription, not paid beta |
| Top-up small | $5 | $4.56 | n/a | $4.38 | Future only; small-ticket fixed fee hurts |
| Top-up fallback | $10 | $9.41 | n/a | $9.06 | Prefer over $5 if support/top-up complexity is high |

Formula examples:

- Standard one-time: `gross - (gross * 0.029) - 0.30`
- Standard subscription: `gross - (gross * 0.036) - 0.30`
- Managed one-time: `gross - (gross * 0.064) - 0.30`
- Managed subscription: `gross - (gross * 0.071) - 0.30`

## Required Stripe Products

Create these in Stripe before implementation:

| Stripe Object | Purpose | Beta |
| --- | --- | --- |
| Product: Octadock Local | Paid beta and 1.0 one-time license | Required |
| Price: Local beta `$49` | Beta checkout | Required |
| Price: Local 1.0 `$59` | Later public checkout | Not active in beta |
| Product: Update Renewal | 12-month update renewal | Required before first renewal window, not beta launch |
| Price: Update renewal `$19` | Optional update extension | Later |
| Product: Octadock Pro | Hosted usage subscription | Waitlist only |
| Price: Pro monthly `$10` | Future subscription | Not active in beta |
| Product: Hosted top-up | Future credit pack | Not active in beta |

## Required Webhooks

The license service must consume and idempotently process:

- `checkout.session.completed`
- `checkout.session.expired`
- `customer.created`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `charge.refunded`
- `charge.dispute.created`
- `invoice.payment_succeeded`
- `invoice.payment_failed`
- `customer.subscription.created`
- `customer.subscription.updated`
- `customer.subscription.deleted`

Paid beta can start with the one-time purchase subset:

- `checkout.session.completed`
- `charge.refunded`
- `charge.dispute.created`

## License Service Contract

Stripe does not replace Octadock's entitlement system.

Octadock still needs a small license service:

- Receive Stripe webhooks.
- Create license keys for successful Local purchases.
- Email or expose the license key through a success/deep-link flow.
- Track activations with a 3-device limit.
- Issue Ed25519-signed entitlement files.
- Revoke or downgrade refunded/disputed licenses.
- Provide resend-key flow by purchase email.
- Support future subscription entitlement refresh.

## App UX Defaults

- Website primary CTA says "Download free trial", not "Buy now".
- Settings gets **Account & Billing**, not "Sign in".
- Activation field says "Enter license key".
- Billing button says "Manage billing in Stripe".
- Trial gate says "Buy Octadock" and opens Stripe Checkout.
- Post-checkout success page offers `octadock://activate?key=...` if we can safely issue immediately.
- Fallback success page says "Check your email for your license key".

## Risks And Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Stripe standard is not MoR by default | Tax/compliance remains founder responsibility unless Stripe Tax/Managed Payments is configured | Use Stripe Tax/Managed Payments if eligible; legal/accounting review before public launch |
| Managed Payments eligibility varies | Cannot assume MoR for every Stripe account/product | Check Dashboard eligibility before committing copy |
| Small top-ups lose margin to fixed fee | $5 packs are inefficient | Skip top-ups until Pro; prefer $10 fallback |
| Refund/revocation without accounts can confuse users | Support load | License key + purchase email recovery; clear Settings state |
| Stripe outages affect checkout only | New buyers blocked temporarily | Existing app entitlements remain local/signed and fail open |

## Sources

- Stripe pricing: https://stripe.com/pricing
- Stripe Checkout: https://docs.stripe.com/payments/checkout
- Stripe Customer Portal: https://docs.stripe.com/customer-management
- Stripe Managed Payments changelog: https://docs.stripe.com/payments/managed-payments/changelog
- Stripe Managed Payments migration docs: https://docs.stripe.com/payments/managed-payments/update-checkout

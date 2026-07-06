# Live Pricing Sources to Verify

Pricing changes frequently. Fable must verify official pages during the pass if
it has browsing access. The values below were spot-checked on 2026-07-06 and are
inputs, not final truth.

## AI and Voice Cost Sources

### OpenAI API Pricing

Official source:

- https://developers.openai.com/api/docs/pricing

Spot-check on 2026-07-06:

- `gpt-4o-transcribe`: estimated USD 0.006/minute.
- `gpt-4o-mini-transcribe`: estimated USD 0.003/minute.

Fable should verify:

- current transcription pricing;
- whether pricing is per minute, token-estimated, or both;
- any batch/discount options relevant to a Pro plan;
- whether BYO-key language should mention OpenAI specifically or generic cloud
  providers.

### ElevenLabs

Official sources:

- https://elevenlabs.io/pricing
- https://elevenlabs.io/pricing/api

Spot-check on 2026-07-06:

- The subscription pricing page uses a credit model.
- The API pricing page exposes dollar-metered usage, including TTS per 1,000
  characters and STT per hour.

Fable should verify:

- whether Octadock should model ElevenLabs as credits, direct API cost, or BYO
  provider only;
- whether premium voices should be included in Pro or stay BYO-key initially;
- whether the pricing research mixes subscription credits and API pricing in a
  way that would mislead product planning.

## Payment Gateway Sources

### Lemon Squeezy

Official sources:

- https://www.lemonsqueezy.com/pricing
- https://docs.lemonsqueezy.com/help/getting-started/fees

Spot-check on 2026-07-06:

- Public pricing headline: 5% + 50 cents per transaction.
- Additional/edge fees may apply, including some marketing and payout fees.

Fable should verify:

- merchant-of-record posture;
- subscriptions and license key support;
- refund/chargeback handling;
- whether a one-time Windows license plus Pro subscription is easy to implement.

### Paddle

Official source:

- https://www.paddle.com/pricing

Spot-check on 2026-07-06:

- Public pricing headline: 5% + 50 cents per Checkout transaction.
- Products under USD 10 or invoicing may need custom pricing.

Fable should verify:

- merchant-of-record posture;
- checkout, subscription, license/entitlement fit;
- whether Pro at USD 9/month is a bad fit because of the under-USD-10 note.

### Stripe

Official source:

- https://stripe.com/pricing

Spot-check on 2026-07-06:

- US domestic cards: 2.9% + 30 cents.
- Additional fees apply for manually entered cards, international cards, and
  currency conversion.

Fable should verify:

- billing/subscription/tax/usage-metering add-ons;
- operational burden compared with merchant-of-record providers;
- whether Stripe should be v1 or v2.

## Competitive Pricing Sources

### CleanShot X

Official source:

- https://cleanshot.com/pricing

Spot-check on 2026-07-06:

- Cloud Pro listed at USD 8/user/month billed annually or USD 10/month monthly.
- Optional app update renewal listed at USD 19/year.

Fable should verify:

- current app purchase price;
- update policy;
- what is bundled in app-only vs Cloud Pro.

### TechSmith Snagit

Official source:

- https://www.techsmith.com/store/snagit

Spot-check on 2026-07-06:

- Regional page returned an annual personal subscription price in EUR.

Fable should verify:

- US/global price;
- subscription-only vs perpetual/update structure;
- what AI/cloud/sharing features are bundled.

### ShareX

Official source:

- https://getsharex.com/

Spot-check on 2026-07-06:

- ShareX positions itself as free, open source, no ads, lightweight, and
  privacy-first.

Fable should verify:

- which capabilities make the free alternative most threatening;
- what Octadock must charge for besides "screenshot app".


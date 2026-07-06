# Octadock Launch Pricing Research

Date: 2026-07-06  
Status: launch research, not a final pricing decision

## Short Recommendation

Use a hybrid model:

- **One-time local license** for the Windows desktop utility.
- **Pro subscription** for hosted AI usage, higher-accuracy cloud speech,
  summaries/explanations, premium voices, context packaging, and future hosted
  services.
- **Bring your own key** for power users who want cloud AI without Octadock
  reselling usage.

Do not sell unlimited hosted AI inside a one-time license. The local app has
mostly fixed support/update costs; cloud AI, speech, and premium voice features
have recurring cost of goods sold.

## Why Hybrid Fits Octadock

Octadock has two different economic shapes:

1. **Local utility features**
   - capture;
   - shelf;
   - annotation;
   - pins;
   - local history;
   - local OCR;
   - local Parakeet/Whisper dictation;
   - Windows read-aloud;
   - clipboard history;
   - file preview;
   - text tools;
   - CLI/protocol automation.

   These can be sold as durable desktop software. A paid license with a year of
   updates is a familiar model for power-user desktop tools.

2. **Hosted or metered features**
   - cloud transcription;
   - AI explanation/summarization;
   - premium TTS;
   - hosted context analysis;
   - future hosted sharing or team features;
   - optional image/video generation.

   These need monthly limits, usage credits, overage, or BYO-key because vendor
   costs scale with use.

## Proposed Packaging

### Local License

Purpose: make Octadock credible as a serious Windows utility before the cloud
layer is complete.

Possible price band:

- $49 to $79 one-time for individual users.
- Includes 12 months of updates.
- Renewal for updates: $19 to $39/year.
- The app keeps working if the user does not renew updates.

Included:

- all local capture, shelf, annotate, pin, OCR, history, clipboard, preview,
  text tool, recording MVP, dictation, and read-aloud features;
- local models when available;
- BYO-key configuration for cloud providers;
- no included hosted AI credits.

### Pro Subscription

Purpose: cover recurring hosted AI cost while adding meaningful product value.

Possible price band:

- $8 to $15/month for solo Pro.
- Annual discount optional.
- Should include the local feature set plus monthly cloud credits.

Included:

- hosted transcription allowance;
- AI explain/summarize allowance;
- Context Shelf AI features when built;
- premium voice allowance if using ElevenLabs/OpenAI TTS;
- priority updates and beta features.

Suggested launch wording:

> Start local. Add Pro when you want cloud accuracy and AI context.

### Team / Business

Defer until the product needs it, but design checkout and entitlements so it is
not blocked later.

Likely features:

- seat management;
- centralized billing;
- shared prompt/context libraries;
- admin policy for cloud sends;
- SSO later;
- usage reporting.

## AI Cost Notes

### OpenAI Transcription

Official OpenAI API pricing lists:

- `gpt-4o-transcribe`: estimated $0.006/minute;
- `gpt-4o-mini-transcribe`: estimated $0.003/minute.

Implication:

- A Pro plan can include a useful transcription quota without huge cost, but the
  app still needs hard monthly usage accounting.
- `gpt-4o-mini-transcribe` is the better default for included credits.
- `gpt-4o-transcribe` can be a "high accuracy" toggle that consumes more credits.

Source: https://developers.openai.com/api/docs/pricing

### ElevenLabs

Official ElevenLabs pricing uses a monthly credit system with separate Free,
Starter, Creator, Pro, Scale, and Business tiers. Exact credit allowances can
change by plan, billing term, and promotion, so verify the live page before
publishing prices.

Approximate product credit costs listed by ElevenLabs:

- Text to Speech: 1 credit per character;
- Speech to Text: 330 credits per minute;
- Sound Effects: 200 credits per generation.

Implication:

- ElevenLabs is good for premium voices and short high-quality marketing/audio
  experiences.
- It should not be the default unlimited read-aloud engine.
- Windows TTS should remain the default local read-aloud path.
- Use ElevenLabs as BYO-key, premium credit, or limited Pro feature.

Source: https://elevenlabs.io/pricing

## Payment Gateway Options

### Lemon Squeezy

Observed pricing:

- 5% + 50c per transaction;
- no monthly fee for payment processing;
- global payments and automated sales tax compliance are part of the value prop.

Fit:

- strong for fast launch;
- merchant-of-record style simplicity;
- good for one-time licenses, subscriptions, license keys, and tax handling;
- less flexible than a custom Stripe stack for complex usage metering.

Source: https://www.lemonsqueezy.com/pricing

### Paddle

Observed pricing:

- 5% + 50c per checkout transaction;
- Paddle positions itself as a merchant-of-record solution for SaaS/apps;
- pricing page notes products under $10 or invoicing may need custom pricing.

Fit:

- strong for software/app global tax and subscription handling;
- good if Octadock wants merchant-of-record simplicity;
- keep starter prices above $10/month or expect custom pricing discussions.

Source: https://www.paddle.com/pricing

### Stripe

Observed pricing:

- 2.9% + 30c per successful domestic card transaction in the US;
- Stripe offers Billing, Subscriptions, Tax, Revenue Recognition, and Metronome
  usage-based billing as separate products/features.

Fit:

- most flexible long-term;
- best if Octadock needs custom usage billing, internal wallets, usage events,
  credit packs, coupons, and direct product control;
- more operational burden than merchant-of-record options unless paired with
  Stripe Tax/Managed Payments.

Source: https://stripe.com/pricing

## Competitive Pricing Signals

### CleanShot X

CleanShot sells:

- app + Cloud Basic at $29 one-time;
- one year of updates;
- optional update renewal at $19/year;
- Cloud Pro at $8/user/month annually or $10/month monthly.

Signal:

- desktop capture utilities can support one-time app licensing plus a recurring
  cloud tier.
- Octadock should not copy the product, but the pricing pattern is directionally
  useful.

Source: https://cleanshot.com/pricing

### TechSmith Snagit

Snagit is sold as a yearly personal subscription and includes Windows support,
screen capture, markup, sharing, and some AI features.

Signal:

- the broader screen capture market accepts subscriptions, especially for mature
  products with sharing, AI, and bundled services.
- Octadock can still differentiate with a local-first one-time option.

Source: https://www.techsmith.com/store/snagit

### ShareX

ShareX is free and open source for Windows, with capture, recording, file
sharing, productivity features, OCR, pinning, and many destinations.

Signal:

- Octadock cannot win by being "a screenshot tool" alone.
- The paid angle must be speed, polish, desktop objects, voice, local context,
  file preview, privacy, and future AI-ready workflows.

Source: https://getsharex.com/

## Metering Recommendations

Track usage internally even if the first release uses BYO keys only.

Meter:

- cloud transcription minutes;
- premium transcription model minutes;
- explain/summarize input/output tokens;
- premium TTS characters;
- sound effect/image/video generations if ever exposed;
- uploaded/shared storage if cloud sharing is built.

Useful credit labels:

- "cloud transcription minutes";
- "AI context credits";
- "premium voice characters".

Avoid hiding every vendor unit behind one vague number too early. Users need to
understand what they are spending.

## Launch Decision

Recommended beta checkout:

- One-time Local license: $59.
- Pro subscription: $9 to $12/month.
- BYO key available on all paid plans.
- No unlimited hosted AI.
- Use Lemon Squeezy or Paddle first if global tax simplicity matters more than
  custom billing.
- Revisit Stripe/Metronome once usage-based Pro is proven.

Open questions:

- Should the first beta be waitlist-only before payment?
- Should Local license include one year of updates or lifetime minor updates?
- Should Pro require an Octadock account from day one?
- Should BYO-key be available in free/beta builds, or paid-only?
- Should cloud usage roll over, or reset monthly?

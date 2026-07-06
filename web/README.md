# Octadock website (`web/`)

The minimum honest paid-beta website for Octadock: a download-first landing page,
a pricing page, and the legal surfaces (privacy, refunds, EULA, terms). Plain
static HTML + one CSS file. **No build step, no framework, no CDN, no external
fonts.** Every file works opened directly in a browser or served statically.

## Files

| File | Purpose |
| --- | --- |
| `index.html` | Download-first landing. Hero CTA, honest feature summary, local-first precision, network-egress table. |
| `pricing.html` | $49 one-time Local license; Pro as waitlist-only; Context as "in development". |
| `privacy.html` | Exact license-service fields, retention, processors, and the network-egress table. |
| `refunds.html` | Voluntary 14-day money-back guarantee + EU/UK withdrawal + immediate-supply consent. |
| `eula.html` | End-user license agreement (draft, plain-language). |
| `terms.html` | Terms of service (draft). |
| `styles.css` | The one shared stylesheet. Dark "Obsidian Instrument" palette. |

## How to host (per the 0-to-100 plan, WS2)

The plan calls for **Cloudflare Pages** for the site and **Cloudflare R2 + CDN**
for the signed installer download, with CDN analytics as the canonical Downloads
metric.

1. **Site → Cloudflare Pages.** Point a Pages project at this `web/` directory
   (framework preset: "None"; build command: none; output directory: `web`).
   Because there is no build step, Pages just serves the files.
2. **Domain.** Attach `octadock.com` (and `www`) to the Pages project. DNS is at
   Namecheap today and still on the parking records; replace the parking `URL`/
   `CNAME` records with the Pages target once the host is chosen. Do **not** touch
   the FastMail MX/DKIM/DMARC/SPF records — email depends on them.
3. **Download build → R2 + CDN.** Host the signed installer on R2 behind the CDN
   so download counts are measurable. Wire CDN analytics as the Downloads number.
4. **Checkout.** The Buy buttons point at `#checkout-pending`. Replace with the
   live Stripe Checkout link (or Payment Link) for price
   `octadock_local_beta_usd_49` once legal URLs, tax posture, webhook delivery,
   and license-email delivery are all verified (see the commercial infra doc).

Local preview needs no tooling — open `web/index.html`, or serve the folder
(`python -m http.server` from inside `web/`, any static server works).

## Founder-gated — replace the placeholders before launch

These are intentionally stubbed. Each is a hard dependency owned by the founder,
not something to invent:

| Placeholder in the pages | Replace with | Gate |
| --- | --- | --- |
| `href="#download-pending"` (index) | The R2/CDN URL of the signed installer | Signed build must exist and pass clean-VM verification first. |
| `<SHA-256 PENDING …>` (index) | The published SHA-256 of the exact shipped installer | Must match the CI artifact hash byte-for-byte. |
| `href="#checkout-pending"` (pricing) | Live Stripe Checkout / Payment Link for `octadock_local_beta_usd_49` | Do not publish a live checkout until legal URLs, tax posture, webhook delivery, and license-email delivery are all verified. |
| `href="#waitlist-pending"` (pricing) | The Pro waitlist form endpoint (double opt-in, consent-stamped) | Needs the waitlist endpoint from WS3. |
| DNS host target | The Cloudflare Pages / R2 targets | Only after the host is chosen; never disturb the FastMail records. |
| Legal copy in `privacy.html`, `refunds.html`, `eula.html`, `terms.html` | Lawyer-reviewed text | All four legal pages are marked **DRAFT — pending legal review**. Keep them marked until a lawyer signs off. |

The Stripe catalog, DNS, and email facts these pages reflect live in
`docs/ops/COMMERCIAL_INFRA_SETUP_2026-07-06.md`. Email is **FastMail** on
`mail.octadock.com` (not Postmark — the plan's earlier default was superseded by
what was actually provisioned).

## Honesty constraints — future edits MUST preserve these

The copy is deliberately, verifiably honest and maps to real code behavior and the
project's locked decisions. Do not let edits regress any of the following. The
first six are enforceable by grep: these strings must never appear in `web/`.

- **Never** claim "fully offline" or "local AI". Dictation needs a one-time model
  download; "explain"/"summarize" shell out to the user's cloud AI CLI.
- **Never** mention "AI Discovery" or "AI Sessions". That feature was permanently
  dropped (schema migration 6 dropped its tables). Do not resurrect it.
- **Never** market screen recording as capturing audio. Recording is **video
  only**; audio is not implemented yet.
- **Pro is waitlist-only.** Never a buy button, never a price, never "buyable".
  Violet is reserved for cloud/Pro; teal is for everything local.
- **Context is "in development."** Never sold in a checkout bullet. It ships to
  Local at no extra cost when ready.
- **The egress table must stay complete and accurate.** Every outbound call the
  app can make is listed (one-time Hugging Face model download; license
  activation/entitlement refresh to the Octadock license service; opt-in
  OpenAI / ElevenLabs; the user's own AI CLI for explain/summarize). Nothing
  transmits captures, history, or clipboard.
- **Pricing must match reality:** $49 beta ($59 at 1.0), one-time, 3 devices,
  12 months of updates, includes 1.0, keeps working after updates end, optional
  $19/yr renewal. No permanent free tier. No first-party accounts (identity =
  license key + purchase email + Stripe portal).
- **Every legal/draft page stays marked** "DRAFT — pending legal review" until a
  lawyer reviews it.

### Honesty grep (run before shipping any edit)

From the repo root, this must return **no matches**:

```bash
grep -rniE "fully offline|local AI|AI Discovery|AI Sessions|record(ing)? (with )?audio|audio recording" web/
```

## Design notes

- **Palette** is grounded in the shipped WPF dark theme
  (`src/Octadock.App/Resources/Themes/Dark.xaml`): obsidian blue-black surfaces,
  teal→cyan accent (`#2dd4bf`/`#38bdf8`), hairline glass borders. Violet
  (`#a78bfa`) is added only for cloud/Pro and appears nowhere on a local/CTA
  element.
- **Self-contained:** system font stack, inline SVG icons, a `data:` favicon. No
  network requests at all, so the pages work air-gapped and pass a strict CSP.
- **Accessibility:** skip link, visible teal focus rings, semantic landmarks and
  headings, labelled tables, ≥4.5:1 body contrast, keyboard-operable, and
  `prefers-reduced-motion` honored (all reveal/scroll motion is suppressed).
- **Motion** stays under 300ms with a custom ease-out; the download CTA
  deliberately has no hover transform (a critical, high-frequency action).
- **Responsive** down to mobile with no horizontal body scroll; wide tables scroll
  inside their own container.

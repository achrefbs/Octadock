# Wargame Result — Octadock Pricing, Accounts, Gates, and Design System

Run: 2026-07-06 · Baseline: `main` @ `48bceb0` · Method: `WARGAME_PLAN.md` roles/scenarios · Pricing verified live on 2026-07-06 (see snapshot) · Builds on `docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md`

## Executive Summary

- **Overall verdict:** Sellable, with two structural corrections. The hybrid model (Local license + Pro subscription + BYO-key) survives the wargame. What does not survive: launching on Lemon Squeezy (platform is being folded into Stripe Managed Payments), and building a first-party account system for v1 (nothing in v1 needs one — license keys + the payment provider's portal cover every flow).
- **Recommended launch model:** **Paid beta as a combination**: free 14-day full-local trial (no account, no card) → one-time **Local license, $49 beta / $59 at 1.0** (12 months of updates, keeps working after) → **Pro deferred to a waitlist** until metering infrastructure exists (target $10/month). BYO-key available to everyone including trial. No first-party accounts anywhere in v1.
- **Biggest pricing risk:** Selling Pro before hosted metering exists. Pro's promise (cloud transcription credits, premium voices, AI context) has recurring COGS and needs quota enforcement, top-ups, and dunning states — none built. Selling it early creates refund/chargeback exposure and support debt. Sell Local now; capture Pro demand via in-app waitlist.
- **Biggest account/gating risk:** Scope inflation around identity. If v1 requires accounts for activation, the privacy-first positioning ("no account required") collapses and a full auth subsystem lands on the pre-beta critical path. The entitlement layer (device activation, signed offline license file, revocation) is the real work; do that, skip auth.
- **Biggest design-system risk:** One-note teal-on-navy plus icon chaos. Everything interactive today speaks teal; the dock mixes icon fonts, emoji-like glyphs, and text labels; the accent gradient appears on anything "primary". Fix: a neutral Fog ramp for structure, **violet as the reserved cloud/Pro tier color**, one icon family (Lucide), and an explicit accent budget per surface.
- **P0 decisions required before implementation:** (1) payment provider = Paddle; (2) Local $49→$59 with $19/yr update renewal, 3 devices; (3) trial = 14 days, non-bricking expiry; (4) no first-party accounts in v1; (5) BYO-key free for all tiers, keys move to DPAPI/Credential Manager; (6) Pro = waitlist until metering ships, then $10/mo with 300 std transcription min; (7) design tokens locked per this document before the Phase 1 UI work from wargame #1 starts (it depends on them).

## Verified Pricing Snapshot

Verified by live fetch on 2026-07-06. "Verified" = read from the official page; "Corroborated" = official page unreachable, cross-checked from indexed official content; "Ambiguous" = structure verified, number not.

| Item | Source | Verified Price/Claim | Status | Notes |
| --- | --- | --- | --- | --- |
| OpenAI transcription | developers.openai.com/api/docs/pricing | `gpt-4o-transcribe` $2.50/$10.00 per 1M tokens ≈ **$0.006/min**; `gpt-4o-mini-transcribe` $1.25/$5.00 ≈ **$0.003/min**; `gpt-realtime-whisper` $0.017/min | Verified | Token-based with per-minute estimates. **whisper-1 and all dedicated TTS models (tts-1, gpt-4o-mini-tts) are no longer on the pricing page** — any OpenAI-TTS plan in the research doc is unverifiable; cost premium voices on ElevenLabs numbers only. |
| ElevenLabs API/subscription | elevenlabs.io/pricing, /pricing/api | Subscriptions: Free $0/10k cr, Starter $6/30k, Creator $22 (intro $11)/121k, Pro $99/600k. API: TTS Flash/Turbo **$0.05 per 1k chars**, Multilingual v2/v3 **$0.10 per 1k chars**; STT Scribe **$0.22/hr** ($0.0037/min); credits: TTS 1 cr/char, STT 330 cr/min | Verified | Research doc's caution was right: subscription credits ≠ API pricing. Model Octadock hosted usage on the **API dollar rates**, never the consumer credit tiers. |
| Lemon Squeezy | lemonsqueezy.com/pricing (403), docs (403) | 5% + 50¢, +1.5% international; MoR; native license-key API | Corroborated, **platform risk** | Official pages bot-blocked. Acquired by Stripe (2024); **Stripe Managed Payments (MoR, same 5%+50¢) entered public preview Feb 2026 as its successor with a migration path**. Do not build a new business on LS in mid-2026. |
| Paddle | paddle.com/pricing | **5% + 50¢ per checkout transaction**; MoR "purpose-built for SaaS and apps"; subscriptions + one-time supported; "products under $10 or invoicing → custom pricing" | Verified | Under-$10 note verbatim. $10/mo Pro and $59 license are at/above the line; a future $5 top-up SKU may trigger the custom-pricing conversation — confirm with Paddle sales before relying on top-ups. |
| Stripe | stripe.com/us/pricing | 2.9% + 30¢ US cards; Billing +0.7%; Tax +0.5% (or $0.50/txn API); intl +1.5%, FX +1%. Not MoR standard. **Stripe Managed Payments: MoR at 3.5%/txn, 75+ countries, preview** | Verified | Page geo-served in Spanish; numbers unambiguous. Managed Payments is the future consolidation path once it exits preview. |
| CleanShot X | cleanshot.com/pricing | **$29 one-time** (Mac, keeps working), 1 yr updates, **$19/yr optional renewal**; Cloud Pro **$8/mo annual / $10/mo monthly** | Verified | The pricing pattern Octadock mirrors. Octadock's broader scope (dictation, read-aloud, preview, OCR, context) justifies $59 vs $29. |
| Snagit | techsmith.com/store/snagit | Subscription-only since 2025 (perpetual discontinued); observed **€39.57/yr**; US **~$39/yr individual** | Structure verified; USD ambiguous | Geo-priced page. Signal: market accepts subscriptions, and a local-first one-time license is now a *differentiator* against Snagit. |
| ShareX | getsharex.com | "Free forever. Open source. No account required." 10 capture methods incl. scrolling; OCR; screen/GIF recording; 20+ annotation tools | Verified | ShareX already has free OCR + scrolling capture. Octadock cannot charge for capture mechanics; it charges for the integrated surface: shelf/pins/dictation/read-aloud/preview/context, polish, and support. |

## Recommended Packaging

### Free / Trial / Beta

- **Recommendation:** 14-day free trial, full local feature set, no account, no card, no watermarks. One in-app extension (+7 days) allowed via a "need more time" click — cheap goodwill, removes the top support ticket. During beta the download is public (no waitlist gate on the trial itself); the **waitlist applies to Pro only**.
- **Included:** everything in Local license, plus BYO-key cloud providers (user pays their own vendor).
- **Limits:** time only. On expiry the app does **not brick**: existing history/shelf/pins/annotations remain viewable and exportable forever; new captures, OCR, dictation, read-aloud, and preview-of-new-files are disabled behind a calm inline gate. Never hold user data hostage (aligned with wargame #1's data-safety posture).
- **Account required:** No. Trial state is machine-local (hashed machine id + signed trial file). Wiping `%LOCALAPPDATA%` resets it; accept this during beta rather than adding accounts — the revenue lost to determined resetters is smaller than the conversion lost to a signup wall.

### Local License

- **Recommended price:** **$59 one-time** at 1.0; **$49 during paid beta** with a public "beta price" label and launch-price lock for beta buyers. (Research band $49–79; CleanShot anchors $29 for a narrower tool.)
- **Included:** all local features — every capture mode (area/window/full/scrolling/OCR/record), Capture Shelf, pins, annotation incl. image writeback via the wargame-#1 revision layer, local OCR, local dictation (Parakeet/Whisper), Windows read-aloud, clipboard history, file preview (all types incl. PDF render and zip browse when they ship), Context building + package export, Explorer integration, CLI/protocol automation, BYO-key configuration. **Rule: local compute is never gated behind Pro.**
- **Update entitlement:** 12 months of updates included; **$19/year optional renewal** (CleanShot-verified pattern). After expiry the installed app keeps working forever; the updater simply stops offering new versions and Settings shows a quiet renewal card.
- **Device policy:** **3 concurrent activations** per license (desktop + laptop + reinstall slack), self-serve deactivation in Settings > Account & Billing; activation counter visible. Support can force-clear.
- **Account required:** **No.** License key delivered by email at checkout (email address ≠ account). Key + purchase email is the recovery credential.
- **Offline behavior:** activation requires one online call; after that the license is a **signed entitlement file on disk — no phone-home requirement, indefinite offline validity**. Opportunistic revalidation when online (picks up revocations). Fully-offline machines: manual activation file exchange, support-mediated (P2).

### Pro Subscription

- **Recommended price:** **$10/month** or **$96/year** ($8/mo effective) when it launches. **Do not sell Pro in the paid beta** — sell the waitlist ("Pro is coming: cloud accuracy and AI context. Join from the app."). Launch Pro only when metering, quotas, top-ups, and dunning states exist.
- **Included:** everything in Local (while subscribed — see cancellation), plus monthly hosted credits: cloud transcription (default `gpt-4o-mini-transcribe`-class), high-accuracy transcription toggle (2× credit weight), AI explain/summarize when built, hosted premium voices when metering matures (v1.5 — BYO-only before that), early-access features.
- **Monthly quotas:** 300 standard transcription minutes (COGS ≈ $0.90–1.10 at verified rates); high-accuracy counts 2×; premium TTS when enabled: 20k Flash-class characters (COGS ≈ $1.00), 2× weight for Multilingual-class. Worst-case COGS ≈ $3/user vs $8.45 net of Paddle fees — ≥ 65% gross margin. Quotas reset monthly, **no rollover in v1** (revisit with data).
- **Overage/top-ups:** soft warning at 80%, hard stop at 100% with automatic fallback to local providers (never silent vendor spend). Top-up: $5 credit pack ≈ 250 standard minutes, usable 12 months, survives cancellation (consumable, already paid). Confirm sub-$10 SKU treatment with Paddle first; fallback is a $10 pack.
- **BYO-key behavior:** configured keys always available as an alternative route; BYO usage never consumes Octadock credits and is labeled "billed by your provider." When credits run out and a key exists, the exhausted state offers one-click "switch to your key."
- **Cancellation behavior:** Pro runs to period end, no refund proration (MoR policy governs statutory cases). After end: falls back to whatever the user owns — Local license if purchased, else trial-expired behavior. **Fairness rule (recommended, owner to ratify): 12 consecutive paid months of Pro grants a perpetual Local fallback license** ("your subscription earns the app"). Unused top-ups stay usable for hosted features for their 12-month life.

### Team Later

- **Later features:** seats, centralized billing, shared context libraries, admin policy for cloud sends (org-level consent lockdown), usage reporting, SSO.
- **Entitlement architecture to preserve:** `license.seats` (int, =1 today), `license.owner_email`, `activation.seat_index`, nullable `entitlement.org_id`, `meter.scope` (user|org), policy blob on entitlement (`cloud_allowed`, per-provider allow-list). Ship these fields dormant in the v1 schema so Team never requires a migration of live licenses.

## Account And Payment Feature Spec

Provider-portal-first: **Octadock has no first-party accounts in v1.** Identity = license key + purchase email; billing management = Paddle customer portal (magic-link by email). Every flow below is designed against that.

### Required User Flows

| Flow | Happy Path | Error/Edge States | UI Surfaces | Data Stored | Acceptance Criteria |
| --- | --- | --- | --- | --- | --- |
| First install/no account | Install → first-run wizard (existing) gains final "Trial" step: "14 days, everything local, nothing leaves your machine" → app opens in trial | Clock tamper (trial file signed w/ first-seen time; skew >48h → soft warning, never brick); corporate machines w/o internet: trial works fully offline | First-run wizard step; trial status chip in Settings | Local: signed trial file (machine hash, start date) | Zero network calls required; time-to-first-capture < 60s; no email field anywhere |
| Start trial | Automatic on first run (no explicit "start") | Reinstall reset: accepted for beta (see packaging); second reset attempt shows honest "trial was extended already" | None (implicit) — trial end date shown in Settings > Account & Billing | Same trial file | User never sees a trial dialog until day 11 (banner), day 14 (gate) |
| Buy Local license | In-app "Buy Octadock" → opens Paddle checkout (web) → email with license key → paste key in Settings (or deep-link `octadock://activate?key=…` from thank-you page) → activation call → signed entitlement file written → "Licensed" state | Payment declined (Paddle handles); email typo (Paddle receipt resend); key not arrived (resend link + spam note); activation offline (queue + retry with visible pending state); key already at 3 devices (see device flow) | Buy button (trial banner, Settings); activation field in Settings > Account & Billing; success toast | Server: order (webhook), key, activation record. Local: entitlement file. **Nothing about the card ever touches Octadock** | Checkout → activated < 2 min; activation succeeds on first try ≥ 99% telemetry-free (measured via support volume); key works without creating any account |
| Activate second device | Enter same key on device 2 → activation 2/3 recorded → entitlement file written | Limit reached → dialog lists active devices (name + last seen) with "Deactivate one" self-serve; license revoked → clear error + support link | Settings > Account & Billing device list | Server: activation rows (device hash, friendly name, timestamps) | Self-serve deactivation requires no support contact; device list never shows other buyers' data |
| Offline usage | Licensed app runs forever offline; Pro (later): cached entitlement valid 30 days offline, then grace banner | Grace expiry (Pro): local features keep working, hosted features already need internet; entitlement refresh fails while online → retry with backoff, keep cached state | Offline chip in Account & Billing ("Offline — license valid"); grace banner (Pro) | Local: signed entitlement w/ issued-at + grace window | Airplane-mode laptop for 6 months: zero nags for Local licensees |
| Upgrade to Pro (post-beta) | Settings > Account & Billing "Pro" card → Paddle subscription checkout → webhook issues Pro entitlement → app refreshes → meters appear | Already-Local owner: same flow, price identical, Local ownership noted ("your Local license stays yours"); checkout abandoned → no state change | Pro card; usage meters appear on success | Server: subscription mirror + entitlement. Local: refreshed entitlement file | Upgrade never requires reinstall or re-activation of Local |
| Cancel Pro | Paddle portal (magic link from Settings) → cancel → runs to period end → app entitlement downgrades at expiry | Cancel-then-resubscribe (new period, fine); cancel with unused top-ups (top-ups keep working for hosted till spent/expired) | Settings link "Manage subscription" → portal; post-expiry state card | Server: subscription state. Local: entitlement refresh | Cancellation reachable in ≤ 2 clicks from Settings; no retention dark patterns |
| Payment failed/past due (Pro) | Paddle dunning emails + retries; app shows **calm amber inline banner** "Payment issue — Pro paused in N days" during Paddle's retry window; hosted features keep working during window | Retries exhausted → Pro lapses to owned tier; fixed payment → auto-restore | Banner in Settings + one non-modal toast; never captures-blocking | Server: `past_due` mirror. Local: entitlement flag | Zero modals; local features untouched throughout |
| Credits exhausted | 80%: meter turns amber + one toast. 100%: hosted call returns quota error → inline state on the feature ("Monthly credits used") with \[Top up] \[Switch to local] \[Use your key (if configured)] | Mid-job exhaustion (finish in-flight job, meter may slightly overrun — absorb overage ≤ 5%); top-up purchase fails (Paddle retry) | Usage meter (Settings + inline on speech features); exhausted inline state | Server: usage ledger. Local: cached meter snapshot | Hosted call never silently downgrades or silently bills; fallback path always one click |
| BYO-key setup | Settings > Cloud Providers → provider row → paste key → live "Test" call → green "Your key" badge → per-feature cloud toggles unlocked | Invalid key (verbatim provider error + status-page link); key with wrong scopes; env-var keys detected → banner offering one-click migration to Credential Manager; key removal → features fall back to local/Pro | Settings > Cloud Providers | Local only: **Windows Credential Manager (DPAPI)** — never the SQLite settings table, never sync, never logs. Env vars still honored (power users) but UI-stored keys take precedence | Key never appears in logs/DB (test asserts); test call round-trips < 5s; removing a key leaves no residue |
| Refund/revocation | Refund via Paddle (MoR policy, 14-day default) → webhook → license revoked server-side → next online check flips app to revoked state → trial-expired behavior (data intact, export forever) | Chargeback (same path + abuse flag); refund after heavy hosted use (Pro: credits consumed are non-refundable per policy — Paddle policy config); offline machine post-refund (revocation lands on next connect; accepted exposure) | Revoked state card (rose, factual, support link — no shaming copy) | Server: revocation row + reason. Local: entitlement invalidated | Refund → revoked within 1 webhook cycle; user's own data never locked or deleted |
| Account recovery | "Lost your key?" → enter purchase email → license service emails all keys for that address (via Paddle order lookup) | Email no longer accessible → support-mediated re-issue (revoke old key, issue new, ID = order reference); key leaked publicly → same revoke+reissue | Recovery link on activation field + homepage footer | Server: resend log (rate-limited) | Self-serve recovery for the 95% case; no account, no password, ever |

### Entitlement States

| State | User Meaning | Allowed Features | Blocked Features | UI Treatment | Backend/Local Requirement |
| --- | --- | --- | --- | --- | --- |
| No account | Fresh machine, trial running | Everything local + BYO-key | Octadock-hosted usage | No chrome at all; trial end date only in Settings | Signed trial file, local clock policy |
| Trial active | Evaluating | Same as above | Same | Day-11 banner (dismissible), day-14 gate | Same |
| Trial expired | Decided-not-yet | View/export/copy all existing data; annotate existing items; settings | New captures, OCR, dictation, read-aloud, new-file preview, new pins | Calm full-width inline gate on capture surfaces: "Trial ended — Buy $49 / Enter key"; **never a modal on launch** | Local expiry check; no server |
| Local license active | Owner | Everything local + BYO | Octadock-hosted usage (offers Pro waitlist/upsell quietly) | "Licensed to <email>" card; nothing else | Signed entitlement file; activation record server-side |
| Update entitlement expired | Owner, updates ended | Everything they had | New app versions only | Quiet card in Settings: "Updates ended <date> — app fully yours. Renew $19/yr"; updater shows latest-eligible version | Entitlement carries `updates_until`; updater compares build date |
| Pro active | Subscriber | Everything + hosted credits | — | Violet "Pro" badge in Settings; meters visible | Subscription mirror; entitlement refresh ≤ 24h when online |
| Pro past due | Payment hiccup | Everything incl. hosted (during Paddle retry window) | — | Amber inline banner with fix link | `past_due` flag; countdown from webhook |
| Pro canceled | Ending by choice | Pro until period end, then owned tier | Hosted after end | Neutral card "Pro until <date>" | Entitlement `ends_at` |
| Offline grace | Traveling/airgapped | Local: everything, forever. Pro: cached entitlement honored 30 days | Hosted (needs network anyway) | "Offline — license valid" chip; Pro grace countdown only if < 7 days left | Signed entitlement w/ grace window; no hard lock on expiry — degrade to Local-active behavior |
| Device limit reached | 4th machine | Activation blocked on this device only | — | Device-list dialog with self-serve deactivate | Server activation registry |
| BYO-key configured | Power user | Cloud features routed via their key | — | "Your key" badge per provider; per-feature cloud toggles | Credential Manager storage; no metering |
| Hosted credits exhausted | Heavy month | Everything local + BYO | Octadock-hosted calls until top-up/reset | Meter at 100% (rose), inline exhausted state with 3 exits | Server hard stop; local cached snapshot |
| License revoked/refunded | Refund/chargeback processed | Trial-expired behavior (view/export forever) | New activity | Rose factual card + support link | Revocation list; checked opportunistically online |

### Account/Billing UI Inventory

| Surface | Purpose | Entry Point | Required Controls | Empty/Error States | Copy Notes |
| --- | --- | --- | --- | --- | --- |
| First-run activation | Set trial expectation; offer key entry for owners | First-run wizard final step | "Start trial" (primary), "I have a license key" (quiet), privacy one-liner | Key invalid/offline-queue states | "Everything runs on your PC. 14 days, all features, no account." |
| Settings > Account & Billing | Single home for plan, key, devices, updates, (later) Pro + meters | New Settings section "Account" | Plan status card; key entry/display (masked, copy); device list w/ deactivate; update-entitlement card + renew link; Buy/Pro cards; "Lost your key?"; offline chip | No key yet (trial card); revoked (rose card); pending activation (spinner + queued) | Never says "sign in" — says "enter your license key". Factual, no urgency theater |
| Settings > Cloud Providers | BYO keys + cloud consent in one place | New Settings section (evolves current Speech provider manager) | Per-provider rows (OpenAI, ElevenLabs, +): masked key field, Test, remove; "Your key" badge; per-feature cloud toggles (read-aloud, transcription); "what gets sent" links; env-var migration banner | No keys (explainer + provider links); test failed (verbatim error, status-page link) | "Your keys stay in Windows Credential Manager on this PC. Octadock never sees them." |
| Usage meter | Show Pro credit consumption honestly | Settings > Account & Billing + inline on hosted features | Per-meter bar + numbers ("184 / 300 min"), reset date, top-up button, per-meter tooltip breakdown | Zero usage; exhausted (rose + 3 exits); stale-offline ("as of <time>") | Units in plain language ("minutes", "characters") — never opaque "credits" alone |
| Upgrade/gate banner | Explain a gate without blocking work | Inline where a hosted feature would be | Lock glyph, one sentence, \[See Pro]/\[Join waitlist] + \[Use your key] when applicable, dismiss | Beta variant: "Pro isn't for sale yet — join waitlist" | Never modal for discovery; never interrupts a capture in progress |
| Cloud-send confirmation | Explicit consent before bytes leave the machine | First use of any cloud route (Pro or BYO) | "What will be sent" detail (file/duration/text size), destination badge (provider name), retention note link, "Always allow for <feature>" checkbox, confirm button labeled with destination ("Send to OpenAI") | Declined → falls back to local provider if one exists | The confirm button names the recipient. No generic "OK" |
| License renewal state | Update entitlement ended | Settings card + updater dialog | Renew ($19) link, "what you keep" line, changelog link of missed versions | Renewal checkout failed (Paddle retry) | "Your app keeps working. Renewing gets you new features." — no fear copy |
| Offline state | Reassure, not nag | Status chip in Account & Billing; grace banner (Pro only, < 7 days) | None (informational) | Pro grace expired → downgrade note | "Offline — your license is valid on this PC." |

## Account Requirement Matrix

| Workflow | No Account | Local License | Pro Account | Notes |
| --- | --- | --- | --- | --- |
| Install/open app | ✅ | ✅ | ✅ | Never gated |
| Capture/shelf/pin/annotate | ✅ (trial) | ✅ | ✅ | Local forever; trial-expired = view/export only |
| Local OCR | ✅ (trial) | ✅ | ✅ | Windows.Media.Ocr, on-device |
| Local STT/read aloud | ✅ (trial) | ✅ | ✅ | Parakeet/Whisper + Windows voices, on-device |
| File preview | ✅ (trial) | ✅ | ✅ | Incl. PDF render/zip when shipped |
| Context package export | ✅ (trial) | ✅ | ✅ | Local compute rule; wargame-#1 export preview applies |
| BYO-key cloud use | ✅ | ✅ | ✅ | Requires internet + vendor account, **no Octadock account**; explicit consent flow |
| Hosted transcription | ❌ | ❌ (waitlist CTA) | ✅ | Needs Pro entitlement + internet; still no first-party account (Paddle portal identity) |
| Premium voices (hosted) | ❌ | ❌ | v1.5 credits | BYO-key route available to all tiers at launch |
| Future team/admin | ❌ | ❌ | ❌ (Team plan later) | First feature that genuinely requires first-party accounts — build auth then, not now |

## Feature Gate Matrix

Gate line: **local compute → Local license · Octadock-hosted compute → Pro · your-vendor compute → BYO-key.** One sentence, explains every row. Wargame-#1 scope corrections applied (nothing unbuilt is charged for).

| Feature | Free/Beta | Local License | Pro | BYO-Key | Team Later | Not V1 | Rationale |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Capture modes (area/window/full/scroll/OCR/record) | Trial | ✅ | ✅ | — | Policy control | — | Table stakes; ShareX has all of it free — never gate within local |
| Capture Shelf | Trial | ✅ | ✅ | — | — | — | Core identity |
| Annotation (+ image writeback w/ revisions) | Trial | ✅ | ✅ | — | — | PDF/Office writeback (per wargame #1) | Writeback rides the revision layer; images only in v1 |
| Pins | Trial | ✅ | ✅ | — | — | — | Signature desktop-object feature |
| History | Trial | ✅ | ✅ | — | Org retention policy | — | Local SQLite |
| Local OCR | Trial | ✅ | ✅ | — | — | — | On-device, zero COGS |
| Local dictation (Parakeet/Whisper) | Trial | ✅ | ✅ | — | — | — | Local models; the "usable without cloud" proof point |
| OpenAI/hosted STT | — | Waitlist CTA | ✅ 300 min/mo (hi-acc 2×) | ✅ user's key | Pooled quota | Hosted proxy is not built yet → Pro waits | Recurring COGS; the core Pro value |
| Premium TTS (ElevenLabs-class) | — | — | v1.5 credits (20k chars) | ✅ user's key | — | Hosted TTS credits at launch | $0.05–0.10/1k chars verified; BYO first, credits when metering matures |
| File preview (text/CSV/JSON/MD/images) | Trial | ✅ | ✅ | — | — | — | Exists today |
| PDF preview | Trial | ✅ | ✅ | — | — | Writeback | Render-only per wargame #1 |
| Office preview | Trial | ✅ | ✅ | — | — | Rendered-page fidelity | Extracted-content preview per wargame #1 |
| Context package export | Trial | ✅ | ✅ | — | Shared libraries | — | Local packaging incl. export-preview/redaction from wargame #1 |
| Future AI context (summarize/explain over packages) | — | — | ✅ credits when built | ✅ user's key | Admin policy | ✅ not in v1 | Roadmap only; homepage must say "in development" |

## Hosted Usage and Metering

All hosted calls proxy through an Octadock cloud endpoint that checks entitlement + meters **before** dispatch to the vendor. BYO-key calls go direct from the app to the vendor and are never metered server-side (local session counters only, labeled "estimate").

| Meter | Unit | Applies To | Included Quota | Warning Threshold | Hard Stop | BYO-Key Behavior |
| --- | --- | --- | --- | --- | --- | --- |
| Cloud transcription | seconds (displayed as minutes) | Pro hosted STT (mini-class default) | 300 min/mo | 80% (amber + one toast) | 100% + in-flight job completes (absorb ≤ 5% overrun) | Unmetered; direct to vendor; local estimate shown |
| High-accuracy transcription | seconds ×2 weight | Pro hosted STT (`gpt-4o-transcribe`-class toggle) | Shares the 300-min pool at 2× | Same pool | Same pool | Same |
| AI summarize/explain | tokens in/out → displayed as "AI credits" | Pro, when built (v1.5+) | TBD at build time (price from live token rates then) | 80% | 100% | Same |
| Premium TTS | characters | Pro v1.5 (Flash-class 1×, Multilingual-class 2×) | 20k chars/mo | 80% | 100% | Same |
| Future hosted storage | GB-month | Team/sharing later | — | — | — | n/a |

Abuse/runaway-bill controls (server): max audio duration per request 4 h; max concurrent hosted jobs per license 2; per-device rate limit; monthly hard cap = quota + purchased top-ups (no unbounded overage ever); global vendor-spend circuit breaker with alerting; anomaly flag (single license > 3× median) → manual review; signed requests bound to entitlement + device hash so leaked Pro tokens die with revocation. Top-ups: $5 ≈ 250 std minutes (pending Paddle sub-$10 confirmation; fallback $10 pack), valid 12 months, survive cancellation for hosted use.

## Payment Provider Recommendation

- **Recommended v1 provider: Paddle** (merchant of record).
- **Why:** MoR removes global VAT/sales-tax registration and remittance from a solo Windows founder — decisive at this stage. Paddle is verified live (5% + 50¢, one-time + subscriptions under one fee). The natural alternative, Lemon Squeezy, is verified to be mid-absorption into Stripe Managed Payments (public preview Feb 2026): its official pages are already bot-walled, and new merchants would be building on a platform whose successor is in preview — wrong risk for a launch. Stripe classic is cheaper (2.9% + 30¢ + 0.7% Billing + 0.5% Tax) but makes Octadock the merchant of record for global tax — not worth it at beta volume.
- **What it handles:** checkout, global tax, subscriptions + dunning, one-time purchases, refunds/chargebacks, customer portal (the "account" UI Octadock doesn't build), receipt emails, EU/UK compliance.
- **What Octadock still must build:** a **small first-party license service** (Paddle has no native license-key/activation product): webhook consumer (order/refund/subscription events), key issuance + email, activation registry (3-device), signed entitlement file issuance (Ed25519), revocation list, resend-key endpoint, Pro entitlement refresh endpoint, usage-metering ledger (Pro phase). Estimated scope: one small service + ~6 endpoints + a signing key ceremony. This service is wanted regardless of provider (offline entitlements and device policy are product requirements no MoR provides).
- **When to revisit:** at Pro launch (usage billing pressure) and when **Stripe Managed Payments exits preview** — it is MoR at 3.5%/txn (verified) with Stripe Billing's metering underneath; migrating Paddle→Stripe-MP later is a checkout swap, not an entitlement rewrite, because entitlements live in Octadock's own service.
- **Risks:** Paddle's "products under $10 need custom pricing" note vs the $5 top-up SKU (confirm before promising top-ups; fallback $10 packs); Paddle checkout availability in some regions; MoR refund policies constrain custom refund rules (consumed-credits carve-out must be configured within Paddle's policy); provider dependency for the resend-key lookup (mitigate: mirror order email→key mapping in the license service at webhook time).

## Entitlement Architecture

- **License states:** `trial(start,expiry,extended)` → `local_active(updates_until)` → `updates_expired` (still active) · `revoked(reason)` · orthogonal flag `beta_pricelock`.
- **Subscription states (Pro):** `pro_active(period_end)` · `pro_past_due(retry_until)` · `pro_canceled(ends_at)` · `pro_lapsed` — mirrored from Paddle webhooks into the license service; the app only ever reads Octadock-signed entitlements, never Paddle directly.
- **Offline grace:** Local = infinite after activation (signed file, no expiry beyond `updates_until` which never disables the app). Pro = entitlement file carries `refreshed_at` + 30-day grace; on expiry degrade to owned tier, never lock. Clock skew tolerance ±48 h on all signature validity checks.
- **Device activation:** activation = `(license_key, machine_hash, friendly_name)` → registry row + signed entitlement bound to machine hash. 3 concurrent; self-serve deactivate; deactivation invalidates that device's entitlement on next online contact.
- **Account recovery:** email-based key resend (license-service mirror of Paddle orders, rate-limited); support-mediated revoke+reissue for email loss/leak. No passwords exist to reset.
- **Local secure storage:** entitlement + trial files: signed JSON in `%LOCALAPPDATA%\Octadock\license\` (tamper = invalid = trial rules; no DRM arms race beyond signature). BYO keys: **Windows Credential Manager via DPAPI**, keyed per provider; never in `octadock.db`, never in logs (assert in tests); existing env-var path (`OCTADOCK_ELEVENLABS_API_KEY` etc., `ElevenLabsTtsProvider.cs:10-24`) remains honored for power users with UI-stored keys taking precedence.
- **Server-side requirements:** webhook consumer (idempotent, replay-safe), key issuance, activation registry, entitlement signer (Ed25519, key rotation plan), revocation list, resend endpoint, daily Paddle reconciliation poll (webhook-loss backstop), usage ledger (append-only: `license_id, meter, qty, device_hash, request_id, ts`) + quota evaluator (Pro phase), minimal ops dashboard (orders, activations, flags).
- **Events to log:** server — `order.created/refunded`, `license.issued/activated/deactivated/revoked`, `entitlement.refreshed`, `meter.usage`, `abuse.flagged`, `resend.requested`. Local (in existing Serilog, Information level) — activation state changes, consent grants/denials, quota warnings shown, entitlement validation failures. **No behavioral telemetry**; this list is the entirety of what the license system knows.
- **Failure modes:** license service down → app fails **open** for cached valid entitlements, fails **closed** for new activations (visible queued-retry state + support link); webhook loss → reconciliation poll catches within 24 h; signing-key compromise → rotate + re-sign on next refresh, old-key acceptance window 60 days; Paddle outage → checkout down but every existing user unaffected (entitlements are Octadock-signed); refunded-but-offline machine → revocation lands on next connect (accepted bounded exposure).

## Design System Verdict

### Brand Principles

- **Local-first is visible, not claimed.** Cloud is a *labeled, colored, consented* exception (violet + provider badge); everything else is presumed local. The UI itself is the privacy policy.
- **Instrument, not dashboard.** Octadock surfaces are dense, calibrated tools — hairline borders, precise rows, quiet chrome. No marketing gradients inside utility UI; no cards-in-cards.
- **One accent means something.** Teal = interaction and "yours/local". Violet = cloud/Pro tier. Status colors only for status. If everything glows, nothing does — each surface gets one gradient moment at most (its primary action).
- **Same bones on app and web.** The homepage is the instrument at display scale: same palette, same radii logic, same icon family; only type scale and spacing multiply.
- **Honest states over marketing states.** Trial, gated, offline, quota, revoked — every commercial state has a designed, calm, non-modal treatment. Never dark-pattern urgency.

### Visual Direction

- **Name:** **Obsidian Instrument**.
- **One-sentence description:** Deep blue-black glass surfaces with hairline edges and disciplined teal instrumentation, warmed by a neutral fog ramp and a reserved violet cloud tier — a Windows utility that reads like a precision instrument, on desktop and on the homepage.
- **What it keeps from current obsidian glass:** the obsidian base ramp (#0C1220/#141E32), teal #2DD4BF identity + teal→cyan gradient as the *single* brand flourish, hairline `GlassBorder` (#30FFFFFF) on floating surfaces, dark-always glass family (dock/shelf/pins/HUD/preview stay dark in both themes), Segoe UI Variable, the existing `Octadock.Brush.*` token discipline.
- **What it changes:** adds a deeper canvas (#070B14) and an overlay step (#1B2942) so elevation is real; adds Fog neutrals so secondary UI stops being blue-tinted teal; introduces violet as the cloud/Pro semantic; restricts the accent gradient to one primary action per surface; replaces the 20/16/14/13 ramp with a full 8-step ramp incl. mono; tightens dense-surface radii (rows 8, dense cards 10, dialogs 12); one icon family (Lucide) replacing MDL2 + text glyphs; adds designed billing/entitlement states.
- **What it explicitly forbids:** teal as a background wash for large areas; the accent gradient on more than one control per surface; icon-font/text-symbol glyphs; cards inside cards; marketing hero styling inside app windows; viewport-scaled fonts; urgency/scarcity patterns in commercial states; underwater/mascot octopus treatments.

### Primitive Palette

| Primitive | Hex | Role | Notes |
| --- | --- | --- | --- |
| Obsidian 950 | `#070B14` | App canvas (dark), scrims | New — one step under current Surface |
| Obsidian 900 | `#0C1220` | Surface (dark) | Existing `Octadock.Color.Surface` value, kept |
| Obsidian 850 | `#0F182A` | Inputs, wells | Existing `InputBackground` value, promoted to ramp |
| Obsidian 800 | `#141E32` | Raised surface / cards (dark) | Existing `SurfaceAlt` value, kept |
| Obsidian 700 | `#1B2942` | Overlay/popup/hover-raise (dark) | New elevation step |
| Obsidian 600 | `#25334E` | Borders (dark) | Existing `Border` value, kept |
| Slate/Fog neutral | `#F2F6FC` / `#B6C2D4` / `#8DA0BC` / `#5B6B84` | Text primary / secondary-strong / secondary / tertiary-disabled (dark) | Fog 100/300/400/600; 300 is new (was missing — everything fell to 400) |
| Teal primary | `#2DD4BF` (600 `#0D9488`, 300 `#5EEAD4`, 700 `#0F766E`) | Accent, selection, focus, "local" identity | Existing values kept; 700 = light-theme accent |
| Cyan secondary | `#38BDF8` (600 `#0284C7`) | Gradient partner, info | Existing gradient end, now also Info token |
| Secondary accent (violet) | `#A78BFA` (300 `#C4B5FD`, 600 `#7C3AED`) | **Cloud/Pro tier, gates, upgrade surfaces** | New — the anti-one-note move; never used for product features |
| Success | `#4ADE80` (600 `#16A34A`, 200 `#BBF7D0`) | Saved/synced/valid | Existing 400/600 kept |
| Warning | `#FBBF24` (600 `#D97706`, 200 `#FDE68A`) | Quota 80%, past-due, caution | Existing kept |
| Danger | `#FB7185` (600 `#DC2626`, 200 `#FECDD3`) | Destructive, revoked, quota 100% | Existing kept |
| Info | `#38BDF8` (600 `#0284C7`) | Neutral notices | Shares cyan primitive deliberately |
| Glass alphas | `#30FFFFFF` border · `#14FFFFFF` hover · `#26FFFFFF` pressed · `rgba(12,18,32,0.78)` glass fill | Floating-surface recipe | Existing values, now named as a recipe |

### Semantic Color Tokens

WPF keeps the `Octadock.Brush.*` convention (additions, three re-points, no deletions — see migration). Web uses `--od-*`.

| Purpose | WPF Token | CSS Variable | Dark Hex | Light Hex | Usage |
| --- | --- | --- | --- | --- | --- |
| App background | `Octadock.Brush.Canvas` *(new)* | `--od-bg-canvas` | `#070B14` | `#EDF1F8` | Window/page backdrop; replaces `WindowBackdrop` gradient |
| Surface | `Octadock.Brush.Surface` | `--od-bg-surface` | `#0C1220` | `#F3F6FB` | Panels, sidebars, inline sections |
| Surface raised | `Octadock.Brush.SurfaceRaised` *(new; `SurfaceAlt` aliased)* | `--od-bg-raised` | `#141E32` | `#FFFFFF` | Cards, rows, popovers |
| Surface overlay | `Octadock.Brush.SurfaceOverlay` *(new)* | `--od-bg-overlay` | `#1B2942` | `#FFFFFF` + e2 shadow | Menus, dropdowns, hover-raise, tooltips |
| Border | `Octadock.Brush.Border` | `--od-border` | `#25334E` | `#DAE2EE` | Hairline structure |
| Border strong | `Octadock.Brush.BorderStrong` *(new)* | `--od-border-strong` | `#33455F` | `#B9C6D9` | Input hover, table header rules |
| Text primary | `Octadock.Brush.Text` | `--od-text` | `#F2F6FC` | `#0D1526` | Body, labels |
| Text secondary | `Octadock.Brush.TextSecondary` *(new; `TextMuted` aliased)* | `--od-text-2` | `#8DA0BC` | `#5B6B84` | Metadata, captions (6.3:1 dark / 5.4:1 light — passes) |
| Text tertiary/disabled | `Octadock.Brush.TextDisabled` *(new)* | `--od-text-disabled` | `#5B6B84` | `#93A3BA` | Disabled labels (≥3:1 target) |
| Accent primary | `Octadock.Brush.Accent` | `--od-accent` | `#2DD4BF` | `#0F766E` | Local actions, selection, toggles |
| On-accent | `Octadock.Brush.AccentText` | `--od-on-accent` | `#04201C` | `#FFFFFF` | Text/icons on accent fills (9.2:1 / 5.5:1) |
| Accent soft | `Octadock.Brush.AccentSoft` | `--od-accent-soft` | `#1F2DD4BF` (12% teal) | `#1A0F766E` | Selected rows, chips |
| Accent secondary | `Octadock.Brush.AccentSecondary` *(new)* | `--od-accent-2` | `#38BDF8` | `#0284C7` | Gradient partner, links on dark |
| Success | `Octadock.Brush.Success` (+`SuccessSoft` new) | `--od-success`, `--od-success-soft` | `#4ADE80` / 14% wash | `#16A34A` / 10% wash | Saved/synced badge, toasts |
| Warning | `Octadock.Brush.Warning` (+`WarningSoft` new) | `--od-warning`, `--od-warning-soft` | `#FBBF24` / 14% wash | `#D97706` / 10% wash | Quota 80%, past-due banner |
| Danger | `Octadock.Brush.Danger` (+`DangerSoft` new) | `--od-danger`, `--od-danger-soft` | `#FB7185` / 14% wash | `#DC2626` / 10% wash | Destructive, revoked, quota 100% |
| Info | `Octadock.Brush.Info` *(new)* | `--od-info` | `#38BDF8` | `#0284C7` | Neutral notices, offline chip |
| Focus ring | `Octadock.Brush.FocusRing` | `--od-focus` | `#5EEAD4` | `#0D9488` | 2px outer ring, 2px offset (12.8:1 / 3.4:1 vs surface) |
| Gated/locked | `Octadock.Brush.Gated` *(new)* | `--od-gated` | `#8DA0BC` @ text + lock glyph, surface `#141E32` @ 60% desat overlay | mirrored | Locked feature tiles; pairs with Cloud tokens on gate banners |
| Cloud/Pro | `Octadock.Brush.Cloud` (+`CloudSoft` new) | `--od-cloud`, `--od-cloud-soft` | `#A78BFA` / `#1FA78BFA` (12%) | `#7C3AED` / 8% wash | Pro badge, gate banners, meters' plan chrome, provider badges (6.1:1 on raised) |
| BYO-key | `Octadock.Brush.ByoKey` *(new)* | `--od-byo` | `#B6C2D4` chip + key glyph, teal text accent | `#5B6B84` chip | "Your key" badges — deliberately *neutral*, not violet: user's own cloud is not an upsell |
| Quota warning | alias → `Warning` | alias → `--od-warning` | — | — | Meters at 80–99% |

**Signature gradient** `Octadock.Brush.AccentGradient` / `--od-gradient-brand`: `#2DD4BF → #38BDF8` (135°). Allowed: one primary CTA per surface, dock breathing dot, homepage hero moments, selected-tab indicator bar. Forbidden everywhere else. **No violet gradient exists** — cloud tier is always solid.

### Contrast Requirements

Computed (WCAG 2.1 relative luminance) for the dark theme against stated backgrounds; light theme spot-values in notes.

| Pair | Minimum Ratio | Proposed Ratio | Pass/Fail | Notes |
| --- | --- | --- | --- | --- |
| Primary text on app background (`#F2F6FC` on `#070B14`) | 4.5:1 | ≈ 18:1 | Pass | Light: `#0D1526` on `#EDF1F8` ≈ 15:1 |
| Metadata text on raised surface (`#8DA0BC` on `#141E32`) | 4.5:1 | ≈ 6.3:1 | Pass | The reference-image grays that fail must map to this token, not darker; light `#5B6B84` on white ≈ 5.4:1 |
| Accent text on accent fill (`#04201C` on `#2DD4BF`) | 4.5:1 | ≈ 9.2:1 | Pass | Light: white on `#0F766E` ≈ 5.5:1 |
| Warning text on warning surface (`#FDE68A` on 14% amber over `#141E32`) | 4.5:1 | ≈ 12:1 | Pass | Body text on soft washes always uses the 200-level status tint |
| Danger text on danger surface (`#FECDD3` on 14% rose wash) | 4.5:1 | ≈ 10:1 | Pass | — |
| Disabled text on surface (`#5B6B84` on `#141E32`) | 3:1 (target; WCAG-exempt) | ≈ 3.1:1 | Pass | Deliberately sub-4.5 to read as disabled |
| Focus ring on surface (`#5EEAD4` on `#0C1220`) | 3:1 | ≈ 12.8:1 | Pass | Light `#0D9488` on `#F3F6FB` ≈ 3.4:1 — passes 3:1 non-text |
| Cloud/Pro text on raised (`#A78BFA` on `#141E32`) | 4.5:1 | ≈ 6.1:1 | Pass | Light `#7C3AED` on white ≈ 5.8:1 |

Rules: any new token pairing must be checked at spec time, not QA time; icon-only glyphs follow the 3:1 non-text minimum; the 200-level status tints are the only legal body-text colors on soft status washes.

### Typography

App = fixed pixel ramp (no viewport scaling — wargame #1 rule). Web = same roles, Inter variable (Segoe UI Variable is not web-licensable), fluid only at Display/Title on marketing pages.

| Role | Font | Size | Weight | Line Height | Usage |
| --- | --- | --- | --- | --- | --- |
| Display | Segoe UI Variable Display / Inter | 28 | 600 | 36 | First-run, empty states, homepage h2 (hero h1 web: 44–64 fluid) |
| Title | Segoe UI Variable Display / Inter | 20 | 600 | 28 | Window titles, settings page title (existing `Text.Title`) |
| Section | Segoe UI Variable / Inter | 16 | 600 | 24 | Card/section headers (existing `SectionHeader`) |
| Subtitle | Segoe UI Variable / Inter | 14 | 600 | 20 | Row titles, dialog headers (existing `Text.Subtitle`) |
| Body | Segoe UI Variable / Inter | 13 | 400 | 20 | Default UI text (existing `Text.Body`) |
| Caption | Segoe UI Variable / Inter | 12 | 400 | 16 | Metadata, tooltips, meter labels |
| Micro | Segoe UI Variable / Inter | 11 | 500, +0.4px tracking, uppercase optional | 14 | Badges, column headers, status chips |
| Mono | Cascadia Mono / Cascadia Code (web fallback: ui-monospace) | 12 | 400 | 18 | Paths, OCR preview, CSV cells, license keys, "what will be sent" details |

Numerals: tabular (`Typography.NumeralAlignment`/`font-variant-numeric: tabular-nums`) in meters, tables, timers. Minimum UI text 11px; body never below 13.

### Spacing, Radius, Elevation, Motion

- **Spacing scale (4px base):** `2, 4, 6, 8, 12, 16, 20, 24, 32, 40, 48` → WPF `Octadock.Space.05…12` (Thickness resources), CSS `--od-space-*`. Rules: icon↔label 6–8; row internal padding 12; card padding 16 (dense 12 — current `Pad.Card` stays for dense); section gap 24; window margin 24. Layout grid: standard windows content max 760px; settings sidebar 200px; web 12-col, 1200px container, 24px gutters.
- **Radius scale:** `r1 4` (chips/badges) · `r2 6` (buttons/inputs — existing `Corner`) · `r3 8` (rows, pill nav — existing `Corner.Pill`) · `r4 10` (dense cards: settings cards, preview cards) · `r5 12` (floating panels, dialogs — existing `Corner.Large`, now scoped) · `r6 16` (HUD tiles, homepage cards) · `rFull 999` (dock capsule, dots, meter bars). Dense utility rows never exceed r3; the 12px-everywhere look is retired.
- **Border widths:** hairline 1px everywhere; 2px only for focus ring and the selected-state border on shelf/context rows; never 1.5px (DPI shimmer).
- **Elevation/shadow rules:** `e0` flat (border only, in-window sections) · `e1` card `0 1 2 rgba(0,0,0,.28)` + border · `e2` floating panel (shelf, preview cards, HUD) `0 8 24 rgba(0,0,0,.36)` + `GlassBorder` hairline · `e3` overlay (dialogs, pins, toasts) `0 16 48 rgba(0,0,0,.50)`. Glass recipe (floating family only): fill `rgba(12,18,32,0.78)` + 1px `#30FFFFFF` + e2/e3; WPF has no true backdrop blur on borderless windows — this pseudo-glass is the spec, do not chase DWM blur hacks. Standard windows are opaque.
- **Motion durations/easing:** `fast 100ms` (hover-out; hover-in is instant), `state 150ms` (press, toggle, check), `enter 200ms` (row/toast fade+4px rise), `panel 250ms` (shelf/HUD/preview enter: fade + 8px translate or 0.98 scale), `settle 350ms` (shelf stack reflow). Easing: decelerate `cubic-bezier(0.2, 0, 0, 1)` (WPF KeySpline `0.2,0 0,1`) for enters; accelerate `(0.4, 0, 1, 1)` for exits. Tokens: `Octadock.Duration.*` / `--od-dur-*`, `--od-ease-*`.
- **Reduced motion behavior:** if `prefers-reduced-motion` / Windows "Show animations" off: all translations/scales become opacity-only ≤ 100ms; shelf reflow becomes instant; dock breathing dot freezes at 100%; homepage hero parallax/scroll choreography fully disabled → static render.
- **Reduced transparency behavior:** when transparency effects are off (battery saver, RDP, policy): glass fills swap to solid `#141E32` (`SurfaceRaised`), hairline stays; no functionality or layout changes. Bind once via a `Octadock.Glass.Enabled` resource, not per-control checks.
- **High contrast:** merge a `HighContrast.xaml` that re-points all tokens to `SystemColors`; gradients → solid `SystemColors.Highlight`; soft washes → transparent + 1px border; focus ring 2px `SystemColors.Highlight` everywhere; icons inherit text color. Web: `forced-colors: active` equivalents.

### Iconography

- **Recommended icon family:** **Lucide** (ISC license) as the single family for app + web. Rationale: one geometry set shared across WPF and the homepage; stroke-based 24-grid consistency; removes the Segoe MDL2 private-use-glyph fragility documented in this repo's history (invisible-glyph edit traps); huge coverage incl. lock/cloud/key/meter glyphs the billing UI needs.
- **WPF implementation approach:** build-time generator converts Lucide SVGs → `StreamGeometry` resources in one `OctadockIcons.xaml`; rendered as `Path` with `Stroke={DynamicResource …}` `StrokeThickness=1.75` (scaled: 1.5 at 16px, 1.75 at 20/24px), `StrokeLineCap=Round`. No icon fonts. Window min/max/close chrome stays native/Segoe (OS-owned).
- **Web implementation approach:** `lucide-static` inline SVGs (tree-shaken), `stroke="currentColor"`, sized via `--od-icon-*`.
- **Existing glyphs to replace:** all Segoe MDL2 usages (e.g. the menu checkmark `E73E` in `Shared.xaml:706`), the annotation editor's text-symbol tool glyphs (select/crop/arrow/rect/ellipse/line/text/highlighter/blur/pixelate/counter/freehand — all have Lucide equivalents: `mouse-pointer`, `crop`, `move-up-right`, `square`, `circle`, `minus`, `type`, `highlighter`, `droplets`, `grid-2x2`, `list-ordered`, `pen-tool`), the dock's mixed emoji/text tiles (camera→`camera`, window→`app-window`, screen→`monitor`, scroll→`arrow-down-to-line`, OCR→`scan-text`, read→`audio-lines`, record→`circle-dot`, shelf→`layers`, history→`history`, clip→`clipboard-list`, file→`file-search`, settings→`settings-2`, dictate→`mic`), shelf rail (copy `copy`, save `save`/`download`, annotate `pen-line`, pin `pin`, discard `trash-2`), billing set (lock `lock`, cloud `cloud`, key `key-round`, meter `gauge`, offline `cloud-off`, Pro `sparkles` — used *only* on Pro badge).
- **Icon sizes:** 16 (inline/chips), 20 (rows, rails, buttons — default), 24 (dock, HUD tiles). Touch/click target always ≥ 32×32 regardless of glyph size.
- **Stroke/fill rules:** stroke-only, `Round` caps/joins; no filled variants except the recording dot and status dots (true fills); never mix filled and stroked glyphs in one cluster.
- **Tooltip/screen-reader rules:** every icon-only control has `AutomationProperties.Name` + themed tooltip sourced from the same resource string (single source, no drift); web `aria-label` mirrors it. Tooltips appear 400ms hover / immediately on keyboard focus.

### Components

| Component | Variants | States | Accessibility | Notes |
| --- | --- | --- | --- | --- |
| Icon button | 32×32 default; 28×28 dense (rails); ghost / accent / danger | default, hover (`#14FFFFFF` layer), pressed (`#26FFFFFF`), focus (2px ring), disabled (45% — existing), loading (spinner replaces glyph) | Name + tooltip mandatory; ≥32px hit area even at 28px visual | Existing `Style.IconButton` evolves; keep state-layer-on-top pattern (comment in `Shared.xaml:90` — it's correct) |
| Action rail | Row rail (horizontal, ≤5 + overflow `ellipsis`); toolbar rail (editor) | per-button states + rail reveal: opacity 0→1 on row hover **and row focus-within** | Roving tabindex within rail; arrow-key traversal; overflow menu keyboard-reachable | Wargame-#1 rule codified: max 5 visible, extension-preserving truncation beside it |
| Shelf row | media / OCR-text / recording / external-file; 360w default (320–420), 68h; thumb 84×56 r2 | default, hover, selected (1px teal border + `AccentSoft` wash + left dot), focus ring, pinned badge, OCR chip, in-context chip (teal outline `CTX`), drag-ghost, restore-flash (success wash 350ms) | Row = single focusable item; rail via arrow keys; SR announces name+type+age | Ref-04 geometry; replaces card stack |
| Context row | Same skeleton + include checkbox (18px, left), source badge (Micro chip), notes affordance, drag handle | + included/excluded (excluded = 60% opacity + strikethrough-free "excluded" chip — never hide), reorder-dragging | Checkbox labeled "Include in package"; reorder keyboard: Ctrl+↑/↓ | Distinct header ("Context", `layers-3` icon) + violet-free styling — context is local, stays teal-family |
| Inspector panel | History/library right panel; 320px, `Surface`, e0 + left hairline | loading (skeleton rows), empty, populated, error | Landmark region + heading; fields labeled | Field rows: Caption label + Body value + optional copy button (16px) |
| File preview tab | Preview / Metadata / Text-OCR / Annotations / Context; underline-free pill tabs r3 | default, hover, selected (`AccentSoft` + 3px gradient indicator bar), disabled (no data), loading | Standard tab UIA pattern; Ctrl+Tab cycles | Kill the "AI Analyze" button from ref-02 CSV panel |
| Toast/banner | Toast (320w, e3, r3, bottom-right, max 3, 5s; errors sticky) · Inline banner (full-width row, r3, soft wash + status-600 left bar 3px) | info/success/warning/danger/cloud variants; with/without action | `role=status` (toast) / `role=note`; never steals focus; actionable via F6 region | Banners are the *default* for commercial states; toasts only for transient confirmations |
| Pricing card (web + app plans view) | Local (one-time) / Pro (violet top border 2px + `CloudSoft` badge "Cloud") / Team (ghost, "later") | default, hover (e1→e2, 150ms), featured, current-plan (success chip "Your plan") | Card = `<article>` with h3; price + period in one SR sentence | Web 320w r5; app 280w r4 denser; **no gradient fills — gradient budget goes to the single Buy button** |
| Account status badge | Trial (fog) / Licensed (teal outline) / Pro (violet fill `#A78BFA`, on-violet text `#0C1220`) / Past due (amber) / Revoked (rose) / Offline (info outline) | static | Text + color (never color-only); Micro type | One badge component everywhere: settings, about, gate banners |
| Usage meter | Inline (140×4) / full (240×6 + labels); r-full | normal (teal fill), warning ≥80% (amber), exhausted (rose + top-up link), stale-offline (striped + "as of" caption), loading | `role=meter` with value/min/max; text alternative always visible ("184 of 300 min") | Tabular numerals; resets caption "Resets Aug 1" |
| Pro gate banner | Inline row 44h, `CloudSoft` wash, `lock` 16px, violet left bar 3px; beta variant (waitlist) | default, dismissed (per-feature remembered), hover on actions | Not a dialog — inline `role=note`; actions are real buttons | Copy pattern: "<Feature> uses Octadock cloud — Pro. \[See Pro] \[Use your key]". Never modal, never blocks local path |
| Cloud-send confirmation | Modal 440w r5 e3; per-feature | first-use (full detail), subsequent (skipped if "always allow"), declined | Focus-trapped dialog; title = "Send to <Provider>?"; detail box mono 12 | Confirm button names destination ("Send to OpenAI"); checkbox "Always allow for Read Aloud"; cancel = local fallback if available |
| Dock capsule | idle (36h pill, breathing dot) / expanded (48h, grouped) | idle, hover-expand (200ms panel ease), active-capture (dot → red recording), dragging, hidden | Expanded dock = toolbar UIA; every tile named; Esc collapses | Groups: \[status] · Capture (4) · Text/Voice (3) · Record · Library (4) · Settings — hairline separators `#30FFFFFF`; **"AI" tile removed** |
| Capture HUD tile | 96×88 r6; icon 24 + label 13/600 + status pill 11 | ready (teal pill), standby (fog pill), active (gradient border 1px), unavailable (disabled 45% + reason tooltip), focus | Grid arrow-key navigation; tile = toggle button | 3×2 grid, 8px gaps; footer chips: Lock Aspect / Fixed Size / Delay / Settings |
| Annotation toolbar | Tool cluster (12 Lucide tools) + swatches + sliders + history + export cluster | tool-selected (`AccentSoft` + 2px teal underline), per-button states | Toolbar UIA pattern; number-key shortcuts announced in tooltips | Replaces text-symbol glyphs; groups separated by hairlines, 8px gaps |
| Settings sidebar/tab | 200w rail; 4 groups + **Account group** (Account & Billing, Cloud Providers); pill items r3 | default, hover, selected (existing `AccentSoft` + 3px gradient indicator — keep), focus, badge (e.g. amber dot on Account when past-due) | Existing `SettingsTabItem` pattern is sound; add `AutomationProperties` names | Group headers: Micro caps fog-400 |
| Modal/dialog | Confirm 440w / content 640w, r5, e3, scrim `#070B14` @ 60% | default, destructive (danger primary btn), loading, error | Focus trap; Esc closes (destructive: explicit only); h2 title | Buttons right-aligned: quiet cancel + one primary (accent or danger — never gradient in dialogs) |

### Surface-by-Surface Application

| Surface | Layout Rules | Components | Token Notes | States To Support | Deprecated Patterns |
| --- | --- | --- | --- | --- | --- |
| Dock | Bottom-center capsule, rFull, glass recipe, 48h expanded; grouped tiles 32×32 icon buttons, 24px glyphs | Dock capsule, icon buttons, status dot | Glass fill + `GlassBorder`; breathing dot = the *only* ambient gradient use | idle/expanded/recording/dictating/paused/dragging | 13-tile flat row; mixed text+icon tiles; "AI" tile; emoji-style glyphs |
| Capture Shelf | Bottom-left stack → ref-04 rows, 360w, 8px row gap, e2 panel r5, header (count pill + Clear all + gear) | Shelf row, action rail, toast (restore) | Rows r3 on panel r5; selection = teal; success flash on restore | all shelf-row states + empty ("Capture something — Win+Shift+C") + trial-expired gate | Card stack with oversized thumbnails; GUID-truncating filename display (keep extension per wargame #1) |
| Context | Separate panel, same row skeleton + include/notes/reorder; footer: item count + est. size + **Export preview** button (wargame-#1 requirement) | Context row, inspector (detail), modal (export preview) | Teal-family only (local feature); violet never appears here | included/excluded/reordering/exporting/empty ("Add from Shelf, History, or Explorer") | Naming "Context Shelf" in UI copy — surface is titled **Context** (wargame-#1 open question resolved) |
| File Preview | Card e2 r4, header (type badge + filename + path caption + rail), tab strip, body per provider | Preview tabs, action rail, gate banner (none in v1 — all local), cloud-send confirm (only if future AI actions added) | Type badge = Micro chip, fog; provider label caption | loading (skeleton), error (inline danger wash), fallback FileInfo, executable-guard banner (danger soft, per wargame #1) | "AI Analyze" button; hardcoded preview colors in code-behind |
| History/Library | Standard window: filter rail 240w + card grid (200px min cell) + inspector 320w; status bar with storage meter | Cards, inspector, usage-style storage meter, toasts | Cards r4 e1; selected = teal border; OCR chip Micro | grid loading/empty/filtered-empty; item selected; bulk-select | Win32 DatePickers (themed replacement — loudest inventory complaint); "Share" button (unscoped — cut) |
| Pin Viewer | Ref-01: top toolbar 40h overlay (auto-hide 2s), bottom metadata strip 28h; compact <640w → 3 icons + overflow | Icon buttons, opacity slider, badges | Toolbar glass fill; `Pinned` badge success-soft | locked (click-through, lock icon persists), opacity-changed, compact, always-on-top toggle | Toolbar always-visible; unlabeled lock behavior (add tooltip "Ctrl+Alt to unlock" — existing chord, `PinWindow` keeps it) |
| Annotation Editor | Standard window; single 48h toolbar (tools · swatches · strokes · history · export), canvas, 28h status bar | Annotation toolbar, modal (unsaved), toasts | Tool selection teal; canvas bg `Canvas` token + checkerboard | tool states, dirty indicator, export progress, revision-saved confirmation (wargame-#1 revision layer) | Text-symbol glyph buttons; scattered toolbar rows |
| Settings | 200w rail (4 groups + Account group), content max 760, cards r4 pad 16, sticky footer (Close/Save) | Settings tabs, cards, inputs, badges | Existing pill-nav pattern kept; cards tighten 12→10 radius | per-tab dirty state; Account badge states; availability labels (Speech pattern is good — generalize it) | 13 flat tabs (group them); cards-in-cards; 12px radius on dense cards |
| Account/Billing | Single scroll page in Settings: status card → license card (key masked + devices table) → updates card → Pro card (or waitlist) → meters (Pro) → legal links footer | Status badge, usage meter, pricing card (app variant), gate banner, modals (deactivate confirm) | Violet only on Pro elements; everything else fog/teal | all 13 entitlement states from the spec table — **every one has a designed calm treatment here** | Any modal-on-launch for commercial states; urgency copy |
| Capture HUD | 6 tiles 3×2 (96×88, gap 8) + footer chips; panel glass r5 e2; must fit 1366×768 @125% (compact 84×76 tiles below 900px avail height) | HUD tiles, chips | Ready/standby pills; one gradient border max (active tile) | per-tile ready/standby/active/unavailable; compact | Oversized fixed HUD that clips on small laptops |
| Homepage | 12-col/1200px; hero = text-left (eyebrow Micro, H1, sub, CTAs, trust row) + octopus asset right; sections per homepage concept; pricing = 3 pricing cards | Web mirrors: pricing cards, meters (illustrative), gate-banner styling for "Pro coming" ribbon, trust chips | Same palette via `--od-*`; hero bg `Canvas` with radial teal glow ≤ 8% opacity; Inter | reduced-motion (static octopus), no-WebGL fallback, mobile (octopus below text) | Underwater/mascot art; baked-copy hero images; generated mockup screenshots as "product UI"; exact prices in hero |

### Token Migration Plan

Phase 1 = additive (no key deletions; three value re-points); Phase 2 = deprecation sweep after all windows compile against new names.

| Existing Token/Pattern | Keep/Replace | New Token/Pattern | Migration Notes |
| --- | --- | --- | --- |
| `Octadock.Brush.Surface` (#0C1220) | Keep (role narrows) | + new `Octadock.Brush.Canvas` (#070B14) beneath it | Windows move backdrop to `Canvas`; `Surface` becomes panel/section fill. `WindowBackdrop` gradient → deleted in favor of flat `Canvas` |
| `Octadock.Brush.SurfaceAlt` (#141E32) | Keep as alias | `Octadock.Brush.SurfaceRaised` (same value) + new `SurfaceOverlay` (#1B2942) | Alias keeps old XAML compiling; new code uses `SurfaceRaised`; menus/tooltips re-point to `SurfaceOverlay` |
| `Octadock.Brush.Accent` / `AccentSoft` / `AccentText` / `FocusRing` | Keep unchanged | — | Values already correct and contrast-verified |
| `Octadock.Brush.TextMuted` | Keep as alias | `Octadock.Brush.TextSecondary` (same value) + new `TextDisabled` (#5B6B84) | Disabled text stops reusing muted; 45%-opacity disabled pattern on controls stays |
| `Octadock.Brush.AccentGradient` | Keep, **scope restricted** | Usage rule: 1 primary CTA per surface + dock dot + tab indicator | Audit: Save buttons keep it; remove from any secondary/inline use |
| `Octadock.Corner` (6) / `Corner.Pill` (8) / `Corner.Large` (12) | Keep | + `Corner.Chip` (4), `Corner.CardDense` (10), `Corner.Tile` (16), `Corner.Full` (999) | Settings/preview cards move 12→10; `Corner.Large` reserved for floating panels/dialogs |
| `Octadock.Pad.Card` (12) | Keep (dense) | + `Octadock.Space.*` scale (2–48) | Standard cards pad 16 via `Space.4`; dense keep 12 |
| Hardcoded preview colors in code (`PreviewCardWindow.cs` et al., noted in `PROJECT_INPUTS.md`) | Replace | All through `Octadock.Brush.*` | Grep-audit `Color.FromRgb|#FF` under `src/Octadock.App` outside Themes; acceptance: zero literals |
| Segoe MDL2 glyphs + text-symbol toolbar glyphs | Replace | `OctadockIcons.xaml` Lucide `StreamGeometry` set | Removes the documented invisible-glyph editing trap; window chrome buttons exempt |
| Win32 default DatePickers (History) | Replace | Themed date-range popover (custom, uses `SurfaceOverlay`) | Loudest complaint in `UI-INVENTORY.md` |
| Native white tooltips (tray/some surfaces) | Replace where owned | Themed `ToolTip` style exists in `Shared.xaml:628` — enforce app-wide | Tray menu itself stays native (documented constraint) |
| New tokens engineering must add | — | `Canvas, SurfaceRaised(alias), SurfaceOverlay, BorderStrong, TextSecondary(alias), TextDisabled, Info, Success/Warning/DangerSoft, Cloud, CloudSoft, ByoKey, Gated, Space.*, Corner.Chip/CardDense/Tile/Full, Duration.*, Ease.*, Icon.* sizes, Glass.Enabled flag` + `HighContrast.xaml` | Mirror 1:1 as `--od-*` custom properties in the homepage stylesheet; single source-of-truth JSON generating both is P1-nice, manual sync acceptable at this scale |

## Homepage Corrections

- **Hero:** visible brand wordmark stays huge, but the semantic H1 becomes the value line ("Capture, read, dictate, preview, and package context from your Windows desktop") — "Octadock" alone is weak for SEO/assistive tech. Eyebrow: "Windows-first. Local by default." CTAs: **"Download free trial"** (primary — replaces "Join Waitlist" since the beta sells Local) + "See plans" (secondary). Trust row verified against reality: "Windows 10/11 · Local by default · Bring your own keys · No silent uploads" — all four are true (crash reports are local + opt-in per Settings; keep them that way or soften the fourth chip).
- **Octopus asset:** approved as a serious technical object; ship **v1 as a static high-quality render** (≤200KB AVIF/WebP + parallax), GLB/Three.js deferred until the page earns it; static fallback for reduced-motion/mobile mandatory; text and CTAs must render before and without the asset (LCP = text). No underwater props, no mascot poses (concept doc already forbids — hold the line).
- **Pricing copy:** on the pricing section only (not hero): "Local — $59 one-time ($49 during beta). Yours forever. 12 months of updates, renew $19/yr if you want more." / "Pro — from $10/mo. Cloud accuracy and AI context. **Coming soon — join the waitlist.**" / "Team — later." Never show hosted quotas until Pro is purchasable. The tagline "Start local. Add Pro when you want cloud accuracy and AI context." survives verification — keep it.
- **Privacy copy:** the strongest section — lead with mechanics, not adjectives: "Everything runs on your PC. Cloud requests happen only when you press a button that says so, and the button names the destination." Link a plain-English "What leaves your machine" doc (it can literally embed the cloud-send confirmation screenshot).
- **Context copy:** label the Context section "**In development — ships with the Local license**"; show the real export-preview/redaction flow from wargame #1 as its proof point; never imply AI analysis of packages exists.
- **CTAs:** primary = trial download everywhere; Pro CTAs are waitlist-only until metering ships; no pricing in the first viewport (concept doc is right).
- **Claims to avoid:** "unlimited" anything; "AI-powered" as a product adjective; any PDF/Office *editing* claims (wargame #1: v1 is preview + annotate-on-copy); scrolling-capture superiority claims vs ShareX (ShareX has it free — verified); "replaces your screenshot tool" (invites the ShareX comparison on the wrong axis); shipped-tense for Context AI.

## Risk Register

| Risk | Area | Severity | Likelihood | Mitigation | Verification |
| --- | --- | --- | --- | --- | --- |
| Lemon Squeezy platform sunset mid-migration to Stripe Managed Payments | Payments | P0 | High (verified: preview live, LS pages walled) | Launch on Paddle; entitlements in first-party service so provider swap = checkout swap | Re-check Stripe MP status quarterly |
| Selling Pro before metering exists → refunds, vendor bills, trust damage | Pricing | P0 | High if ignored | Pro = waitlist until quota/top-up/dunning built (P1 backlog) | Pro checkout literally absent from beta builds |
| First-party account system creeps into v1 critical path | Accounts | P0 | Medium | "No accounts in v1" decision ratified in this doc; Paddle portal + license keys cover all flows | First-run and activation flows contain zero sign-in UI |
| Paddle sub-$10 custom-pricing rule blocks $5 top-ups | Payments | P1 | Medium (note verified verbatim) | Ask Paddle before promising top-ups; fallback $10 packs | Sales confirmation in writing |
| BYO keys in env vars leak via logs/shell profiles; UI-less key setup blocks non-technical users | Privacy/Support | P1 | High | Credential Manager storage + Settings > Cloud Providers UI + migration banner; assert keys never logged | Test: key value absent from logs/DB after full session |
| Trial reset via `%LOCALAPPDATA%` wipe normalizes non-payment | Pricing | P2 | Medium | Accept in beta; signed trial file + one honest extension; revisit with server-side check only if data shows abuse | Beta conversion metrics vs reset signals (support anecdotes) |
| License key sharing beyond 3 devices | Entitlements | P2 | Medium | Activation registry + self-serve deactivate; revoke+reissue on public leaks; no aggressive DRM (posture protects brand) | Registry anomaly report (>6 distinct hashes/90d) |
| Refund-after-heavy-hosted-use burns COGS (Pro era) | Payments | P1 | Medium | Consumed-credit carve-out in refund policy (within Paddle MoR policy); hosted hard stops | Policy text reviewed against Paddle terms before Pro launch |
| OpenAI/ElevenLabs price or model changes strand quota economics | Pricing | P1 | Medium (OpenAI page already dropped whisper-1/TTS listings — verified) | Quotas defined as adjustable in ToS; margin modeled at 3× headroom; re-verify quarterly | Repeat this doc's pricing pass each quarter |
| Violet cloud-tier color drifts into general accent use, recreating one-note UI with two notes | Design | P2 | Medium | Hard rule in token docs: violet = commercial/cloud only, never product features; design review checklist item | Grep-audit `Cloud` brush usages per release |
| Offline-grace / entitlement bugs lock out paying users | Entitlements | P0 | Medium | Fail-open for cached valid entitlements; clock-skew tolerance; kill-switch-free design (no remote disable of Local) | Test matrix: clock skew ±72h, 6-month offline, revocation, migration |
| "No silent uploads" claim vs any future telemetry | Brand/Privacy | P1 | Low | Event list in this doc is exhaustive; any addition requires copy review of the claim | Homepage claim ↔ event-list diff check at each release |
| Icon migration regressions (invisible glyphs history) | Design/Eng | P2 | Medium | Path-geometry icons remove the failure class; per-icon visual snapshot tests | Snapshot suite over `OctadockIcons.xaml` |
| Homepage 3D hero tanks LCP/conversion | Marketing | P2 | Medium | Static render v1, lazy 3D later, text-first LCP, CTA never WebGL-gated | Lighthouse budget: LCP < 2.0s on mid-tier laptop |

## Revised Backlog

### P0 Before Paid Beta

- Paddle merchant account + checkout for Local $49 (beta) with license-key email delivery; refund policy configured (consumed-credits carve-out drafted for later Pro).
- **License service v1:** webhook consumer, key issuance, activation registry (3 devices), Ed25519-signed entitlement files, revocation, resend-key endpoint, daily reconciliation.
- App entitlement layer: trial state machine (signed file, non-bricking expiry, one extension), key activation UI + `octadock://activate` deep link, offline validity, revoked state.
- **Settings > Account & Billing v1** (status/key/devices/updates cards + buy link) and trial banner/gate states — all calm inline, zero launch modals.
- Cloud-send consent framework (first-use confirmation + per-feature "always allow" + destination-named buttons) — required now because BYO cloud (ElevenLabs read-aloud, OpenAI STT) already ships in trial.
- In-app **Pro waitlist** capture (email field, one endpoint) on gate banners.
- Update-entitlement gate in the updater (`updates_until` check; app never disabled).
- Legal set: EULA, privacy page (with the exhaustive event list), refund policy page.
- **Design-token phase 1** (additive tokens incl. violet/cloud set, fog steps, spaces/radii/durations + `HighContrast.xaml` + reduced-transparency flag) — wargame #1's Phase-1 UI work builds *on* these; landing them first avoids re-skinning.
- Homepage v1: trial-download hero (static octopus), pricing section with beta price, waitlist for Pro, privacy section.

### P1 Before Public Launch

- Pro infrastructure: hosted proxy with entitlement check, usage ledger + quota evaluator, meters UI, top-ups (post-Paddle confirmation), dunning/past-due states, Pro entitlement refresh; then flip Pro from waitlist to purchasable at $10/mo.
- BYO key storage move to Credential Manager + **Settings > Cloud Providers** full UI (test call, badges, per-feature toggles, env-var migration banner).
- Icon migration to Lucide across dock/shelf/editor/settings; delete text-symbol toolbar glyphs; snapshot tests.
- Component rollout: shelf rows (ref-04), dock grouping polish, HUD compact mode, themed date-range picker (kills Win32 DatePickers), pin toolbar auto-hide + compact.
- Light-theme parity + high-contrast + reduced-transparency QA pass over all new surfaces.
- Account-surface polish: device-list management, past-due/canceled/revoked treatments, offline chips.
- Homepage v2: real product-UI fragments (HTML/CSS per concept doc), plans page with full matrix, "What leaves your machine" doc.

### P2 Later

- Annual Pro ($96/yr) + 12-month-tenure perpetual-fallback rule (owner ratifies first).
- Stripe Managed Payments evaluation when it exits preview (checkout swap; entitlements unaffected).
- Team plan activation on the dormant schema (seats, org policy for cloud sends, pooled meters) + first-party accounts *then*.
- Hosted premium-voice credits (v1.5 metering maturity), AI summarize/explain credits when the feature exists.
- Offline/airgapped manual activation file exchange; EDU/regional pricing; GLB/Three.js hero; token JSON single-source generator.

## Open Questions

| Question | Owner | Blocking? | Suggested Default |
| --- | --- | --- | --- |
| Ratify Paddle vs waiting for Stripe Managed Payments GA | Founder | Yes (P0 path) | Paddle now; revisit at Pro launch |
| Beta price $49 vs straight $59 | Founder | Yes (checkout copy) | $49 beta with public launch-price note |
| Device limit 2 vs 3 | Founder | No | 3 (support-friendlier; CleanShot manages at 1–2 with heavier support tooling) |
| Pro monthly $10 vs $12 | Founder | No (Pro is waitlisted) | $10; decide finally with real COGS telemetry from beta BYO usage patterns |
| 12-month-Pro → perpetual Local fallback | Founder | No | Yes — strong retention/fairness story, trivial entitlement rule |
| Trial length 14 vs 21 days | Founder | No | 14 + one self-serve 7-day extension |
| Hosted premium TTS in Pro v1 or BYO-only until v1.5 | Founder | No | BYO-only first (COGS + abuse simpler); credits at v1.5 |
| Local license includes 12 months updates vs "lifetime minor updates" | Founder | Yes (checkout copy) | 12 months + $19/yr (verified CleanShot pattern; predictable revenue) |
| Does "No silent uploads" chip survive legal review with opt-in local crash reports | Founder + copy review | No | Keep chip; crash reports stay local-only and opt-in (current behavior) |
| Homepage stack Next.js vs Vite static | Engineering | No | Vite static for v1 (one page + waitlist endpoint); Next.js only if docs/blog follow |
| Cloud usage rollover vs monthly reset (research open question) | Founder | No (Pro later) | Monthly reset, no rollover v1; top-ups carry 12 months |
| Does the beta build show the Pro column in the plans view at all | Founder | No | Yes, as "coming soon + waitlist" — captures demand, sets roadmap expectations honestly |

# Octadock Paid-Beta Support Runbooks

Status: **pre-launch draft; not an operational authority**

This document preserves planned support procedures, but delivery, mail,
admin-console, installer, and production-payment steps remain incomplete. Do not
use it with customers until `docs/ROADMAP.md` Gate D-05 passes. Current code
behavior is documented in `docs/PROJECT-STATE.md` and
`services/license-service/README.md`; the July 6 wargames are historical inputs.

## How to use this document

Each runbook follows the same shape: **Symptom** (what the buyer says) · **Likely cause** ·
**Diagnose** (which admin surface / field to check) · **Resolution** · **Macro** (a short canned
reply you can paste and edit) · **Prevention**.

### Founder-gated capabilities — read this first

Several support actions depend on infrastructure that is provisioned but **not yet live** ("founder-gated"
in the plan). Where a step needs one of these, it is marked **[FOUNDER-GATED]**. Until each lands, use the
documented interim path.

- **Support mailbox** — `support@octadock.com` is the intended buyer-facing channel, but DNS,
  mailbox, sender verification, and warm-up are not yet evidenced. **The support channel itself is
  [FOUNDER-GATED].** Until it is live, there is no inbound queue to work.
- **Admin surface** — the license service exposes authenticated **`/admin/health`** and a minimal
  public liveness **`/health`** response. Production still needs a founder-gated network layer
  (Cloudflare Access + WebAuthn). The app layer requires `LicenseService:AdminToken` only through
  `X-Admin-Token`, rejects query tokens, returns `403` for missing/wrong headers, fails closed with
  `503` when unconfigured, emits no metrics on failures, and marks responses `Cache-Control: no-store`.
- **License search UI** — a full search-by-email / key-suffix / payment-intent console with 5 actions
  (resend, deactivate/migrate, revoke, reinstate, replacement key) is specified but the UI is a
  **Fast Follow** build. At beta, "search" means **querying the license-service SQLite DB directly** (the
  `licenses`, `activations`, `webhook_events`, `identity_links`, and `audit_log` tables) plus the Stripe
  Dashboard. Where a runbook says "search by X", that is the DB query / Stripe lookup to run.
- **Live email delivered/bounced status** — the launch-health `email` block is intentionally `null`
  ("no data by design") because the email pipeline is **[FOUNDER-GATED]**. No provider or success-page
  delivery fallback is implemented today.
- **Self-serve deactivation, slot migration/LRU, offline-activation page, license-lookup/VAT page** — all
  **Fast Follow**. At beta these are **support-assisted** (you act on the DB / Stripe on the buyer's behalf).

### The one invariant to keep in your head

**Exactly one license record per verified paid Stripe event.** A license is issued only from a
`Stripe-Signature`-verified `checkout.session.completed` whose price matches the configured Octadock beta
price, deduped by event id. Activation later produces the signed device entitlement. Refund/dispute events
flip the license to `revoked`. The hourly reconciliation poller flags paid sessions with no license row.

### Admin fields you will reference constantly

| Where | Field | Meaning |
| --- | --- | --- |
| `/admin/health` | `reconciliation.diff` | Paid Stripe sessions with **no** license row. Should be 0. |
| `/admin/health` | `reconciliation.paidSessionSourceConfigured` | Whether the live Stripe source is wired. If `false`, a "clean" diff is **not** trustworthy — the source never ran. **[FOUNDER-GATED]** (needs the live restricted Stripe key). |
| `/admin/health` | `webhookEvents.mostRecentReceivedAt` | Staleness clock. > 60 min with expected traffic = webhook problem. |
| `/admin/health` | `activation.successRate` | Live from `audit_log` (`device.activated` vs `device.activation_failed`). |
| `/admin/health` | `licenses.issuedNotActivated` / `Rate` | Bought-but-never-activated. Feeds "paid but stuck" outreach. |
| `/admin/health` | `email.*` | Always `null` — see founder-gated note above. |
| DB `licenses` | `status` | `active` or `revoked` (refund/dispute). |
| DB `licenses` | `stripe_checkout_session_id`, `stripe_payment_intent_id`, `purchase_email`, `stripe_customer_id` | The identity handles you search by. |
| DB `activations` | `machine_hash`, `device_hash_v`, `deactivated_at`, `first_activated_at`, `last_seen_at` | The device registry. Active = `deactivated_at IS NULL`. |
| DB `audit_log` | `action`, `actor`, `detail`, `created_at` | Append-only. `license.issued`, `license.revoked`, `device.activated`, `device.activation_failed`. |

---

## Runbook 1 — SmartScreen: "Windows protected your PC" on download/install

**Symptom.** "When I run the installer, Windows says *Windows protected your PC* / *unrecognized app*
and I have to click *More info → Run anyway*. Is this safe?"

**Likely cause.** SmartScreen reputation warm-up is still accruing. The installer is (or will be) signed
under one stable identity, but Microsoft's reputation for that identity builds over weeks of real downloads.
Before the warm-up window completes, a fraction of users see the interstitial. (Note: per Microsoft's own
docs, an EV certificate no longer auto-bypasses SmartScreen — reputation is what clears it.)

**Diagnose.**
- Confirm the buyer downloaded from the **canonical URL** and the file hash matches the **published SHA-256**
  on the download page. A mismatch means they got a re-hosted / tampered copy — do not coach them through
  the warning; send them to the official link. **[FOUNDER-GATED]** the live download URL + SHA-256 are set
  at release time.
- Confirm the release is signed: `signtool verify /pa <installer>` should pass. (This is a CI gate; if a
  buyer's copy fails it, it is not our build.)

**Resolution.**
1. Reassure: the interstitial is a reputation artefact of a new signed app, not a malware detection.
2. Have them verify they are on the official download page and, if they wish, check the SHA-256 against the
   one published there.
3. Walk them through **More info → Run anyway** *only after* the URL + hash are confirmed official.
4. Log the report — a cluster of SmartScreen hits vs. a normal download count is the "SmartScreen ate the
   funnel" signal.

**Macro.**
> Thanks for checking before clicking through — that's exactly the right instinct. Octadock's installer is
> code-signed, but because it's a brand-new signed app, Windows SmartScreen hasn't built up its reputation
> yet, so some people briefly see that "protected your PC" screen. It's a reputation notice, not a virus
> warning. Please make sure you downloaded from our official page [LINK] — you can match the SHA-256 shown
> there against your file if you'd like. Once you've confirmed that, click **More info → Run anyway** and
> you're good. It'll stop appearing as more people install. Any trouble, just reply here.

**Prevention.** Start the signing + warm-up clock weeks before checkout opens; publish one canonical download
URL with its SHA-256; never teach users to bypass warnings *before* verifying the source (that trains them to
run re-hosted trojans).

---

## Runbook 2 — Paid but no key received

**Symptom.** "I paid but never got my license key. Nothing in my inbox."

**Likely cause (ranked).**
1. **Email not delivered** — cold-domain spam filtering or a typo'd purchase email. The key was issued; the
   email didn't land.
2. **Webhook didn't verify / didn't arrive** — no license row was created (signature failure, endpoint down,
   or a Stripe retry that hasn't landed). Reconciliation will show a non-zero diff.
3. **Wrong price/product** — a paid session that isn't the configured Octadock beta price does **not** issue a
   license by design (anti-spoofing). Rare for real buyers, common for test noise.

**Diagnose.**
1. Open `/admin/health`. Check **`reconciliation.diff`**. Nonzero = at least one paid session with no key —
   this buyer is likely in it. (If `paidSessionSourceConfigured=false`, the diff is not authoritative yet —
   **[FOUNDER-GATED]** — fall to step 2/3.)
2. In the **Stripe Dashboard**, find the payment by email / card last-4 / payment-intent. Confirm
   `payment_status=paid` and that the price is the Octadock beta price.
3. In the license DB, search `licenses` by `purchase_email`, `stripe_checkout_session_id`, or
   `stripe_payment_intent_id`.
   - **Row exists, `status=active`** → key was issued; this is an **email delivery** problem → resend.
   - **No row** → issuance didn't happen → check `webhook_events` for the event id and
     `webhookEvents.mostRecentReceivedAt` staleness; check Stripe's webhook delivery log for failures.
4. Email delivered/bounced status inside the admin surface is `null` (**[FOUNDER-GATED]**); confirm delivery
   via the sending provider (FastMail) log instead.

**Resolution.**
- **Key issued, email lost:** no primary delivery path is implemented yet. Until an authenticated,
  reviewed delivery surface exists, this draft procedure is manual DB lookup plus an approved secure
  support channel; the resend endpoint remains **[FOUNDER-GATED]**.
- **No license row but Stripe shows paid:** re-drive issuance. Because webhook processing is idempotent and
  unprocessed duplicates are retryable, re-delivering the Stripe event issues exactly one key (never a
  duplicate). If the webhook path is down, issue and send the key manually, then fix the endpoint. Never let
  a confirmed-paid buyer wait — a stranded payer files a dispute.
- **Wrong-price paid session:** if it's a genuine buyer who somehow hit an inactive price, refund and have them
  re-purchase the active beta price, or issue manually per founder call.

**Macro.**
> Sorry about that — let's get your key to you right now. I can see your payment on our side; the key just
> didn't reach your inbox (new-domain email sometimes gets filtered). Your Octadock license key is:
> **OCTA-XXXXX-XXXXX-XXXXX-XXXXX**. Open Octadock → **Settings → Account & Billing**, paste it into the
> license box, and click **Activate**. Also worth checking your spam/Promotions folder and adding
> support@octadock.com to your contacts. Let me know once it activates.

**Prevention.** Build an authenticated, reviewed delivery surface with durable email backup; keep hourly
reconciliation that pages the founder on any diff > 0; verify and warm the mail domain before customer use.

---

## Runbook 3 — Device limit reached

**Symptom.** "It says my license is already active on the maximum number of devices. I only have my one PC /
I replaced my old laptop."

**Likely cause.** The license allows **3 devices**. Activation is machine-scoped by `machine_hash`
(`SHA-256` of the Windows `MachineGuid`). Old PCs, VMs, or a Windows reinstall can each hold a slot, so the
3 active slots fill up. The 4th `/activate` returns **409 DeviceLimitReached**. LRU self-eviction and a
self-serve device page are **Fast Follow**, so at beta this is **support-assisted**.

**Diagnose.**
1. Search the license DB for the buyer's `licenses` row (by email / key), get its `id`.
2. Query `activations WHERE license_id = <id> AND deactivated_at IS NULL` — these are the live slots. You'll
   see up to 3 rows, each with a `machine_hash` (only the first ~8 chars are echoed into `audit_log` detail),
   `first_activated_at`, and `last_seen_at`.
3. Identify the stale slot(s) — an old `last_seen_at`, or a device the buyer confirms they no longer use.

**Resolution.**
1. Confirm the buyer's identity (purchase email) and which device to free.
2. **Deactivate the stale slot** by setting `deactivated_at` on that `activations` row (support-assisted;
   the self-serve/LRU path is Fast Follow). This frees a slot without changing the device limit.
3. Have them re-run **Activate** on the new PC — it now succeeds and writes a fresh activation row.
4. If they legitimately need more than 3 concurrent devices, that's out of policy for the beta license — note
   it and escalate; do not silently raise the limit.

**Macro.**
> Your license covers 3 devices at once, and it looks like older machines are still holding slots. I've freed
> the slot for [device], so you're clear to activate on your current PC now — open **Settings → Account &
> Billing**, paste your key, and click **Activate**. If you swap machines again in future, just reply and I'll
> free the old slot. (Self-service device management is on the way.)

**Prevention.** Slot migration on changed `machine_hash` + LRU self-eviction + a self-serve device page
(all Fast Follow). Until then, the runbook above. Set expectations on the pricing page: 3 devices, reinstalling
Windows shouldn't normally cost a slot (see Runbook 11).

---

## Runbook 4 — Offline / air-gapped activation

**Symptom.** "This PC has no internet / is air-gapped. Activation says it can't reach the license service.
How do I activate?"

**Likely cause.** Activation is an online call: the client POSTs the key + `machine_hash` to `/activate`, the
service returns a signed, device-bound entitlement, and the client verifies it locally. On an offline machine
that call returns **EndpointUnavailable** ("Couldn't reach the Octadock license service"). A downloadable
offline-activation page (machine code → signed entitlement file to import) is **Fast Follow** — **[FOUNDER-GATED]**,
not live at beta.

**Diagnose.**
- Confirm it's genuinely a connectivity issue, not a wrong endpoint: the client shows *EndpointUnavailable*
  specifically (distinct from "key not recognized"). If the buyer is behind a corporate proxy/firewall,
  `api.octadock.com` may simply be blocked.

**Resolution (interim, support-assisted).**
1. **Preferred:** have them activate once on any machine *with* internet using the same key. After a successful
   online activation the app holds a signed entitlement locally and works offline thereafter ("works offline
   after one activation"). Note this consumes a device slot for that machine.
2. If the target machine can *never* reach the internet and can't be activated online first, this is the
   offline-import case. That page is Fast Follow / **[FOUNDER-GATED]**; until it ships, escalate to the founder
   to hand-issue a device-bound entitlement for their `machine_hash` (the service can sign one) and deliver it
   for manual import.
3. For a blocked-proxy case, give them the host to allowlist (`api.octadock.com`) and have them retry.

**Macro.**
> Octadock needs to reach our license service once to activate; after that it stays activated on that machine
> without needing to check in again. Easiest path: activate once on any PC that has internet using your key
> (that machine then keeps working without a connection going forward). If your machine is permanently
> air-gapped, reply and let me know — we can issue an
> offline activation for it directly. If it's a work firewall, allowlisting `api.octadock.com` and retrying
> usually does it.

**Prevention.** Ship the self-serve offline-activation page (machine code in → signed entitlement out);
document the `api.octadock.com` host on the download/privacy page so IT can allowlist it in advance.

---

## Runbook 5 — Trial expired early / "your PC clock looks wrong"

**Symptom.** "My trial says it ended way before 14 days" **or** "Octadock says my clock looks wrong and the
trial is paused."

**Likely cause.** The trial clock is a **monotonic high-water anchor**, not raw wall-clock time. It persists
the maximum UTC it has ever observed and computes expiry against `max(now, high-water)`. A **backward** clock
jump greater than the 48-hour grace **freezes** the countdown and shows a "your PC clock looks wrong" banner
(state = **TrialFrozen**, chip label **"Paused"**). This is rollback detection for intact local state; it
does not survive state deletion or replay. For honest users, it prevents a corrected clock from silently
eating trial days.

**Diagnose.**
- Which is it? **Frozen** (chip "Paused", banner about the clock) vs **genuinely expired** (chip "Trial ended").
- Ask what the Windows clock reads. A wrong system date (dead CMOS battery, manual date change, VM snapshot
  restored to the past) is the usual cause of a freeze.
- Note: `trial.json` is unsigned local JSON under `%LOCALAPPDATA%\Octadock\license\`, outside the main DB.
  It has monotonic clock checks but does not yet provide deletion/replay resistance; B-04 policy is unresolved.

**Resolution.**
1. **Frozen:** have them set the Windows clock to the correct current date/time (ideally enable automatic
   time sync), then reopen Octadock. Once `now` is no longer behind the high-water mark by more than the grace,
   the countdown resumes.
2. **Genuinely expired early and the clock is correct:** that shouldn't happen for an honest install; capture
   details (install date, current date, whether the machine's clock was ever ahead then corrected) and escalate.
   Do not promise a trial extension; the shipped product has no extension mechanism. A purchased key can be
   activated immediately while the clock issue is investigated.
3. Do not promise that wipe/replay abuse is prevented; capture evidence and escalate under the unresolved B-04 policy.

**Macro.**
> Octadock pauses the trial countdown if the PC's clock looks off, so nobody loses days to a wrong date. Please
> set your Windows date & time to the correct current values (turning on "Set time automatically" is ideal),
> then reopen Octadock — the trial will pick back up. If it still looks wrong after that, reply with what your
> clock reads and I'll sort it out.

**Prevention.** Monotonic high-water clock with a 48-hour grace and a visible "clock looks wrong" banner;
ambient trial status from day one (tray tooltip, day-7 badge, day-11 banner) so expiry is never an ambush.

---

## Runbook 6 — Refund requested → license revoked

**Symptom.** "I'd like a refund" / "I want to cancel and get my money back."

**Likely cause.** Within the voluntary **14-day money-back** window (and the EU/UK statutory withdrawal right),
buyers can request a refund. On refund/dispute, the license is revoked.

**Diagnose.**
- Confirm the purchase in Stripe (email / payment-intent) and that it's within the 14-day window (or a valid
  statutory withdrawal).
- Find the `licenses` row (by `stripe_payment_intent_id`) and confirm current `status`.

**Resolution.**
1. Process the refund in Stripe. This emits `charge.refunded` (a dispute emits `charge.dispute.created`).
2. The webhook consumer **revokes every license tied to that payment intent** — `RevokeByPaymentIntent` sets
   `status=revoked`, stamps `revoked_at` + `revoked_reason`, and writes a `license.revoked` audit row.
3. **Revocation takes effect on the next activation / connect**, not instantly on already-running installs.
   The stored entitlement is still signed and valid on-device until the client next re-evaluates against a
   revoked status. Set this expectation: the buyer's currently-open app may keep working until it next checks.
4. Existing captures/history are never deleted by revocation — the app returns to the trial/expired gate,
   which still lets them view and export what they already have (see the verb matrix).

**Macro.**
> No problem — I've refunded your purchase in full. The license will deactivate the next time Octadock checks
> in (it may keep working until then). Everything you've already captured stays on your PC and remains viewable
> and exportable. If there's something we could've done better, I'd genuinely like to hear it. Thanks for
> giving Octadock a try.

**Prevention.** Publish the 14-day money-back + EU withdrawal terms on `/refunds`; wire the refund/dispute
webhook consumers so revocation is automatic and audited (not a manual chore); make clear that revocation is
"next connect", not retroactive data deletion.

---

## Runbook 7 — Clipboard history stopped after trial ended

**Symptom.** "My clipboard history stopped recording new items after my trial ended. Is it broken?"

**Likely cause.** **Working as designed, not a bug.** Per the ratified post-expiry verb matrix, the clipboard
**monitor PAUSES** at trial expiry (state shows an inline "Paused — trial ended" banner). Existing clips stay
fully viewable and exportable; only the *recording of new clips* stops. This closes the "the app is still
recording my clipboard after I stopped paying" trust hole.

**Diagnose.**
- Confirm the license state: chip reads "Trial ended" (or "Revoked"). The clipboard monitor pauses in exactly
  those non-`AllowsFullUse` states.
- Confirm the buyer *can still open and export* their existing clipboard history — that distinguishes "paused
  by design" from an actual fault.

**Resolution.**
1. Explain it's intentional: capturing new content is a licensed action; viewing/exporting existing data is
   always allowed.
2. To resume recording, they activate a license. The shipped product has no trial-extension mechanism.
3. If they *want* it to stay paused, nothing to do — their existing clips remain available.

**Macro.**
> That's actually by design, not a glitch. When the trial ends, Octadock stops recording *new* clipboard
> items (we don't think an expired trial should keep capturing your clipboard in the background). Everything
> already in your clipboard history stays there and you can still view and export it. Activate a license and
> recording picks right back up — **Settings → Account & Billing**.

**Prevention.** Make the "Paused — trial ended" banner clearly visible on the clipboard surface; state on the
website that the gate blocks new capture/compute but never blocks viewing/exporting existing data.

---

## Runbook 8 — GDPR / data-erasure request

**Symptom.** "Under GDPR, please tell me what data you hold about me and/or delete it."

**Likely cause.** A legitimate data-subject request. Octadock has **no first-party accounts**; the only
personal data on our servers lives in the **license service**, tied to the purchase.

**Diagnose — what the license service stores (per the privacy page).** For a paying buyer, across the
`licenses`, `identity_links`, `activations`, `webhook_events`, and `audit_log` tables, this can include:
- **`purchase_email`** and **`stripe_customer_id`** (in `licenses` and `identity_links`);
- Stripe references: `stripe_checkout_session_id`, `stripe_payment_intent_id`, plus amount/currency;
- The **license key** and its `status`/timestamps;
- **`machine_hash`** per activated device (a `SHA-256` of the machine's `MachineGuid` — a pseudonymous
  fingerprint, not directly identifying), with `device_hash_v` and activation timestamps;
- **`audit_log`** rows recording issuance/revocation/activation actions.
- Captures/history and current-alpha trial state live **only on the user's own PC**
  (`%LOCALAPPDATA%\Octadock\`). B-04 may add a pseudonymous server-side trial subject/expiry; update this
  runbook and the privacy field list before release. For local data, erasure = uninstall + delete that folder.
- Payment card data is held by **Stripe**, not by us; direct card/receipt requests to their Stripe records.

**Resolution.**
1. **Access request:** enumerate the fields above for their purchase email and provide them. Reference the
   privacy page's field list so the answer matches published copy exactly.
2. **Erasure request:** verify identity (purchase email), then delete/anonymise their personal fields in the
   license DB — `purchase_email`, `stripe_customer_id`, and the `identity_links` row. Note the tension with the
   audit requirement: keep the append-only `audit_log` integrity by anonymising rather than deleting audit rows
   where you must retain a financial/issuance record; delete what isn't legally required to keep. Record the
   erasure itself.
3. Deleting server-side identity **does not revoke** a working license — if they want a refund too, that's
   Runbook 6.
4. Tell them how to erase the **local** data (uninstall + delete `%LOCALAPPDATA%\Octadock\`).
5. Where the request touches Stripe-held data, point them to the Stripe pathway.

**Macro.**
> Here's what we hold about you: the email you purchased with, your Stripe customer/payment references, your
> license key and its status, and a per-device activation fingerprint (a one-way hash of the machine ID — we
> can't reverse it to identify your hardware). Your captures and history stay on your PC. Current-alpha trial
> state is also local; the paid-beta B-04 policy is not final. If you'd like us to erase your personal details
> from our license records, just
> confirm the purchase email and we'll remove them (we keep a minimal, anonymised record only where law
> requires). To remove the local data, uninstall Octadock and delete the `%LOCALAPPDATA%\Octadock` folder.

**Prevention.** Keep the privacy page's field list exhaustive and current so support answers match published
copy; store the minimum; use a pseudonymous `machine_hash` (already the design) rather than raw hardware IDs.

---

## Runbook 9 — Screen-reader / keyboard-only buyer needs help completing purchase + activation

**Symptom.** "I use a screen reader / keyboard only and I'm having trouble buying or activating."

**Likely cause.** An accessibility gap on a commercial surface (checkout page, key-entry field, gate prompt),
or the buyer simply needs the accessible path spelled out. The commercial surfaces are built to a minimum a11y
bar (names/roles/focus order/contrast; the key box carries an `AutomationProperties.Name`; the activation
result line is announced assertively; state chips use **text labels, not colour alone**). Live Narrator-on-VM
verification is **[FOUNDER-GATED]** (WS11), so real-world reports here are high-signal — capture details.

**Diagnose.**
- Where does the flow break: the website checkout, the Settings key-entry box, or the expiry gate?
- Which AT and browser/OS? Note it — a11y regressions are hard to see without the exact combo.

**Resolution.**
1. **Purchase:** Checkout is Stripe-hosted (broadly AT-friendly). Confirm they can reach the payment fields by
   Tab; the invoice/receipt is generated per session. If a specific field is unlabelled, capture it and escalate.
2. **Activation:** Direct them to **Settings → Account & Billing**. The license box is keyboard-reachable and
   labelled; **Activate** is a standard button; the result line is announced. The state chip announces a text
   label ("Trial", "Licensed", "Trial ended", "Paused", "Revoked").
3. **Accelerator:** an `octadock://activate?key=...` deep link (or `octadock activate --key ...` from the CLI)
   activates without navigating the UI at all, and is **exempt** from the automation-enable toggle so it works
   before they turn anything on. Offer this if UI navigation is the blocker.
4. Offer to do a support-assisted activation on their behalf if they hit a hard wall (issue/verify from your side).
5. **Every a11y issue reported is a bug** — log it; screen-reader/keyboard buyers literally can't pay if the
   commercial path is broken.

**Macro.**
> Happy to help you through it. To activate: open Octadock, go to **Settings → Account & Billing** (all
> keyboard-reachable), tab to the "License key" field, paste your key, and activate — the result is announced
> out loud. If navigating the settings is awkward, you can activate in one step with this link:
> `octadock://activate?key=OCTA-XXXXX-XXXXX-XXXXX-XXXXX` — it works even before enabling automation. And if
> anything is unlabelled or a field won't take focus, tell me exactly where and I'll both help you finish and
> get it fixed.

**Prevention.** Keep the Accessibility Insights FastPass green on the ~6 commercial surfaces as a written no-go;
run a real Narrator + keyboard pass through expiry → buy → activate before each release; keep text labels on
all state chips.

---

## Runbook 10 — Key won't activate / "key not recognized"

**Symptom.** "I'm pasting my key and it says it can't find it / it's not recognized."

**Likely cause (ranked).**
1. **Typo or partial paste** — most common. The key format is `OCTA-XXXXX-XXXXX-XXXXX-XXXXX`.
2. **Wrong string** — they pasted an order number, Stripe id, or receipt text instead of the key.
3. **Genuinely unknown key** — issued against a different product/price, or the row doesn't exist.
   Note: **normalization is tolerant** — the client strips whitespace, tolerates dashes/spacing/case and
   canonicalises before sending, so ordinary copy-paste messiness is *not* the cause. Two distinct client
   responses matter: **InvalidKeyFormat** ("that doesn't look like a key" — never even reaches the service)
   vs **KeyNotRecognized** ("we couldn't find that key" — the service returned `LicenseNotFound`).

**Diagnose.**
1. Ask them to paste the exact string they're entering. If it doesn't resemble `OCTA-…` at all, it's the wrong
   string (case 2) — get them the real key (Runbook 2 path).
2. If it's well-formed but rejected, search `licenses` by that `license_key`. **No row** → it was never issued
   or belongs elsewhere; cross-check Stripe by their email. **Row exists but `status != active`** → that's
   *LicenseInactive* (revoked/refunded), not "not recognized" — see Runbook 6.
3. Check `audit_log` for recent `device.activation_failed` rows with `reason=LicenseNotFound` to confirm the
   attempt is hitting the service.

**Resolution.**
1. **Typo/wrong string:** send the exact key from the `licenses` row through the approved secure delivery
   channel and have them paste it fresh. Normalization handles casing/spacing, so a clean copy-paste works.
2. **Well-formed but no row (paid):** issue is on our side — re-drive issuance / issue manually (Runbook 2).
3. **Inactive:** explain the license was revoked (refund/dispute) — Runbook 6.

**Macro.**
> Let's get you activated. Octadock keys look like **OCTA-XXXXX-XXXXX-XXXXX-XXXXX** — don't worry about
> spaces or capitalisation, Octadock cleans those up. Could you paste the exact text you're entering so I can
> compare it to your key on our side? If it turns out something got garbled in the copy, here's your key
> again: **OCTA-XXXXX-XXXXX-XXXXX-XXXXX**. Paste it into **Settings → Account & Billing** and click Activate.

**Prevention.** Tolerant key normalization (already shipped); build an authenticated, reviewed key-delivery
surface; distinguish the "malformed" vs "not found" messages so the buyer knows whether it's their paste or our
records.

---

## Runbook 11 — Reinstalled Windows / new PC — does it cost a device?

**Symptom.** "I reinstalled Windows / got a new SSD / rebuilt my PC. Will that use up one of my 3 devices?"

**Likely cause.** Device identity is `machine_hash = SHA-256(MachineGuid)`, and the Windows `MachineGuid`
(`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`) **can change on a clean Windows reinstall**. A changed
hash reads as a new device and can consume a fresh slot. Slot migration on changed hash + LRU self-eviction are
**Fast Follow**; at beta it's **support-assisted**.

**Diagnose.**
- Same physical machine, reinstalled OS → likely a new `machine_hash`, so the old slot is now orphaned.
- Genuinely new/different machine → that legitimately is a new device.
- Query `activations WHERE license_id = <id> AND deactivated_at IS NULL` and compare `first_activated_at` /
  `last_seen_at` to spot the stale slot.

**Resolution.**
1. **Same PC, reinstalled OS:** free the orphaned slot (set `deactivated_at` on the old `activations` row —
   support-assisted) and have them re-activate. Net device count stays the same; the reinstall shouldn't cost
   them a slot.
2. **New machine within the 3-device allowance:** they just activate; no action needed unless they're at 3
   already (then it's Runbook 3).
3. Once slot migration + LRU ship (Fast Follow), the changed hash will migrate automatically and this becomes
   self-service.

**Macro.**
> Reinstalling Windows can change the hidden machine ID Octadock uses, so your old install may still be holding
> a slot. I've cleared that old slot for you — go ahead and activate on the freshly-installed PC (**Settings →
> Account & Billing**). Reinstalling your OS shouldn't cost you a device; if it ever looks like it did, just
> reply and I'll free it.

**Prevention.** Ship slot migration on changed `machine_hash` + LRU self-eviction; set the expectation on the
pricing page that reinstalling Windows won't normally cost a slot.

---

## Runbook 12 — "Is Octadock offline?" (honesty answer)

**Symptom.** "Your site says local-first / private. Is Octadock actually fully offline? Does it send my data
anywhere?"

**Likely cause.** A trust question. Answer it **honestly and precisely** — never claim "fully offline" or
"local AI" as blanket product claims, because both would be false for specific opt-in paths.

**The honest answer (this is the ground truth to convey).**
- **Local-first by default.** Capture, annotation, OCR, local text transforms, screen recording,
  and history are on-device. There are **no network requests during capture, annotation, OCR, or recording**
  unless the user explicitly configures one.
- **One-time model download for dictation.** The default dictation engine downloads its speech model **once**
  (a few hundred MB), behind a **sized consent card** shown *before* the first fetch — declining cancels the
  fetch. After that, dictation transcribes **on-device**.
- **Agent Workspace uses the user's selected CLI after review.** It packages the trusted outcome and
  acceptance criteria with explicitly selected, untrusted evidence; shows exact `TASK.md`, hashes,
  detected-secret count, unchanged attachment boundary, and Codex/Claude destination first. Redaction is on
  by default and every destination-named handoff requires confirmation. The selected CLI may use its configured
  remote service, so it is a cloud hop, **not** "local AI." There is no silent provider fallback.
  `read --explain` only opens this review.
- **Opt-in cloud providers, billed by the provider.** OpenAI (dictation) and ElevenLabs (read-aloud voices) are
  strictly opt-in. Cloud STT only activates on an **`OCTADOCK_`-prefixed** env var (e.g.
  `OCTADOCK_OPENAI_API_KEY`) — a bare `OPENAI_API_KEY` is detected but never silently used. Usage is billed by
  that provider directly, not by us.
- **Published egress list.** The only outbound calls the app makes are: the one-time model download
  (Hugging Face), license activation/entitlement (`api.octadock.com`), and the opt-in OpenAI/ElevenLabs calls.
  There is a network-egress table on the website that enumerates them.

**Resolution.** Give the answer above, matched to what they'll actually use. If they never touch dictation
models, confirm an Agent Workspace handoff, or enable opt-in providers, their captures never leave the PC.

**Macro.**
> Straight answer: Octadock is local-first, and by default your captures, annotations, OCR, and recordings
> never leave your PC. Three honest exceptions, all in your control: (1) the default dictation engine downloads
> its speech model once (with a consent prompt first), then runs on-device; (2) an optional AI Action shows
> its exact outbound text and selected CLI destination, redacts detected secrets by default, and sends only
> after you confirm — the selected CLI may make a cloud hop and we label it as one;
> (3) optional OpenAI/ElevenLabs voices are opt-in and billed by those providers. We publish the full list of
> every network call the app can make on our site. If you skip those opt-in features, nothing goes out.

**Prevention.** Keep the "fully offline" and "local AI" strings out of product copy (CI copy-honesty gate);
keep the egress table published and accurate; show the model-download consent card before any fetch.

---

## Runbook 13 — Recording has no audio

**Symptom.** "My screen recording has no sound — no mic, no system audio."

**Likely cause.** Audio tracks are **opt-in and off by default**. Screen recording (Beta) captures MP4
video; microphone (WASAPI) and system/app (loopback) audio are separate toggles in Settings → Recording.
If both are off, the MP4 is silent by design — not a broken setting.

**Diagnose.**
- Confirm they mean recorded audio (not read-aloud/dictation). If the MP4 plays video fine but silent,
  check Settings → Recording first: both audio opt-ins default off.
- If an opt-in was enabled and the track is still missing or quiet, check the Windows microphone privacy
  setting and the selected input device. Device loss mid-recording fails the recording truthfully
  rather than saving a silent success.

**Resolution.**
1. Enable **Microphone** and/or **System/app audio** under Settings → Recording, then re-record.
2. If the microphone track was enabled but is silent, verify Windows microphone privacy access and the
   input device, and confirm no device-loss error was reported.

**Macro.**
> Octadock's screen recording is Beta and video-only by default — microphone and system/app audio are
> separate opt-ins under Settings → Recording. Turn on the tracks you want and re-record. If audio was
> enabled but is missing, check the Windows microphone privacy setting and your input device; if the
> device drops mid-recording, Octadock fails the recording honestly instead of saving a silent file.

**Prevention.** Keep recording labeled Beta; keep website/pricing copy accurate that audio is optional
and off by default.

---

## Runbook 14 — Where's my invoice / VAT receipt

**Symptom.** "I need a VAT invoice / a proper receipt for my purchase."

**Likely cause.** Buyers (especially business/EU) need a tax invoice. A self-serve license-lookup / invoice
page is **Fast Follow** — **[FOUNDER-GATED]** — so at beta this is Stripe-driven and support-assisted.

**Diagnose.**
- Find the purchase in Stripe by email / payment-intent. Every Checkout session is created with
  `invoice_creation=true`, so an invoice/receipt exists for the payment.

**Resolution.**
1. Retrieve the invoice/receipt from Stripe for that payment and send it, or send the buyer the Stripe-hosted
   invoice/receipt link.
2. If they need specific VAT details (their VAT number on the invoice, a particular business name), capture
   what they need and adjust via Stripe where possible; note the tax posture (Managed Payments vs Stripe Tax vs
   US/CA-restricted billing) is still being finalised — **[FOUNDER-GATED]** — so escalate edge cases.
3. Don't promise the self-serve lookup page yet (Fast Follow).

**Macro.**
> Absolutely — here's your invoice for the Octadock Local license: [Stripe invoice link / attached]. If you
> need your VAT number or company name shown on it, just send those over and I'll get an updated invoice to
> you. A self-service billing page is on the way, but for now I'm happy to sort receipts directly.

**Prevention.** `invoice_creation=true` on every Checkout session (already the design); ship the self-serve
license-lookup / invoice magic-link page (Fast Follow); finalise the tax posture before opening worldwide billing.

---

## Runbook 15 — Update / "12 months of updates" expectations

**Symptom.** "What do I get for my $49? How long do I get updates? What happens after 12 months? Do I lose the app?"

**Likely cause.** A pre- or post-purchase expectations question about the updates window and the beta→1.0 story.

**The honest terms (ground truth).**
- **$49 one-time** for the Local license (beta price; $59 at 1.0), **3 devices**.
- **Includes 1.0.** Beta buyers' entitlement includes the 1.0 release. The updates window is
  `updates_until = max(purchase + 12 months, 1.0-GA + 12 months)` — so buying during beta does **not** shorten
  the 12-month clock relative to 1.0.
- **12 months of updates.** During the window, they get updates.
- **Keeps working after updates end.** When the update window ends, the app **keeps working** — the license
  doesn't stop functioning; you just stop receiving *new* updates. The entitlement evaluator explicitly treats
  "past update window" as still-Licensed (marked `UpdatesExpired`, app continues).
- **Optional $19/yr renewal** to continue receiving updates after the window (optional, not required to keep
  using what they have).

**Diagnose.**
- Are they asking pre-sale (set expectations) or post-window (reassure the app still works)?
- Check the `licenses` row's `updates_until` for their exact date if they ask "until when?".

**Resolution.**
1. State the terms above plainly; give their exact `updates_until` if they want the date.
2. Reassure: expiry of the updates window is not expiry of the license — the software keeps running.
3. Mention the optional $19/yr renewal only as an *option* for continued updates. (Note: don't push renewal in
   beta until an update can actually reach a buyer — the in-app update check is Fast Follow.)

**Macro.**
> Your $49 Octadock Local license is a one-time purchase for up to 3 devices, and it includes the 1.0 release.
> You get 12 months of updates (and because it includes 1.0, that clock is measured generously — you won't
> lose out by buying during the beta). After those 12 months, Octadock keeps working exactly as it is — you
> just stop getting new updates unless you opt into an optional $19/year renewal. You never lose the app you paid for.

**Prevention.** Publish the `updates_until = max(purchase+12mo, 1.0-GA+12mo)` rule on the pricing page; state
"keeps working after updates end" prominently; don't sell the $19/yr renewal until an update can reach a buyer.

---

## Appendix — quick reference

**License-service endpoints:** `POST /webhooks/stripe` (signature-verified, idempotent issuance) ·
`POST /activate` (key + machine_hash → signed entitlement; 409 device limit, 403 inactive, 404 unknown) ·
`GET /trust-anchor` (public key clients embed) · `GET /health` (minimal public liveness) ·
`GET /admin/health` (authenticated, non-cacheable HTML launch-health plus **[FOUNDER-GATED]** network gate).

**License states / chip labels the buyer sees:** `Trial` (N days left) · `Licensed` · `Trial ended` ·
`Paused` (clock frozen) · `Revoked`. Full use is allowed only in active `Trial` or `Licensed`.

**Client entitlement/trial storage:** `entitlement.json` is Ed25519-signed; `trial.json` is unsigned local
state. Both live under `%LOCALAPPDATA%\Octadock\license\`, outside `octadock.db`. Entitlement forgery fails
verification, but trial deletion/replay resistance is not complete pending B-04.

**Audit actions to search in `audit_log`:** `license.issued`, `license.revoked` (and dispute variants),
`device.activated`, `device.activation_failed` (with `reason=`).

**Device policy:** 3 devices, `machine_hash = SHA-256(MachineGuid)`. Deactivate/migrate/LRU and self-serve
device management are **Fast Follow** — support-assisted at beta via the `activations` table.

**Escalate to the founder for:** manual key issuance when the webhook path is down, offline activation for
air-gapped machines, KMS/signing questions, tax/VAT edge cases, any suspected security issue, and any a11y
defect that blocks purchase or activation.

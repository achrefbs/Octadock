# Octadock 0-to-100 — Founder Action Checklist (BLOCKED-ON-FOUNDER)

These items **cannot be completed by the engineering agent**. They are identity, money, DNS, legal,
and live-card gates. Each is written so the founder can execute it directly. The agent will not fake,
stub, or mark any of these "done" — status flips only when the **Acceptance artifact** is captured and
pasted into [`EXECUTION_STATUS.md`](EXECUTION_STATUS.md).

Sequence note (§17): **items 1–3 are the long-lead calendar clocks — start them first, today.** Every day
they slip pushes the SmartScreen-reputation and email-deliverability warm-up windows past the beta date.
The engineering agent is building items 4–13 in parallel; none of that work can *open* beta until 1–3 land.

Legend: ☐ not started · ◐ in review/warm-up · ☑ accepted (artifact captured).

---

## ITEM 1 — Code-signing identity validation + SmartScreen warm-up  ·  WS1 · R1/R10

**Why now:** unsigned installers trip SmartScreen/EDR and the download funnel dies at the first
double-click. Reputation accrues over *weeks of real signed downloads* — this clock cannot be
compressed later. Microsoft's current guidance: **EV certificates no longer bypass SmartScreen**, so do
**not** wait for an EV cert; submit identity validation today. Recommended path: **Azure Trusted
Signing (Artifact Signing)** (~$10/mo), which signs in the cloud with a non-exportable key.

**Steps**
1. ☐ Confirm the legal signing identity (individual vs. registered business "Surus Labs"). Azure Trusted
   Signing has **geographic + 3-year-history** eligibility rules — verify the chosen identity qualifies
   before starting. If the individual identity fails eligibility, decide business registration now.
2. ☐ In the Azure portal, create a **Trusted Signing account** + a **Certificate Profile** (Public Trust).
3. ☐ Submit the **identity validation** request under that one stable identity (name/address must match
   government/business records exactly — mismatches restart the clock).
4. ☐ Record the submission confirmation + **expected completion date**.
5. ◐ On approval, hand the agent the account/endpoint/profile names so CI can call `signtool` /
   `Azure.CodeSigning` on the self-contained installer (item 4 output).
6. ◐ Begin **reputation warm-up**: publish the signed free build and drive real downloads for **3–4 weeks**
   before checkout opens. Submit each signed release to `microsoft.com/wdsi` for reputation.

**Console:** Azure Portal → Trusted Signing accounts → *(account)* → Certificate profiles / Identity validations.

**Acceptance artifact (paste into ledger):**
- Screenshot of the identity-validation request showing status **"In review"** (or "Completed"), with the
  identity name + submission date.
- Once approved: `signtool verify /pa Octadock-<ver>-setup.exe` output showing a valid chain (CI will run this).
- Clean-VM check: after the warm-up window, download→install→first-run shows **no "Windows protected your PC"** interstitial.

---

## ITEM 2 — DNS zone + mail subdomain (SPF/DKIM/DMARC) + email warm-up  ·  WS2 · R4

**Why now:** a cold sending domain will spam-file the license-delivery email. Deliverability is a
*calendar* dependency (warm-up), not an engineering one. The success-page inline key is the primary
delivery surface, but email is the durable backup and must land in the inbox.

**Recommended layout (per plan §4/§7):** apex `octadock.com`; subdomains `www`, `api` (webhook host),
`mail` (transactional). Sender = `mail.octadock.com` via **Postmark** (or equivalent). Downloads hosting =
Cloudflare Pages/R2 for the canonical Downloads metric.

**Steps**
1. ☐ Confirm the registrar holds `octadock.com` (this repo's environment has a Namecheap MCP connected —
   the agent can *read/propose* records but will **not** change DNS without explicit founder go-ahead).
2. ☐ Create DNS records: `www` (site), `api` (license service), `mail` (transactional sender).
3. ☐ Stand up the email provider (Postmark) on `mail.octadock.com`; add its **SPF** (`include:`),
   **DKIM** (provider-generated selector), and a **DMARC** policy (`p=none` to start, tighten to
   `quarantine` after warm-up).
4. ☐ Send a test message from `mail.octadock.com` to a **fresh Gmail and a fresh Outlook** inbox.
5. ◐ Warm up: send low volume for ~1–2 weeks before launch so the domain earns sender reputation.

**Console:** Registrar DNS panel + Postmark → Sending → Domains (SPF/DKIM/Return-Path verified green).

**Acceptance artifact:**
- `mail-tester.com` score **≥ 9/10** from a `mail.octadock.com` test send.
- Screenshots: DKIM + SPF + DMARC all **verified/passing** in the provider console.
- The test key-delivery email arrives in the **Primary/Inbox** (not Spam) of fresh Gmail **and** Outlook.

---

## ITEM 3 — Stripe Managed Payments / tax eligibility decision  ·  WS3 · R5

**Why now:** on **standard** Stripe you are merchant of record and **EU/UK VAT is owed from sale #1** with
no threshold. **Managed Payments** makes Stripe the MoR and removes that burden — but eligibility must be
confirmed in the *live* dashboard. This decision freezes checkout copy and the tax posture.

Context (from Codex audit): Managed Payments reached GA **2026-04-22** and added AI-product tax-code
eligibility **2026-06-02**. Octadock's product is a desktop utility license (not "AI") — confirm the
correct tax code.

**Steps**
1. ☐ Log into the **live** Stripe dashboard (not test mode) for the Octadock/Surus account.
2. ☐ Check **Managed Payments** eligibility for this account + country.
3. ☐ Record the outcome in the decision record: **eligible → enable it (MoR)**; **not eligible → fallback**:
   restrict Checkout billing countries to **US/CA** for beta, then enable **Stripe Tax + OSS/UK** before
   going worldwide.
4. ☐ Confirm the tax **product/price code** for a one-time software license.
5. ☐ Note this feeds the checkout copy freeze (tax wording) and WS3 webhook/tax wiring.

**Console:** Stripe Dashboard (live) → Settings → Payments / Managed Payments; Tax → Settings.

**Acceptance artifact:**
- Screenshot of the live dashboard showing Managed Payments eligibility state (eligible / not eligible).
- One-line dated decision recorded in the ledger: posture = `Managed Payments` **or** `US/CA-restricted + Stripe Tax later`.

---

## Also blocked-on-founder (tracked here; not part of the first three clocks)

| Gate | WS · Risk | Acceptance artifact |
| --- | --- | --- |
| Legal set sign-off (privacy, refund, EULA/terms + EU immediate-supply consent checkbox) | WS2 · R19/R22 | Live `/privacy`, `/refunds`, EULA; lawyer sign-off note |
| Two live real-card $49 rehearsals (< 60 s each, incl. secret rotation + duplicate replay) | WS11 · R2/R3/R4 | Recorded run: card → success-page key → email → activated, twice |
| Support mailbox `support@octadock.com` live + 1-business-day SLA | WS12 · R4 | Test email round-trips to founder |
| §4 Open Decisions ratification (tax, email provider, admin hosting/auth, "3 devices" meaning, 1.0 entitlement clock, refund policy, a11y bar, downloads metric, portal-vs-lookup) | multiple | Dated decision record; agent uses "safest default" until then |
| Post-expiry verb matrix ratification (§17 item 14) | WS5 · R16/R17 | Founder sign-off on the drafted matrix |

---

_Status flips only when the artifact is captured. The agent will keep building items 4–13 meanwhile._

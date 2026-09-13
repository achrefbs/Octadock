> Historical document: product scope and commercial/cloud instructions below are superseded by `docs/LOCAL-SOFTWARE.md` (13 September 2026). Use the root README for current setup and behavior.

# Octadock — Frequently Asked Questions

> **Pre-launch draft:** Current alpha behavior is documented below. The paid-beta trial policy is not final;
> deletion/replay resistance and whether cold start needs a brief connection remain open under B-04.

A plain-English FAQ for people trying, buying, and using Octadock. Everything here is written to match
what the app actually does and what the website says — no marketing overreach.

---

## Trying Octadock

### Is there a free trial? Do I need an account or a credit card?

The product direction is **14 days, free, with no account and no credit card**. In the current alpha the
trial starts on first launch. The paid-beta enforcement policy is still being finalized; it will not add a
sign-up, email, or card requirement.

### Does the trial start online or offline?

The current alpha starts offline with **zero network required**. That is not yet a paid-beta promise: the
recommended B-04 policy requires a brief connection on cold start so deleting or replaying local state cannot
silently create another trial.

### What happens when the trial ends?

Octadock keeps letting you **view and export everything you've already captured** — your history and
clipboard items stay available. What pauses is *creating new content*: new captures, OCR, dictation,
recording, read-aloud, and text transforms are gated until you buy a license.
Your clipboard history also stops recording new items (it doesn't keep capturing your clipboard after the
trial ends). Nothing you already made is locked away or deleted.

### My trial says it ended early, or that my clock looks wrong. What's going on?

The current alpha uses a local monotonic high-water rollback check. While its unsigned local state remains
intact, setting the PC clock to the past can pause the countdown and show a "your PC clock looks wrong" note
instead of silently eating trial days. Set Windows date & time correctly (turning on "Set time automatically"
is ideal) and reopen the app. The check does not yet resist state deletion or replay; that is the open B-04 work.

### If I uninstall and reinstall, does the trial reset?

In the current alpha, it can: wiping unsigned local trial state can reset the trial. This is a known B-04
security limitation, not an accepted paid-beta policy. The recommended fix preserves a no-account/no-card
trial while using an anonymous server-authoritative device expiry.

---

## Buying a license

### How much does it cost and what do I get?

**$49, one-time**, for the Octadock **Local** license (this is the beta price; it becomes $59 at 1.0). That gets you:

- Use on **up to 3 devices**.
- **12 months of updates.**
- The **1.0 release is included** — buying during the beta doesn't leave you short of 1.0.
- The app **keeps working after the update window ends** — you never lose what you paid for.

### What happens after my 12 months of updates run out?

Octadock keeps working exactly as it is. You simply stop receiving *new* updates. If you want to keep getting
updates after the window, there's an **optional $19/year renewal** — optional, because you don't need it to keep
using the version you have.

### Is the "12 months" counted from when I buy, or from 1.0?

Whichever is more generous to you. The updates window runs until `max(purchase date + 12 months, 1.0 release
date + 12 months)`, so buying early in the beta doesn't shorten your window relative to the 1.0 launch.

### Do I need to create an account to buy?

No. **Octadock has no first-party accounts.** Your identity is simply your **license key**, your **purchase
email**, and your Stripe records. There's no username or password to manage.

### How do I pay? Is my card safe?

Payment is handled by **Stripe** (the same processor countless products use). We never see or store your card
details — Stripe does.

### Can I get a VAT invoice / proper receipt?

Yes. An invoice is generated for every purchase. If you need your VAT number or company name on it, contact
support and we'll get you a correct invoice.

---

## Activating

### How do I activate after I buy?

Two ways:

1. **Paste your key.** Open Octadock → **Settings → Account & Billing**, paste your license key
   (`OCTA-XXXXX-XXXXX-XXXXX-XXXXX`) into the box, and click **Activate**.
2. **One-click link.** Click an `octadock://activate` link (from your purchase page/email) and Octadock
   activates directly.

Don't worry about spaces or capitalisation — Octadock cleans up the key format for you.

### I paid but didn't get my key. What do I do?

First check your spam / Promotions folder (new-domain email sometimes gets filtered). Your key also appears
on the **success page** right after checkout. If you still can't find it, contact support with your purchase
email and we'll get your key to you right away.

### My key says "not recognized." Help?

Almost always a copy-paste hiccup — make sure you've pasted the whole key (`OCTA-` through the last group).
Octadock tolerates spacing and case, so a clean paste of the full key works. If it's still not accepted,
contact support and we'll check your key on our side.

### Do I have to be online to activate?

You need to be online **once** to activate. After that first activation, Octadock works offline on that
machine. If a machine is permanently offline / air-gapped, contact support — we can help you activate it.

---

## Privacy and how Octadock uses the network

### Is Octadock private? Does my stuff leave my PC?

Octadock is **local-first**. By default, your captures, annotations, OCR, screen recordings, and history
live only on your PC — there are **no network requests during capture, annotation, OCR, or recording** unless
you configure one yourself.

There are a few honest exceptions, all in your control (see below). We publish a **network-egress table** on
our website that lists every call the app can make, so nothing is hidden.

### So is it "fully offline"? Is the AI local?

We won't claim either as a blanket statement, because it wouldn't be true for a couple of opt-in features:

- **Dictation** downloads its speech model **once** (a few hundred MB), and only after showing you a consent
  prompt you can decline. After that download, dictation runs **on your device**.
- **Agent Workspace** turns a goal, acceptance criteria, and selected local evidence into an exact reviewed
  task packet for the **Codex or Claude CLI you select**. Before anything leaves the app, Octadock shows the
  exact outbound task, destination, common text-secret detections, and the unchanged-pixel/binary boundary.
  Redaction is on by default and confirmation is destination-named. The CLI may use its configured remote
  service, so this is a cloud hop — not "local AI." There is no silent provider switch.
- **Optional cloud voices/transcription** — OpenAI (dictation) and ElevenLabs (read-aloud voices) are strictly
  **opt-in** and **billed by those providers**, not by us. Octadock only uses cloud transcription if you set an
  `OCTADOCK_`-prefixed API key; a plain `OPENAI_API_KEY` on your system is never used silently.

If you don't use those opt-in features, nothing goes out. Opening Agent Workspace alone sends nothing. Read-aloud
with the built-in Windows voices, OCR, and everything in the core capture loop stay on your PC.

### What data do you store about me on your servers?

Only what's tied to a purchase, in our license service: your **purchase email**, your **Stripe references**,
your **license key** and its status, and a per-device **activation fingerprint** — a one-way `SHA-256` hash of
your machine ID (we can't reverse it to identify your hardware). Your captures and history are **never** on
our servers. In the current alpha, trial state is also local; the recommended B-04 policy would store only a
server-derived pseudonymous trial subject and expiry, which must be added to the privacy field list. Card data
is held by Stripe, not us. You can request access to or erasure of personal data by contacting support.

---

## Devices, refunds, and limits

### How many devices can I use?

Up to **3 devices** on one license.

### What if I hit the device limit, or replace/reinstall a machine?

Reinstalling Windows or swapping a PC can leave an old device holding a slot. If you hit the limit, contact
support and we'll free up the slot for the machine you no longer use so you can activate the new one.
Reinstalling Windows shouldn't cost you a device — if it looks like it did, we'll fix it. (Self-service device
management is on the way.)

### Can I get a refund?

Yes. There's a **14-day money-back guarantee** — if Octadock isn't for you, contact support within 14 days for
a full refund. If you're in the EU/UK, you also have a **statutory right of withdrawal**; note that because
Octadock is delivered as an immediate digital download, checkout asks you to consent to that immediate supply,
and your statutory rights are preserved as described in our terms. When a refund is processed, the license is
deactivated on your next connection — but anything you've already captured stays on your PC.

---

## Features and roadmap

### What are "Pro" and "Context"? Can I buy them?

- **Pro** is **waitlist-only** right now — it's not on sale yet. You can join the waitlist on our site; there's
  no way to buy Pro today, and the Local license doesn't include unlimited hosted AI.
- **Context** exists today as a local work surface: you can package files/captures, include or exclude
  items, and export a safe folder or zip. **Agent Workspace** is also shipped, with evidence provenance,
  acceptance criteria, exact outbound review, hashes, visual comparison, and default-on local text-secret
  redaction before a confirmed read-only Codex/Claude CLI handoff. Future Context source
  integrations, MCP exposure, and hosted-provider support are still in development and are not part of the
  checkout promise.

We'd rather tell you plainly what exists than sell you something that isn't there yet.

### Does screen recording capture audio?

Yes, optionally. Recording is **Beta** and video-only by default; you can turn on **microphone** and
**system/app audio** in Settings → Recording. Both are off unless you enable them, and everything stays
on your PC either way.

### Where can I see everything Octadock does?

The website lists the current features honestly (including that recording is Beta with optional audio,
and which features use the network). For the deepest detail, the project's own docs describe the
as-built state.

---

## Getting help

Need a hand? Contact **support@octadock.com** — we aim to reply within **1 business day**. Have your purchase
email handy so we can find your license quickly.

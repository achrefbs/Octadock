# Octadock Product Strategy — July 2026

## Product thesis

Octadock is the local-first Windows **capture-to-context workspace** for people who build, explain, and debug things on a computer.

The shortest useful description is:

> Turn anything on your Windows screen into usable context.

The core loop is:

`capture or dictate → inspect or transform → keep on the Shelf or add to Context → copy, export, or explicitly send`

Octadock is not trying to be the Windows app with the longest screenshot feature list. ShareX already makes a very broad capture and automation surface available for free. Octadock earns its price through a faster, calmer workflow that joins high-quality capture, cursor dictation, durable Context packages, and evidence-rich tasks that make the user’s existing agent substantially more effective.

## Launch customer

Primary customer:

- a developer, designer, technical founder, support engineer, analyst, or creator on Windows;
- captures bugs, UI, logs, documentation, or research many times per day;
- already pastes screenshots, files, and dictated notes into editors or AI tools;
- values speed and polish but does not want passive screen monitoring or surprise uploads.

The product should remain understandable to a casual screenshot user, but launch decisions optimize for this high-frequency technical workflow.

## Feature decisions

### Keep and make exceptional

| Capability | Product role | Launch standard |
| --- | --- | --- |
| Capture → Shelf | Acquisition and daily habit | Area, window, full screen, timer, safe save, copy, drag-out, annotate, pin, discard/restore, History; first useful result in seconds. |
| Pins and annotation | Differentiated capture follow-through | Fast, reversible, keyboard accessible, and reliable across restarts and monitors. |
| Local OCR | Bridge from pixels to text | Region/file input, explicit local label, useful output modes, bounded files, cancellation, and no clipboard damage on failure. |
| Dictation at the cursor | High-frequency second pillar | Local by default after a disclosed model download; cancellable preparation; live partials; trustworthy clipboard fallback; clear provider/language readiness. |
| Context | Product moat | Durable snapshots, verified large-file references, per-item include/exclude review, safe folder/zip export, and no stale or changed files. |
| Agent Workspace | Paid-value multiplier and product moat | Build evidence-rich, deterministic tasks from captures, Context, voice intent, OCR, files, annotations, and visual verification; exact packet review; default text-secret redaction; honest pixel boundary; named read-only CLI destination; explicit confirmation; ephemeral result. |
| History | Recovery and trust | Search/filter, truthful metadata, preview, copy/export/delete, and no sharing claim. |

### Keep, but make secondary

| Capability | Decision |
| --- | --- |
| Clipboard history | Keep under Library/tray and settings. It is useful, but it should not compete with capture, dictation, or Context in the primary Dock. |
| Local text transforms | Keep as a compact toolbox/palette. Do not market them as a standalone reason to buy. |
| Read aloud | Keep for accessibility and Agent Workspace result follow-through. It is a secondary action, not a core homepage pillar. |
| File preview | Keep safe image/text/CSV/JSON/Markdown and metadata preview. Treat executable, unknown, remote, and very large files conservatively. |
| Automation CLI/protocol | Keep for power users and integrations. It should open the same reviewed UI for privacy-sensitive actions. |

### Demote to Beta or Labs

| Capability | Decision and exit criteria |
| --- | --- |
| Manual scrolling capture | Beta until motion/overlap validation, memory limits, DPI coverage, and a supported-app matrix are proven on real hardware. Never imply universal scrolling. |
| Screen recording | Beta and **video only** until multi-device hardware QA, encoder failure telemetry, finalization integrity, and audio encoding are complete. Do not lead marketing with it. |

### Remove or defer

- Passive AI/session discovery, background screen understanding, background agent mission control, and any screen-watching product story.
- The historical Active AI Sessions surface and every route or claim that suggests it still exists.
- A giant Command Deck/dashboard before the focused surfaces have strong daily retention.
- Direct PDF/Office writeback, universal file editing, legacy Office parsing, and arbitrary archive support.
- Hosted screenshot sharing, teams, sync, and collaboration until the local product has repeatable paid demand.
- Provider fallback, hidden agent sends, stored prompt/session history, or API-key management inside the first handoff slice.
- Audio recording claims before audio tracks are actually encoded and verified.

## Experience principles

1. **Explicit over ambient.** Octadock acts when the user captures, dictates, adds, exports, or sends. It does not watch the screen in the background.
2. **Local by default, precise about exceptions.** “Local-first” is not “fully offline.” Model downloads, activation/update checks, opt-in cloud speech, and user-selected AI CLIs are named at the decision point.
3. **The exact payload is reviewable.** Context export selection and the full Agent Packet are visible before data crosses a boundary.
4. **Failure is recoverable.** User-file writes are atomic/revision-backed; discarded shelf items restore durably; partial recordings and exports never masquerade as success.
5. **Calm density.** The Dock contains the daily loop. Secondary utilities stay in Library/tray. Floating surfaces use native-feeling Obsidian glass with solid/high-contrast fallbacks.
6. **Beta is a promise of scope, not an excuse.** Experimental features are labeled with their real limitation and fail closed.

## Packaging and pricing

### Launch offer

**Octadock Local — $49 paid beta**

- 14-day, no-account, no-card trial;
- one perpetual local license for up to 3 personal devices;
- all shipped local features, including reviewed Agent Workspace handoff through the customer’s installed CLI;
- 12 months of updates, including version 1.0;
- the last entitled version keeps working after updates end;
- optional $19 renewal for another year of updates;
- 14-day voluntary refund window after purchase, aligned with the published policy.

Move the new-license price to **$59 at 1.0** only after capture, dictation, Context, installer/update, and multi-device hardware gates are complete. Existing paid-beta customers keep their entitlement.

This price is intentionally not justified by screenshot capture alone. Current official reference points include CleanShot X at $29 for one Mac with one year of updates and an optional $19 renewal, Snagit Individual at $39/year, ShareX as free/open source, and Superwhisper Pro at roughly $8.49/month. Octadock’s commercial case is the combined Windows workflow and local-first trust model, not feature-count parity with any one competitor.

### What not to sell yet

Do not sell “Pro,” credits, hosted AI, cloud speech bundles, sharing, or teams before measured customer demand and known unit economics. Keep a Pro interest list only. If a hosted tier is later justified, it must have a distinct benefit and budget boundary; it must not make the local license feel intentionally crippled.

## Conversion path

1. Homepage and first run lead with the outcome, not the toolbox: capture or dictate something and turn it into context.
2. First-run setup asks only for launch-at-login and explains the local/network boundary.
3. The first session should produce one Shelf action and one dictated insertion in under ten minutes.
4. Context and Agent Workspace appear after the user understands the local artifact flow; they should not block basic capture.
5. Trial messaging appears ambiently near expiry. Existing artifacts always remain viewable/exportable after expiry.
6. Purchase returns the user to the exact blocked creation action after activation where feasible.

## Metrics for the paid beta

Instrument locally first and request consent before any analytics leaves the device.

| Funnel | Starting metric |
| --- | --- |
| Activation | User completes a capture and a post-capture action in session one. |
| Voice activation | User completes one successful dictated insertion within the first three days. |
| Core retention | Weekly active users completing at least five capture/dictation actions. |
| Context adoption | Activated users who create and export one Context within 14 days. |
| Agent value | Users who build and confirm an Agent Packet, then complete or verify a task; cancellation remains a healthy privacy signal, not an error. |
| Trust | Failed/corrupt saves, false-success recordings, missing Context entries, and unintended external sends: target zero. |
| Commercial | Trial-to-paid conversion, refund rate, activation support rate, and renewal intent. A 4–8% trial-to-paid range is a hypothesis to validate, not a forecast. |

Do not optimize raw feature opens. Optimize successful outcomes, repeat use, low failure/support burden, and purchase conversion among activated users.

## Launch gates outside the application repository

The product is not publicly launch-ready until all of the following are real:

- signed installer and update artifacts with a published SHA-256;
- production domain, download host, privacy/terms/EULA review, and support mailbox;
- Stripe checkout, webhook reconciliation, activation/KMS, refunds, and device reset runbooks exercised in production-like staging;
- Windows 10/11 hardware matrix for capture, mixed DPI, multi-monitor, dictation devices/languages, OCR, GPU/encoder combinations, suspend/resume, RDP, and high contrast;
- crash/update observability that does not capture user artifacts;
- accessibility and rendered visual QA on every primary surface.

## Market references checked 2026-07-09

- CleanShot X official purchase and update model: <https://cleanshot.com/buy> and <https://cleanshot.com/why-updates-expire>
- TechSmith Snagit official store: <https://www.techsmith.com/store/snagit/>
- ShareX official project: <https://getsharex.com/> and <https://github.com/ShareX/ShareX>
- Superwhisper official product/pricing page: <https://superwhisper.com/>

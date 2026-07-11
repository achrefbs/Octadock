# Octadock Roadmap And Launch Plan

Last updated: 2026-07-10

Status: living execution plan. Use `docs/PROJECT-STATE.md` and
`docs/CAPABILITIES.md` for implementation truth, and
`docs/PRODUCT-STRATEGY-2026-07.md` for positioning, pricing, and product
decisions.

## Product Direction

Octadock is a local-first Windows capture-to-context workspace for people who
build, explain, and debug things on a computer.

The primary workflow is:

`capture or dictate -> assemble evidence -> review an Agent Packet -> hand off read-only -> verify`

The launch plan optimizes this workflow. It does not expand Octadock into a
background screen watcher, a general model/tool activity monitor, a universal
document editor, or a hosted collaboration platform.

## Launch Scope

### Primary product pillars

| Pillar | Implemented now | Remaining launch gate |
| --- | --- | --- |
| Capture -> Shelf | Area, window, full-screen, previous-area, timer, OCR, safe persistence, copy, drag-out, annotate, pin, History, discard, and durable restore are wired. | Rendered visual QA, mixed-DPI/multi-monitor hardware coverage, and zero corrupt/false-success saves. |
| Dictation at the cursor | Parakeet is the local default after disclosed model download; local Whisper fallback, explicit OpenAI opt-in, live partials, cancellable preparation, discard, and clipboard-safe insertion are wired. | Microphone/privacy/device/language matrix, long-session QA, and clear readiness/error states on clean machines. |
| Context | Named durable packages, snapshots, verified large-file references, item include/exclude review, export preview, and safe folder/zip export are wired. | Broader source entry points, notes/reorder polish, rendered QA, and adversarial changed/missing-file export tests. |
| Agent Workspace | Deterministic TASK/manifest/SHA256SUMS packets combine goal, acceptance criteria, captures, Context, clipboard, files, annotations, OCR, bounded/hash-locked attachments, and grouped before/after visual verification. Text redaction, unchanged-pixel disclosure, named pre-materialization confirmation, tool-isolated selected-provider-only Codex/Claude handoff, temp leases, and crash scavenging are wired. | Clean-machine CLI canary and rendered accessibility QA, broader contextual entry points, automatic recapture metadata, and pixel redaction. |

### Useful secondary capabilities

Clipboard history, local text transforms, read aloud, safe file preview,
annotation, pins, and the automation CLI/protocol remain supported. They should
stay discoverable through Library, tray, Settings, and contextual actions, but
should not crowd the primary Dock workflow.

### Beta capabilities

| Capability | Honest shipped scope | Beta exit criteria |
| --- | --- | --- |
| Manual scrolling capture | Manual vertical scrolling only. Horizontal and auto-scroll modes are rejected. | Supported-app matrix, motion/overlap validation, memory/DPI coverage, and reliable failure handling on sticky/sparse pages. |
| Screen recording | Active-monitor or selected-region MP4 video only. No microphone or system-audio track is encoded. | Encoder/finalization and multi-device hardware matrix, suspend/disk-pressure recovery, and audio only after encoded tracks are implemented and verified. |

### Explicitly deferred

- passive screen understanding, background session discovery, and background
  agent monitoring;
- hidden provider fallback, stored AI prompt/session history, and surprise data
  sends;
- hosted screenshot sharing, teams, cloud sync, and collaboration;
- universal PDF/Office/archive editing or direct non-image writeback;
- a giant command dashboard, developer mini-tool collection, or hosted AI tier
  before the focused local workflow has repeatable paid demand.

## Phase 0: Truth And Release Gates

Goal: finish the current product pass without allowing code, UI, docs, or sales
copy to diverge.

| Work | Acceptance |
| --- | --- |
| Source-of-truth audit | README, `PROJECT-STATE`, `CAPABILITIES`, CLI help, automation docs, website, and in-app labels describe the same shipped boundaries. |
| Rendered design QA | Primary surfaces have fixed-viewport screenshots, contrast/focus checks, and recorded before/after acceptance. High contrast and reduced-transparency fallbacks remain usable. |
| Whole-product verification | Debug and Release tests pass; capture, dictation, Context export, and reviewed AI cancellation/send paths receive focused regression coverage. |
| Hardware matrix | Windows 10/11, mixed DPI, multiple monitors, microphone/privacy states, OCR, and representative GPU/encoder combinations are exercised on real machines. |
| Failure integrity | Atomic writes, durable discard/restore, reference verification, recording cleanup, and explicit network boundaries never report success for an incomplete artifact. |

## Phase 1: Paid-Beta Product Quality

Goal: make the four primary pillars dependable enough to earn repeat use and a
purchase.

| Area | Work | Acceptance |
| --- | --- | --- |
| Capture | Finish mixed-DPI overlays, shelf/history polish, shortcut discoverability, and error recovery. | A new user gets a useful capture in seconds and can recover it after copy, edit, discard, or restart. |
| Dictation | Harden model readiness/download UX, cancellation, device loss, partial preservation, and insertion fallback. | Stop-to-text is predictable and a failed/cancelled dictation never leaves the microphone or clipboard in the wrong state. |
| Context | Complete source entry points that support the core loop, then refine package naming, notes/reorder, and export review. | Export contains exactly the items shown as included and never silently uses a changed or missing reference. |
| Agent Workspace | Harden packet composition, attachment review, OCR enrichment, visual verification, and destination capability disclosure without adding persistence or background discovery. | Nothing crosses the process boundary before exact-packet review and a fresh destination-named confirmation; analyze-only profiles cannot edit user files. |
| Onboarding | Lead with “Turn anything on your screen into usable context,” then guide one capture and one dictation. | A first session can complete one capture action and one dictated insertion in under ten minutes. |

## Phase 2: Commercial And Distribution Readiness

Goal: ship the paid beta described in `docs/PRODUCT-STRATEGY-2026-07.md` without
selling external infrastructure that is still a placeholder.

| Work | Acceptance |
| --- | --- |
| Installer and signing | Choose installer/MSIX path, code-sign binaries, build SmartScreen reputation, and verify install/uninstall on clean Windows 10/11 VMs. |
| Updates | Host and sign the update manifest/artifacts; expose a safe user flow that cannot downgrade or trust an invalid signature. |
| Payments and activation | Configure production Stripe, KMS signing, webhook reconciliation, refunds, device limits/reset, and support escalation; rehearse money -> entitlement -> activation. |
| Website and legal | Publish real DNS/download URLs and SHA-256 values; complete privacy/terms/EULA review and support mailbox readiness. |
| Privacy-safe operations | Crash/update observability contains no user artifacts; any product analytics is consented, bounded, and documented before transmission. |

## Post-Launch, Demand-Led Work

After paid users demonstrate the focused workflow has retention, evaluate:

- MCP and local-model packet destinations with separate permission/capability design;
- richer safe previews where licensing and sandboxing are clear;
- recording audio and advanced recorder features after the video core exits Beta;
- hosted sharing or team workflows only with measured demand and sustainable
  unit economics.

These are not prerequisites for proving the local capture-to-context product.

## Planning Rule

Every capability enters through the same spine:

`hotkey/tray/dock/protocol/CLI -> parser -> service -> reviewed UI -> persistence/export -> tests`

Privacy-sensitive actions add:

`local validation/redaction -> exact payload preview -> named destination -> explicit confirmation -> no hidden fallback`

Visual work adds:

`design token -> shared control/style -> rendered screenshot gate -> keyboard/high-contrast acceptance`

This keeps Octadock cohesive and makes every launch claim traceable to a real
surface, service, and verification path.

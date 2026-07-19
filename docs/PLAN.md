# Octadock — The Plan

Updated: 2026-07-19. This is the single entry point for anyone — human or agent —
working on Octadock. Task-level authority: `docs/ROADMAP.md`. Current-behavior
truth: `docs/PROJECT-STATE.md` and `docs/CAPABILITIES.md`. Product decision
record: `docs/strategy/SCOPE-RESET-2026-07-19.md`.

## What we are building

**Octadock is the local memory and handoff layer between a vibe coder and their
agents.** Local-first; Claude/Codex participate only through explicit, reviewed,
redacted opt-in. Octadock never edits code, never runs the agent, never competes
with the terminal.

Four jobs:

1. **Feed the agent** — capture, dictate, OCR, Context → reviewed, redacted
   handoff to the user's own CLI. Consolidated locally; rendered C-01/C-03
   acceptance remains.
2. **Remember** — Projects + Thoughts: a brain rendered at read time from live
   artifacts (git state, agent session files), never a synced vault. Gate E, next.
3. **Recover** — History, clipboard history, pins, Library. Built, secondary.
4. **Keep the machine sane** — read-only Tidy report over registered project
   roots. Gate E, last.

## Phases

| Phase | Content | Status |
| --- | --- | --- |
| **1 — Finish recovery** | ROADMAP Gates A–C, plus starting Gate D external tasks | **ACTIVE — the only phase agents may work on** |
| 2 — Memory spine | Gate E-01…E-04 (Thoughts, registry/resolver, Projects screen) | Not started — do not begin |
| 3 — Paid beta | $49 launch on the spine story (recommendation: after E-04) | Waiting on 1–2 |
| 4 — Post-launch | E-05 session lens · E-06 opt-in synthesis · E-07 Tidy | Entitled updates |

## Phase 1 — definition of done

One canonical code path; CI a trustworthy signal; known security findings closed
or explicitly accepted; the four primary workflows pass real-Windows acceptance;
the signed download/purchase/activation path rehearsed end to end. Full gate
tables live in `docs/ROADMAP.md`.

### Founder-owned blockers (nobody else can do these)

- A-03 GitHub Actions billing/quota access
- A-07 GitHub plan / branch-protection settings
- A-09 approval of worktree/archive cleanup
- **B-04 offline-trial policy decision** — unblocks the B-09 security rescan
- Gate D: signing certificate, live Stripe checkout, legal review

### Remaining agent-executable work (briefs in `docs/agents/PHASE-1-BRIEF.md`)

- Preserve and land the verified Phase 1 candidate without overwriting unrelated
  in-flight preview/context work. The per-file integration inventory is
  `docs/agents/PHASE-1-CANDIDATE-INVENTORY.md`; author review is still required
  before committing the mixed-ownership candidate.
- Finish rendered C-01…C-03 Windows evidence, C-05 isolated measurements/top-three
  fixes, and the C-06/C-07 manual matrices.
- C-04, C-06, and C-07 need founder hardware/profile interaction for final
  evidence; B-09 remains gated by the B-04 policy decision.

## Rules — every contributor, human or agent

1. Done = acceptance evidence (tests, rendered checks, recorded output), never
   merged prose alone.
2. Phase 1 only. No Gate E implementation until Gates A–C exit.
3. Never discard, stash, or revert uncommitted work you did not author.
4. The trust model is non-negotiable: explicit over ambient; no background
   acquisition; no cloud call without a named, reviewed opt-in; secret redaction
   stays default-on; no API keys stored in settings or database.
5. Any change that positions a window, overlay, pill, or dock requires
   mixed-DPI and multi-monitor verification before it is called done.

## Document map

Everything not listed here was removed in the 2026-07-19 cleanup; history lives
in git.

| Doc | Role |
| --- | --- |
| `docs/PLAN.md` | This file — start here |
| `docs/ROADMAP.md` | Gates, tasks, acceptance evidence — execution authority |
| `docs/PROJECT-STATE.md` / `docs/CAPABILITIES.md` | What the app actually does today |
| `docs/PRODUCT-STRATEGY-2026-07.md` | Positioning, pricing, launch gates |
| `docs/strategy/SCOPE-RESET-2026-07-19.md` | The approved product direction (memory spine, preview freeze) |
| `docs/strategy/WORKFLOW-INTELLIGENCE-INTERNAL-ADDENDUM.md` | Internal-edition governance; gates E-05 |
| `docs/specs/SIGNAL-LENS.md` | Internal Signal Lens direction; feeds E-05 |
| `docs/proposals/voice-thought-project-resolver-concept.md` | Thought/resolver design source; feeds E-01…E-03 |
| `docs/research/VIBE-DEVELOPER-RESEARCH-2026-07-03.md` | Persona research behind the reset |
| `docs/agents/CONTEXT.md` · `docs/agents/PHASE-1-BRIEF.md` | Agent onboarding: codebase map · task briefs |
| `docs/ARCHITECTURE.md` · `docs/TESTING.md` · `docs/AUTOMATION.md` · `docs/VERSIONING.md` · `docs/CONTRIBUTING.md` | Engineering reference |
| `docs/ops/` | Support and commercial-infra runbooks (Gate D) |
| `docs/brand/` · `docs/design/UI-INVENTORY.md` | Brand system · per-surface UI reference (2026-07-05, partially dated) |
| `docs/IMAGE-MOCKUPS.md` | Image-mockup feature doc |

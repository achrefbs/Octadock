# Scope Reset — the Memory Spine

Status: founder-approved direction, 2026-07-19 (working session).
Implementation begins only after ROADMAP Gates A–C exit; recorded there as Gate E.
Visual references: SuperDraw canvases `octadock-product-map` and `octadock-workflow-day` (local).

## The decision

1. **Add the memory spine.** Octadock gains one new primary surface (Projects) and one
   new data type (Thoughts), plus a read-only Tidy report. Together they answer the two
   questions that interrupt vibe coding all day: *"where was I?"* and *"where do I put
   this?"*
2. **Freeze file preview at glance scope.** Current formats only (images, text/code,
   CSV/TSV, JSON, log, Markdown, metadata fallback). Land the in-flight reliability
   hardening, then the surface is done. The old QuickLook expansion proposal was
   superseded and removed in the 2026-07-19 cleanup (recoverable from git history);
   no format expansion will be roadmapped. If a file needs more than a glance,
   "Open externally" is the feature.
   *(Superseded 2026-07-23: the owner removed generic file preview, the image
   surface, and floating pins entirely; see `docs/agents/PHASE-1-PRODUCT-CHANGES-PLAN.md`
   and `docs/PROJECT-STATE.md` → Removed From Product Direction.)*
3. **Do not reopen the toolbox.** The Tier-2/3 utility list (palette, beautifier, color
   picker, snippets, scratchpad) stays demand-led. Utilities do not create retention;
   workflows do.
4. **Positioning.** Octadock is the local memory and handoff layer between a vibe coder
   and their agents. It never edits code, never runs the agent, never competes with the
   terminal.

## Why

- The shipped product is strong verbs (capture, dictate, transform, hand off) with no
  noun that accumulates value. Nothing improves with tenure, so there is no
  open-it-first-every-morning habit. The founder's retention worry is real.
- The founder's Obsidian brain failed structurally, not accidentally: a curated vault is
  a copy of reality that rots, and a scheduled sync job just automates the staleness.
  The spine stores no vault. **The brain is a lens rendered at read time from live
  artifacts** — git state, agent session files, the user's own dictated words — so
  nothing can go stale.
- This direction has been circled three times (`docs/research/VIBE-DEVELOPER-RESEARCH-2026-07-03.md`,
  the Workflow Intelligence internal addendum, the voice/Thought/resolver concept). What
  was rejected each time was a *form* — ambient watching and a standalone AI dashboard —
  not the need.

## Reconciliation with the 2026-07-05 removal

The removal of background AI/session discovery, run/watch/hook tracking, and passive
overlays stands, permanently, for public builds. The spine is different in kind:

- **Pull, never watch.** Git state and Claude Code/Codex session files are read at the
  moment the user opens a lens — the same trust model as running `git status`. No
  daemons, no hooks, no background acquisition.
- **Explicit registry, no crawl.** Projects come from user-designated roots only.
  Disk-wide scanning remains anti-scope.
- **Consent boundaries** are inherited from the resolver concept: *Identify* (local
  metadata only) → *Read* (per-project file reads, separately consented) → *Send*
  (named provider, exact reviewed payload). Session-file reading sits behind *Read*.

## Scope

Four jobs, and the surfaces that serve them:

| Job | Surfaces | State |
| --- | --- | --- |
| Feed the agent | Capture/Shelf, dictation, OCR, Context, Agent Packet review → BYO CLI | Built; C-01 makes the review contextual |
| Remember | **Projects (new window)**, **Thoughts** (live in Shelf/Library), session lens | New — Gate E |
| Recover | History, clipboard history, Library | Built, secondary |
| Keep the machine sane | **Tidy report** (inside Projects) | New — Gate E, read-only first |

Primary surfaces after the reset: Dock, Shelf, Projects, Context, contextual AI review,
Library, Settings. Projects is the only new window; anything demanding another one is
out of scope by default.

**Out (unchanged or reaffirmed):** universal preview (PDF/Office/archives), ambient
watching, live process supervision, MCP (deferred), notebooks/kanban/PM suite,
teams/sync/accounts, utility-belt extras, provider fallback, API-key storage.

## Local-first / Claude opt-in line

Local does all facts: registry, git reads, session parsing, resolver signals, redaction,
packet building. Claude opt-in does only synthesis — catch-me-up, thought clustering,
idea → agent-ready brief — and every send rides the existing reviewed-packet pipeline
(exact payload, default-on redaction, named destination, no fallback). No new trust
surface is created.

## Build sequence (mirrors ROADMAP Gate E)

1. **E-01 Thoughts capture** — dictation destinations become insert / prompt / save
   Thought; audio persists before transcription; `thoughts` table via schema migration.
2. **E-02 Registry + resolver v1** — explicit roots; signals: spoken project name,
   registry match, prior correction; confidence chip, one-click fix, Unplaced queue.
3. **E-03 Thoughts surfaced** — Shelf rows beside captures; Library search + Unplaced.
4. **E-04 Projects screen** — git-only lens (branch, dirty, last commit, last touch),
   resume actions, momentum sort. No session reads yet.
5. **E-05 Session lens** — read-on-open Claude Code/Codex summaries behind the Read
   boundary. Gated on the internal Workflow Intelligence corpus meeting its revisit
   gate (extraction accuracy, critical-constraint recall, latency).
6. **E-06 Opt-in synthesis** — through the existing packet review only.
7. **E-07 Tidy** — read-only report scoped to registered roots: dirty + no-remote,
   stale 30 days+, reclaimable artifact sizes. Actions (archive-move, never delete)
   come later, preview-first.

**Paid-beta timing (recommendation):** launch after E-01–E-04 so the beta ships the
retention story, with E-05–E-07 as entitled updates. The founder may pull launch
earlier once Gates A–D close; that trade (sooner revenue vs. stronger story) is a
founder call.

## Metrics

From the resolver concept, measure trust and retrieval, not capture volume:
project-suggestion accuracy, corrections per capture, time to a safely saved Thought,
later retrieval rate. Add one retention metric for the spine itself: sessions that
begin at the Projects screen (target: it becomes the most common first surface of the
day within 30 days of E-04).

## Amendments applied 2026-07-19

- `docs/ROADMAP.md`: Gate E added; feature-freeze rule scoped to Gates A–C; product
  decisions table updated; passive-discovery clarification.
- `docs/PRODUCT-STRATEGY-2026-07.md`: dated pointer to this decision; full reconcile at
  next revision.
- `docs/proposals/file-preview-quicklook.md`: marked superseded.

## Open founder queue (unchanged blockers, unrelated to this reset)

B-04 offline-trial policy, A-03 GitHub Actions billing, A-07 control-plane settings,
A-09 worktree cleanup approval, D-01/D-02 signing identity, Stripe/legal/production
checkout. The current dirty preview/context hardening in the primary checkout should be
inventoried and folded into the recovery branch per the one-branch rule.

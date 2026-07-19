# Octadock — agent guide (all agents: Codex, Claude, others)

Octadock is a local-first Windows (WPF) capture-to-context workspace becoming
**the local memory and handoff layer between a vibe coder and their agents**.

## Read first, in order

1. `docs/PLAN.md` — phases, rules, document map. **We are in Phase 1 (recovery).
   Do not implement Gate E features (Projects, Thoughts, Tidy) yet.**
2. `docs/agents/PHASE-1-BRIEF.md` — tasks you may pick up, with acceptance criteria.
3. `docs/agents/CONTEXT.md` — codebase map and gotchas.
4. `docs/PROJECT-STATE.md` — current-behavior truth · `docs/ROADMAP.md` — full gate detail.

## Build and test

- Full release gate: `.\build\build.ps1 -Configuration Release` (build, all
  tests, version/public-boundary/copy-honesty gates). This is the signal.
- Scoped runs: `dotnet test` on the affected `tests/` project; see `docs/TESTING.md`.
- GitHub Actions is currently **not** a trustworthy signal (startup failures,
  tracked as ROADMAP A-03/A-04).

## Operating rules

- Done = acceptance evidence (tests, rendered checks, recorded output) — never
  prose alone. Record the evidence in your task output or PR description.
- The working tree may carry in-flight uncommitted work (currently the
  preview/context hardening). **Never stash, revert, or overwrite uncommitted
  changes you did not author.** If they block you, stop and report.
- The trust model is non-negotiable: explicit user action over ambient behavior;
  no background acquisition; no cloud call without a named opt-in; secret
  redaction stays default-on; no API keys in settings or the database.
- Desktop UI is WPF/XAML on the obsidian-glass token system
  (`src/Octadock.App/Resources/Themes/`); every color maps to an
  `Octadock.Brush.*` token. Any positioning change (windows, overlays, pills,
  dock) requires mixed-DPI and multi-monitor verification.
- ~615 analyzer warnings are tracked debt — do not mass-fix them inside an
  unrelated change.
- Recording is Beta video-only; scrolling capture is Beta manual-vertical-only.
  The copy-honesty gate enforces this wording.

## Web UI work (`web/` only)

Design standards are vendored under `.claude/skills/` (`impeccable/`,
`emil-design-eng/`, `review-animations/`). Claude invokes them as slash skills;
other agents should read each skill's `SKILL.md` and apply the same standards.
They target web frontends only — never the WPF desktop shell.

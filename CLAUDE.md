# Octadock — agent guide

Octadock is a local-first Windows (WPF) capture-to-context workspace becoming
**the local memory and handoff layer between a vibe coder and their agents**.

## Read first, in order

1. `docs/PLAN.md` — phases, rules, document map. **We are in Phase 1 (recovery).
   Do not implement Gate E features (Projects, Thoughts, Tidy) yet.**
2. `docs/agents/PHASE-1-BRIEF.md` — the tasks you may pick up, with acceptance criteria.
3. `docs/agents/CONTEXT.md` — codebase map and gotchas.
4. `docs/PROJECT-STATE.md` — current-behavior truth · `docs/ROADMAP.md` — full gate detail.

## Build and test

- Full release gate: `.\build\build.ps1 -Configuration Release` (build, all
  tests, version/public-boundary/copy-honesty gates). This is the signal.
- Scoped runs: `dotnet test` on the affected `tests/` project; see `docs/TESTING.md`.
- GitHub Actions is currently **not** a trustworthy signal (startup failures,
  tracked as ROADMAP A-03/A-04). Do not treat a red remote run as your failure,
  and do not claim a green one as evidence.

## Operating rules

- Done = acceptance evidence (tests, rendered checks, recorded output) — never
  prose alone.
- The working tree may carry in-flight uncommitted work (currently the
  preview/context hardening). **Never stash, revert, or overwrite uncommitted
  changes you did not author.** If they block you, stop and say so.
- The trust model is non-negotiable: explicit user action over ambient behavior;
  no background acquisition; no cloud call without a named opt-in; secret
  redaction stays default-on; no API keys in settings or the database.
- Desktop UI is WPF/XAML on the obsidian-glass token system
  (`src/Octadock.App/Resources/Themes/`). Every color must map to an
  `Octadock.Brush.*` token. Any overlay/window/dock positioning change requires
  mixed-DPI and multi-monitor verification.
- ~615 analyzer warnings are tracked debt — do not mass-fix them inside an
  unrelated change.
- Recording is Beta video-only; scrolling capture is Beta manual-vertical-only.
  The copy-honesty gate enforces this wording — do not soften it.

## Frontend / web UI work (`web/` only): vendored design skills

Two design skill sets are vendored under `.claude/skills/` for any HTML/CSS/JS
web surface (marketing site, pricing pages). They do **not** apply to the WPF
desktop shell.

- **Impeccable:** `/impeccable shape <feature>` → build → `/impeccable critique`
  → fix → `/impeccable polish`. Also `audit` (a11y/perf/responsive), `bolder`,
  `quieter`, `distill`, `animate`, `colorize`, `typeset`, `layout`, `delight`.
  Type `/impeccable` alone for the menu.
- **Emil Kowalski motion guardrails:** `emil-design-eng`, `animation-vocabulary`,
  and `review-animations` — run `review-animations` on anything with transitions
  before shipping (UI animation under ~300 ms, custom easing, no motion on
  high-frequency actions).

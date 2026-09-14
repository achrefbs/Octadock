# Octadock — agent guide

Octadock is a free, local-first Windows capture toolkit (WPF, .NET 8). Read
`README.md`, `AGENTS.md`, `docs/LOCAL-SOFTWARE.md` and `docs/PROJECT-STATE.md`
first. The earlier paid-product plans are gone; `docs/LOCAL-SOFTWARE.md` is the
contract.

## Build and test

- Full release gate: `.\build\build.ps1 -Configuration Release` (build, all
  tests, version, public-boundary and copy-honesty gates). This is the signal.
- Scoped runs: `dotnet test` on the affected `tests/` project; see `docs/TESTING.md`.
- GitHub Actions is not a trustworthy signal yet (runs fail before jobs start).

## Operating rules

- Free and local: no accounts, activation, network clients, cloud providers,
  telemetry or automatic downloads. Secret redaction stays default-on.
- Never stash, revert or overwrite uncommitted changes you did not author.
- Desktop UI is WPF/XAML on the token system in
  `src/Octadock.App/Resources/Themes/`; every color maps to an
  `Octadock.Brush.*` token. Overlay, window and dock positioning changes need
  mixed-DPI and multi-monitor verification.
- Recording is Beta video-only; scrolling capture is Beta manual-vertical-only.
  The copy-honesty gate enforces this wording.
- About 615 analyzer warnings are tracked debt; do not mass-fix them inside an
  unrelated change.
- Do not publish releases or change repository visibility without the owner's
  say-so.

## Web work (`web/` only)

Vendored design skills live in `.claude/skills/` (impeccable, emil-design-eng,
animation-vocabulary, review-animations). They do not apply to the WPF shell.

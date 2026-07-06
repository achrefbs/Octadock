# Octadock — agent guide

## Frontend / web UI work: use the vendored design skills

This repo vendors two design "taste" skill sets under [`.claude/skills/`](.claude/skills/) so every
agent — local or cloud — has them with no separate install. **Use them for any web / frontend
UI work**: landing pages, the marketing and pricing site, and any HTML / CSS / React / Svelte / Vue
surface. They target web frontends and do **not** apply to the WPF/XAML desktop shell.

### Impeccable — invoke `/impeccable <command> [target]`

- **Plan → build:** `/impeccable shape <feature>` (plan UX first), then `/impeccable craft <feature>`
- **Review:** `/impeccable critique <target>` (UX / hierarchy), `/impeccable audit <target>` (a11y, perf, responsive)
- **Refine:** `/impeccable polish` · `bolder` · `quieter` · `distill`
- **Enhance:** `/impeccable animate` · `colorize` · `typeset` · `layout` · `delight`
- **New surface:** run `/impeccable init` once to capture `PRODUCT.md` / `DESIGN.md` context.
- Type `/impeccable` alone for the full command menu.

### Emil Kowalski — always-on motion guardrails

`emil-design-eng`, `review-animations`, and `animation-vocabulary` trigger contextually. They enforce
premium micro-interaction standards: UI animation under ~300ms, custom easing over CSS defaults, no
motion on high-frequency actions. Run `review-animations` on anything with transitions before shipping.

### Default workflow for a UI change

`/impeccable shape` → build → `/impeccable critique` → fix → `/impeccable polish`, with Emil's
`review-animations` on any motion. Prefer these over ad-hoc styling — they exist to keep the design
out of generic-AI territory.

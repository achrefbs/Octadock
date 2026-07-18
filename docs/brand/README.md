# Octadock visual identity

Status: V2 deployed to the desktop app and website
Direction: **V2 — Balanced Ported D-Pod**
Last updated: 2026-07-18

Octadock's identity is an engineered abstract octopus: eight explicit inputs
dock into one calm context surface. The horizontal D-shaped counter is the Dock,
the broad mantle is the workspace, and the eight ports are the
capture-to-context inputs.

## Name

The product name is **Octadock**.

Rebranded surfaces in the clean repo include:

- solution and project names;
- namespaces and assembly names;
- app executable name: `Octadock.exe`;
- CLI executable name: `octadock.exe`;
- protocol scheme: `octadock://`;
- local app data root: `%LOCALAPPDATA%\Octadock`;
- annotation project extension: `.octadock`;
- primary environment variables: `OCTADOCK_*`.

The code keeps transitional support for selected legacy `SNAPDOCK_*` environment
variables so existing local keys can keep working while the rebrand settles.

## Canonical assets

The master direction is **V2 — Balanced Ported D-Pod**. The mark is an abstract
symbol, not a mascot.

- Vector master: `assets/logo/octadock-symbol-master.svg`
- Optical 16 px and 20 px masters: `assets/icons/`
- Outlined wordmark and lockups: `assets/lockups/`
- Windows icon: `assets/icons/octadock-master.ico`
- Desktop runtime icon: `src/Octadock.App/Resources/Icons/octadock.ico`
- Web assets and favicons: `web/assets/brand/`
- Design tokens: `tokens.css`
- Full specification: `brand-spec.md`
- Motion studies: `motion/demo.html` and `motion/exports/`
- Provenance: `provenance.md`

`assets/octadock-logo-source.png` and
`assets/octadock-logo-transparent.png` remain as compatibility raster aliases.
The SVG master is the source of truth.

## Core palette

- Canvas `#070B14`
- Obsidian `#0C1220`
- Frost `#F2F6FC`
- Signal Teal `#2DD4BF`
- Deep Teal `#0F766E` for accessible teal on light backgrounds
- Signal Cyan `#38BDF8` for motion and environmental effects only
- Cloud Violet `#A78BFA` for Cloud/Pro semantics only

Use a flat, one-color logo. On light surfaces use Obsidian or Deep Teal; on dark
surfaces use Frost or Signal Teal. The teal-to-cyan gradient is never the static
master fill.

## Geometry contract

- 512 × 512 master viewBox.
- Exactly eight named ports in four mirrored pairs.
- Horizontal D-shaped negative-space Dock counter.
- Two lateral ports and six lower ports.
- Keep at least 64 master units of clear space.
- Use the master from 24 px upward and the dedicated optical masters at 16/20 px.

Do not add eyes, a face, biological tentacles, suction cups, shadows, bevels, or
extra ports.

## Motion rule

**Dock, don't swim.** Move mirrored port pairs with purpose, keep the Dock counter
invariant, and play hero/success motion once. Honor `prefers-reduced-motion` by
resolving immediately or using at most a 100 ms whole-logo fade.

AI video may supply a separable atmospheric background, but the exact SVG logo
and outlined wordmark must be composited deterministically afterward.

## Acceptance checklist

- SVG contains one mantle, one Dock cutout, and ports `port-01` through `port-08`.
- PNG corners are transparent and the alpha channel is real.
- ICO contains 16, 24, 32, 48, 64, 128, and 256 px frames.
- Mark is legible in Explorer, taskbar, Alt-Tab, title bars, tray, About, First
  Run, Settings, and the 17 px Dock Pill.
- Website header, footer, favicon, Apple touch icon, and manifest use the same
  direction.
- No glossy-blue placeholder, inline placeholder favicon, or pulsing-dot brand
  mark remains in a shipping surface.
- Trademark clearance is recorded before public launch.

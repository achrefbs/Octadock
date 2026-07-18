# Octadock V2 provenance

## Status

This directory records the deployed **V2 — Balanced Ported D-Pod** identity.
The geometry is approved and used by the desktop app, Dock Pill, website, and
favicons. Trademark clearance remains a separate public-launch gate.

## Selection lineage

- Initial user shortlist: `11C`, `10A`, and `03A`.
- Refined selections: `R2-03` (`r2-11c-c-ported-od-squircle`) and
  `R2-09` (`r2-03a-c-eight-bay-capsule-pod`).
- Approved synthesis: `V2 — Balanced Ported D-Pod`.
- R2-09's generated seven-port artifact was corrected to exactly eight
  deterministic ports before approval.

V2 combines R2-03's D-shaped Dock counter and attached-port rhythm with R2-09's
broader, calmer mantle.

## Canonical vector sources

- Current-color master: `assets/logo/octadock-symbol-master.svg`
- Dedicated small-size masters:
  `assets/icons/octadock-symbol-optical-16.svg` and
  `assets/icons/octadock-symbol-optical-20.svg`
- Geometry contract: 512 × 512 viewBox, one mantle, one horizontal D-shaped
  negative-space Dock, exactly eight named ports, and four mirrored animation
  pairs.

The master and flat color variants are deterministic SVG. The teal-to-cyan file
is a display-only derivative and is not the static master.

## Wordmark and static exports

- Wordmark source: Segoe UI Semibold, converted to outline paths.
- Lockup SVGs contain no live text, external font, raster image, or remote
  dependency.
- PNG icons are transparent RGBA exports.
- The Windows ICO embeds 16, 24, 32, 48, 64, 128, and 256 px frames, with the
  16/20 px family derived from dedicated optical geometry.
- App runtime assets under `src/Octadock.App/Resources/Icons/` are exact copies
  of the approved exports in this package.

## Motion exports

- Logo motion is deterministic HTML/SVG driven by `motion/motion-engine.js` and
  `motion/demo.html`.
- `Dock & Resolve`, `Port Handshake`, `Signal to Dock`, and
  `Quiet Current` preserve the exact geometry and four mirrored port pairs.
- The transparent WebM is a compositing deliverable.
- ElevenLabs Image & Video supplied one optional 9:16 atmosphere plate using the
  provider, model, prompt, and settings in `data/elevenlabs-generation.json`.
- `motion/exports/elevenlabs-signal-to-dock-composite.mp4` places the exact V2
  lockup over that generated atmosphere. No image or video model redraws the
  symbol, wordmark, port count, or Dock counter.

## Preservation source

The approved package was recovered without modification from the pre-triage
portfolio preservation archive dated 2026-07-16. Canonical SVG, optical masters,
PNG/ICO exports, lockups, palette, and motion exports were copied byte-for-byte
before product integration.

## Brand-system evidence

The palette, typography, product framing, and voice guidance synthesize:

- `docs/PRODUCT-STRATEGY-2026-07.md`
- `docs/design/LANDING-CONTEXT-CORE-2026-07-11.md`
- `docs/design/HOMEPAGE-CONCEPT-2026-07-06.md`
- `src/Octadock.App/Resources/Themes/Dark.xaml`
- `src/Octadock.App/Resources/Themes/Light.xaml`
- `src/Octadock.App/Resources/Themes/Shared.xaml`
- `web/styles.css`

AI was used for exploration and a separable atmosphere plate. The production
identity itself is deterministic vector geometry.

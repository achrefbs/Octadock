# Octadock Visual Identity Specification

Version 1.0 · 2026-07-15
Status: V2 geometry approved and deployed; trademark clearance pending.

## Identity idea

Octadock is represented by one engineered, front-facing symbol: **eight explicit inputs docking into one calm context surface**. The horizontal D-shaped counter is the Dock; the broad mantle is the workspace; the eight ports are the product's capture-to-context inputs.

The symbol is abstract—not a mascot. Never add eyes, a face, suckers, biological tentacles, underwater styling, or character behavior.

## Master logo

- Direction: **V2 — Balanced Ported D-Pod**.
- Construction: 512 × 512 viewBox.
- Mantle: 1.5:1 proportion.
- Counter: 2.6:1 horizontal D shape.
- Topology: two lateral ports and six lower ports.
- Motion structure: eight named wrappers grouped into four mirrored pairs.
- Static construction: one color, no stroke, gradient, shadow, bevel, or filter.

### Clear space

Let **X** equal one port width in the 512-unit master: 32 units. Keep at least **2X** clear around the symbol or complete lockup.

### Minimum size

- Master symbol: 24 px digital or 8 mm print.
- Use the dedicated optical masters at 16 px and 20 px.
- Horizontal lockup: 120 px minimum digital width.

## Official color applications

| Application | Foreground | Background | Use |
|---|---:|---:|---|
| Obsidian on light | `#0C1220` | White/Frost | Primary |
| Frost on dark | `#F2F6FC` | `#070B14` or `#0C1220` | Primary reversed |
| Signal Teal on dark | `#2DD4BF` | `#070B14` or `#0C1220` | Digital accent |
| Deep Teal on light | `#0F766E` | White/Frost | Accessible accent |

Signal Teal has excellent contrast on Obsidian but weak contrast on white. Use Deep Teal or Obsidian on light surfaces.

### Core palette

- Canvas `#070B14`
- Obsidian `#0C1220`
- Raised Obsidian `#141E32`
- Border `#25334E`
- Frost `#F2F6FC`
- Mist `#A7B6CF`
- Signal Teal `#2DD4BF`
- Teal Bright `#5EEAD4`
- Signal Cyan `#38BDF8`
- Deep Teal `#0F766E`
- Cloud Violet `#A78BFA` — cloud/Pro only

The teal-to-cyan gradient is environmental and kinetic. Use it for product actions, light trails, or motion—not inside the static master mark.

## Typography

- Display and typeset wordmark: Segoe UI Variable Display / Segoe UI Semibold, weight 600.
- Product and body: Segoe UI Variable / Segoe UI.
- Technical labels: Cascadia Mono / Cascadia Code / Consolas.
- No external font download is required.

The outlined **Octadock** lockup is approved for the beta identity. A future custom logotype may supersede it without changing the symbol.

## Motion system

The rule is **dock, don't swim**. Ports move as mirrored pairs. The Dock counter never blinks, stretches, or morphs.

### Dock & Resolve · 1.2 seconds

Primary hero/ident. The mantle arrives, four port pairs dock outside-in, teal resolves to Frost/Obsidian, then the wordmark enters. Play once.

### Port Handshake · 0.6 seconds

UI success response. A short inner-to-outer signal moves through the paired ports. At 24 px or smaller, animate only whole-mark scale and color.

### Signal to Dock · 6 seconds

Social/video reveal. Eight exact vector signals converge, ports dock, the mantle reveals, and the wordmark resolves. AI may generate only the background atmosphere; the mark and typography are always deterministic overlays.

#### ElevenLabs atmosphere study

The included `motion/exports/elevenlabs-atmosphere-plate.mp4` is a 9:16, six-second concept plate generated in ElevenLabs Image & Video with Gemini Omni Flash. It deliberately ends on an empty central docking field. Use it behind the exact SVG motion; do not extract generated lines as logo geometry. The full prompt and generation settings are recorded in `data/elevenlabs-generation.json`.

The downloaded study is a 480 × 854 preview. Regenerate or retrieve a full-resolution master before paid media delivery, then composite the logo and outlined wordmark from this package.

`motion/exports/elevenlabs-signal-to-dock-composite.mp4` demonstrates that production split. The background is generated; the corrected stacked white lockup is uniformly scaled and alpha-faded without redraw, crop, warp, or aspect-ratio change. A restrained teal underglow starts at 3.20 seconds, the exact lockup enters from 3.50–4.18 seconds, and the resolved identity holds through 6.00 seconds.

### Quiet Current · 5.6 seconds

Landing-page decoration. The logo does not move. A separate halo breathes and low-opacity port color passes once, followed by long stillness. Never use it as a status indicator.

### Reduced motion

Show the complete lockup immediately. At most, allow a 100 ms whole-lockup opacity fade.

## Misuse

Do not:

- change the number, order, or spacing of the eight ports;
- stretch, rotate, outline, bevel, or shadow the mark;
- make violet a core logo color;
- place Signal Teal on white when contrast matters;
- animate the ports individually like waving tentacles;
- loop hero or UI-success motion like a loader;
- ask an image/video model to redraw the final logo or typeset the wordmark.

## Voice and message

- Promise: **Your screen, ready to use.**
- Category: **A local-first capture-to-context workspace for Windows.**
- Voice: calm, direct, precise, privacy-aware, builder-focused.
- Lead with outcome, not AI or a feature inventory.
- Explain privacy through mechanics; say local-first, never fully offline.

## Deployment boundary

This package locks the identity direction but does not deploy it. After final QA, update the desktop ICO/window icons, the separate 17 px Dock glyph, twelve website marks, and six favicons together. Run trademark clearance before public launch.

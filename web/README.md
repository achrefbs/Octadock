# Octadock website (`web/`)

The Octadock website: an immersive underwater-descent landing page ("The
Descent"), a pricing page, and the legal surfaces (privacy, refunds, EULA,
terms). Static files only. **No build step, no framework, no CDN, and no
external runtime requests.** The environment remains the page's hand-built
WebGL2 renderer; the seven-resident V11 aquarium renders into a transparent target on the same
WebGL2 context and is composited before atmosphere, bloom, and the shared color
grade. Both font families and the renderer are self-hosted, so
the site works air-gapped and passes a strict CSP, exactly like the app. The
octopus is an original hand-built parametric model (no purchased or extracted
mesh), rebuilt reference-first against an owner-authorized reference; the
deterministic build scripts live alongside the Blender tooling.

## Files

| File | Purpose |
| --- | --- |
| `index.html` | The landing page. A scroll-driven underwater camera journey: surface → glass octopus → a sunken workstation whose screen the camera dives into (live product demo in DOM) → the violet "cloud is opt-in" thermocline → the seabed close ($49) → the records deck (egress table, CLI, download block). |
| `journey.css` | Landing styles: Clash Display + Switzer, one void black (`--void: #05080f`), journey beats, dock/shelf recreation, records deck. |
| `assets/journey.js` | The hand-built WebGL2 environment and scroll/camera authority. It publishes the exact camera, composition, and fog state consumed by the aquarium layer, then composites the residents before its atmospheric post-process. Scroll never steers an animal. |
| `assets/octopus-v10/` | The production V11 aquarium runtime and reusable asset: one watertight Blender-built body/arm mesh, eight exactly spaced arms, a 130-bone hydrostat rig, eyes/suckers/siphon details, variable-added-mass propulsion, torque turning, and distributed arm inertia. Seven independent skeleton clones share the immutable geometry. The directory name is retained as a stable legacy module path. |
| `assets/landing.js` | Classic-script enhancements + fallbacks: scroll reveals, the interactive dock demo, and the static-water fallback when WebGL is unavailable. |
| `assets/models/octopus-fable-web.glb` | Legacy model retained only for direct `file://` fallback previews, where browser ESM loading is not portable. Hosted pages do not render it. |
| `assets/models/octopus-fable-data.js` | Byte-identical base64 copy used only by that direct-file fallback. |
| `assets/models/octopus-fable-production.blend` | Editable Blender source: 219k-face master mesh, 118-bone rig, weights, materials (wet-skin SSS with dorsal/oral zoning), and six authored actions (the two turn clips are kept in the blend but stripped from the web GLB). |
| `assets/models/octopus-fable-production.json` | Machine-readable mesh, rig, animation, provenance, and export report (sha256 of the shipped GLB). |
| `tools/blender/build_octopus2.py` → `render_six2.py` | The deterministic v2 pipeline: `build_octopus2.py` (parametric master + arm/eye dump), `rig_octopus2.py` (root + 5-bone body chain + 8×14 arm chains, auto weights + corrective smooth), `anim_octopus2.py` (reference-grammar clips), `material_pass2.py` (Cycles skin/eye materials), `export_glb2.py` (web decimate + GLB), `render_six2.py` (six-view inspection renders). |
| `tools/blender/CC0-OCTOPUS-PROVENANCE.md` | Historical record of the retired CC0-derived model lane (no CC0 geometry ships in the current model). |
| `vendor/fonts/` | Self-hosted Clash Display / Switzer woff2. |
| `vendor/three/` | Locally vendored Three.js r185 runtime and required GLTF utilities, including the upstream license. No CDN is used. |
| `pricing.html` | $49 one-time Local license; Pro as waitlist-only. Uses `styles.css`. |
| `privacy.html`, `refunds.html`, `eula.html`, `terms.html` | Legal surfaces (drafts, pending legal review). Use `styles.css`. |
| `styles.css` | Shared stylesheet for the non-landing pages ("Obsidian Instrument"). |

## Working on the landing page

- Rebuild the octopus with Blender 5.1 or newer, entirely from script (no
  source blend needed): `blender --background --python
  tools/blender/build_octopus2.py -- --octver 104 --octsuckers 1 --octdump
  armdata2.json`, then `rig_octopus2.py` (pass the dump), `anim_octopus2.py`,
  `material_pass2.py`, and `export_glb2.py --octweblod 0.18` for the web GLB.
  Regenerate `octopus-fable-data.js` as base64 of the GLB byte-for-byte, and
  bump the `?v=` params in `index.html` / the GLB fetch in `journey.js`.

- Serve the folder with any static server (`python -m http.server` from `web/`,
  or the repo's preview tooling) for the production path. Hosted pages load the
  V11 rig and local renderer modules. Direct `file://` previews remain supported
  through `model-file-loader.js` and the legacy base64 model fallback.
- `?freeze=<0..1>` renders a single still frame at that journey progress —
  deterministic screenshots, and the exact path `prefers-reduced-motion` takes.
- The `#gl` dataset publishes `v10Release`, `v10Population`, per-agent state,
  positions, speed, partner, pose rate, draw/triangle counts, and timing for
  deterministic browser QA.
- `?lab` is the creature workbench: page chrome hidden, camera orbiting the
  octopus in quiet water. **Drag to rotate, wheel to zoom** (auto-orbit until
  the first drag). Combine with `&freeze&labt=<seconds>` for a deterministic
  still pose.
- Press the backtick key on the page for the live tuner (octopus scale, swim,
  glow, glass, bloom, fog, motes, rays).
- `window.__step(p)` force-renders any journey point; `window.__pause(true)`
  halts the loop. Hidden tabs keep advancing via a 50 ms interval so previews
  and screenshots still work.
- Browsers cache hard during iteration: bump a `?fresh=N` param or hard-reload.
- Journey camera keys are measured from the DOM sections at runtime
  (`buildTrack()` in `journey.js`), so changing section heights retunes the
  camera automatically.

The page deliberately separates animal locomotion from page progress. Seven
fixed-step controllers integrate jet force, anisotropic drag, changing added
mass, bounded rotational torque, a low-frequency spatial current, and
aspect-correct crowd separation. Curved autonomous routes cover the full tank;
critically damped camera progress moves only the authored website composition
and never decides an octopus's position, heading, or animation phase.
Routine mantle-first movement holds a partially bundled V-shaped crown with
small arm-specific muscular relief; only a touch escape may gather the full
crown for a jet. One reciprocal social pair can rendezvous and inspect at a
time, while the remaining residents continue independent routes and idle work.

## Accessibility & fallbacks

- The page must read and convert fully with WebGL unavailable, JS disabled, or
  reduced motion: content is normal DOM in document order, reveals are
  enhance-only, the screen-dive demo section renders statically, and
  `prefers-reduced-motion` collapses the journey to stacked beats with a single
  still frame (no scroll choreography).
- Skip link, focus-visible rings, semantic landmarks/headings, labelled table,
  keyboard-operable demo. Wide tables scroll inside their own container.

## How to host (per the 0-to-100 plan, WS2)

1. **Site → Cloudflare Pages.** Point a Pages project at this `web/` directory
   (framework preset: "None"; no build command; output directory: `web`).
2. **Domain.** Attach `octadock.com` (and `www`) to the Pages project. DNS is at
   Namecheap; replace the parking records with the Pages target once the host is
   chosen. Do **not** touch the FastMail MX/DKIM/DMARC/SPF records.
3. **Download build → R2 + CDN.** Host the signed installer on R2 behind the CDN;
   CDN analytics are the canonical Downloads metric.
4. **Checkout.** Buy buttons point at `#checkout-pending` until the live Stripe
   Checkout link for `octadock_local_beta_usd_49` is verified end-to-end.

## Founder-gated — replace the placeholders before launch

| Placeholder in the pages | Replace with | Gate |
| --- | --- | --- |
| `href="#download-pending"` (index) | The R2/CDN URL of the signed installer | Signed build must exist and pass clean-VM verification first. |
| `<SHA-256 PENDING …>` (index) | The published SHA-256 of the exact shipped installer | Must match the CI artifact hash byte-for-byte. |
| `href="#checkout-pending"` (pricing) | Live Stripe Checkout / Payment Link | Legal URLs, tax posture, webhook delivery, license-email delivery all verified first. |
| `href="#waitlist-pending"` (pricing) | The Pro waitlist form endpoint | Needs the waitlist endpoint from WS3. |
| Legal copy in the four legal pages | Lawyer-reviewed text | Keep marked **DRAFT — pending legal review** until sign-off. |

The Stripe catalog, DNS, and email facts live in
`docs/ops/COMMERCIAL_INFRA_SETUP_2026-07-06.md`. Email is **FastMail** on
`mail.octadock.com`.

## Honesty constraints — future edits MUST preserve these

The copy is deliberately, verifiably honest and maps to real code behavior and
the project's locked decisions. Do not let edits regress any of the following.
The first six are enforceable by grep: these strings must never appear in any
**shipped page** (`web/**/*.html`, `*.css`, `assets/*.js`).

- **Never** claim "fully offline" or "local AI". Dictation needs a one-time model
  download; "explain"/"summarize" shell out to the user's cloud AI CLI.
- **Never** mention the dropped AI-session features (schema migration 6 removed
  their tables). Do not resurrect them.
- **Never** market screen recording as capturing audio. Recording is **video
  only**; audio is not implemented yet.
- **Pro is waitlist-only.** Never a buy button, never a price, never "buyable".
  Violet is reserved for cloud/Pro; teal is for everything local — on the
  landing page the octopus itself turns violet only inside the thermocline
  ("cloud is opt-in") beat.
- **Context honesty:** the desktop app has a local Context Stack first slice, but
  AI/MCP/redaction Context is still "in development." It ships to Local at no
  extra cost when ready. Never sell the future capability in a checkout bullet.
- **The egress table must stay complete and accurate.** Every outbound call the
  app can make is listed (one-time Hugging Face model download; license
  activation/entitlement refresh; opt-in OpenAI / ElevenLabs; the user's own AI
  CLI for explain/summarize). Nothing transmits captures, history, or clipboard.
- **Pricing must match reality:** $49 beta ($59 at 1.0), one-time, 3 devices,
  12 months of updates, includes 1.0, keeps working after updates end, optional
  $19/yr renewal. No permanent free tier. No first-party accounts.
- **Every legal/draft page stays marked** "DRAFT — pending legal review" until a
  lawyer reviews it.
- **The dock/shelf demo stays a truthful recreation** of the shipped UI
  (`docs/design/ui/*.png`) — never invent product surfaces that don't exist.

### Honesty grep (run before shipping any edit)

From the repo root, this must return **no matches** (the README itself
documents the banned phrases, so it is excluded):

```bash
grep -rniE "fully offline|local AI|AI Discovery|AI Sessions|record(ing)? (with )?audio|audio recording" web/ --exclude=README.md
```

## Design notes

- **Named lane:** "The Descent" — depth as the privacy metaphor. The deeper you
  scroll, the more local it gets; the depth HUD (SPECIMEN / OCTOPODA · DEPTH ·
  SIGNAL / LOCAL) makes the instrument read literal.
- **One void black.** `--void: #05080f` is read from CSS by the WebGL layer and
  used for clear color, fog, and env box — the historical "five near-blacks"
  seam bug cannot recur unless someone forks the token.
- **Type:** Clash Display (display) + Switzer (body), self-hosted. Mono is
  Cascadia/Consolas for HUD, hashes, and the CLI — real technical content only.
- **Motion:** native scroll only (no smooth-scroll library), UI feedback ≤170 ms,
  the creature animates continuously at low amplitude (organism, not UI), and
  the download CTA has no hover transform.
- **Palette contract:** teal = local/interactive; violet strictly = cloud/Pro
  (the thermocline beat and the Pro bullet). The seabed grid is the only other
  saturated teal surface.

# Octadock website

Static HTML, CSS, and JavaScript for Octadock, a free, local Windows capture application. The current white-and-blue product landing page comes from the newer `gh-pages` branch, commit `452f211` (14 September 2026), and includes app screenshots and an interactive capture illustration. It replaces the older aquarium page previously present on `main`.

The website has no production build step, framework, account service, checkout, analytics, or CDN dependency. Its fonts, renderer, and other runtime assets are local files. Development dependency installation and browser installation require network access on a fresh machine; serving the installed site does not require external assets.

## Current status

- `index.html` evolves the product landing from `gh-pages` with an edge-to-edge blue interactive hero, a pausable Shelf-to-message animation and the full Product/Trust/Support footer. Preserve this design when publishing; do not substitute the older aquarium or parked `claude/landing-product-palette` work. The creator is Prime Ashref, with GitHub and `@primeashref` links.
- `concepts/index.html` compares **Air**, **Studio**, and **Nocturne**, each with a different presentation of the original animated octopus. These alternatives do not replace the existing site until one is selected and implemented.
- `download.html`, `release.html`, `privacy.html`, `license.html`, `terms.html` and `unsubscribe.html` share the landing design. Download opens an optional marketing signup; an unchecked consent box and a no-email download are provided. The Railway server stores consent in a private persistent volume. Campaign sending is not configured. The static preview cannot save subscriptions.
- The desktop preview is an unsigned Windows x64 alpha, available as a per-user Setup.exe or portable ZIP. The production website is hosted from `achrefbs/turing-league`, `sites/octadock`, in the existing Railway project. Release binaries are staged separately from Git; verify checksums and the deployed landing-to-download flow after each deployment. See [installer and site operations](../docs/WEB-AND-INSTALLER.md).

Current product behavior is defined by [the local software contract](../docs/LOCAL-SOFTWARE.md) and [project state](../docs/PROJECT-STATE.md).

## Preview locally

From `web/`:

```powershell
$env:PORT = '4174'
node tests/static-server.mjs
```

Open `http://127.0.0.1:4174/` for the current site, or `http://127.0.0.1:4174/concepts/` for the design chooser. The server binds to localhost. Prefer HTTP previews over direct `file://` loading, which can limit browser modules and clipboard features.

The concept chooser records a preference only in this browser's local storage. It does not submit anything to a server or communicate the choice to Codex. Its product controls manipulate an illustrated scene; they do not operate the native app or capture the user's screen. See [the concept README](concepts/README.md) for interactions and promotion steps.

## Files

| Path | Purpose |
| --- | --- |
| `index.html`, `product.css`, `assets/js/`, `assets/app/` | Product landing, shared design, interactive demo, optional signup/unsubscribe flow and real app screenshots. |
| `journey.css` | Retained legacy aquarium presentation styles. |
| `assets/journey.js` | WebGL environment, page composition, camera state, and still-frame support. |
| `assets/octopus-v10/` | Aquarium runtime, rigged models, motion controllers, and effects. The legacy directory name remains a stable module path. |
| `assets/landing.js` | Page enhancements, copy feedback, and fallback behavior. |
| `assets/model-file-loader.js`, `assets/models/` | Model-loading support, editable Blender sources, GLBs, reports, and legacy direct-file assets. |
| `assets/brand/` | Product marks, icons, and favicons. |
| `vendor/fonts/`, `vendor/three/` | Self-hosted typefaces and Three.js files, including the renderer's upstream license. |
| `download.html`, `release.html`, `privacy.html`, `license.html`, `terms.html`, `unsubscribe.html` | Local supporting pages using `product.css`. |
| `concepts/` | Three independent design previews, shared interactions, a reusable animated octopus stage, static posters, landscape illustration, and review screenshots. |
| `tests/`, `package.json`, `package-lock.json` | Development-only static and browser validation. |

## Validation

Prerequisites are Node 20 or newer and the existing locked development dependencies. From the repository root, the reproducible Windows entry point is:

```powershell
./build/validate-web.ps1
```

It installs the locked npm dependencies and pinned Chromium, then runs website validation. If that Chromium revision is already present, `-SkipBrowserInstall` skips its installation. The canonical repository Release gate also invokes website validation.

For an existing development environment, from `web/`:

```powershell
npm run validate
```

The suite checks the main page and supporting pages for local links/assets, semantic structure, JavaScript-disabled fallback, reduced motion, narrow layouts, automated WCAG A/AA violations, browser errors, local-only runtime requests, and the sample clipboard action. Concept-specific tests additionally cover capture modes, history selection, markup, sample downloads, release dialogs, local preference persistence, and 390 × 844 / 320 × 480 viewports.

For a focused concept iteration:

```powershell
node --check concepts/concepts.js
npx playwright test tests/browser/design-options.spec.mjs
```

Browser failure artifacts are written to `test-results/`; the configured HTML report is written to `playwright-report/`. Tests establish the conditions they exercise, not compatibility with every browser, assistive technology, or graphics device.

## Aquarium maintenance

The existing aquarium presentation remains available for comparison. Its DOM content is independent of decorative rendering and must remain readable with WebGL unavailable, JavaScript disabled, or reduced motion enabled. Keep keyboard focus visible and retain the native cursor for product controls and accessibility modes.

- `?freeze=<0..1>` renders a still at a specified journey position. This is useful for repeatable visual inspection and uses the reduced-motion path.
- `window.__step(p)` in `assets/journey.js` renders a chosen journey point for local QA.
- The renderer and its required modules are vendored. Preserve local asset references when moving or restyling the page.
- Editable model assets and export reports live under `assets/models/`. Repository tools under `tools/blender/` include `build_octopus2.py`, `rig_octopus2.py`, `anim_octopus2.py`, `material_pass2.py`, `export_glb2.py`, and `render_six2.py`.
- Before changing model assets, inspect those tools and their provenance/reference notes. Regenerate affected reports and fallback data with the model export, and update cache-busting versions where the page references changed assets.

## Product copy and release changes

Describe what the current application actually does:

- Features are free, with no account, activation, subscription, trial, or device-count gate.
- Capture, OCR, history, settings, annotations, and export processing run on the PC. There is no desktop telemetry, cloud speech provider, automatic updater, remote AI launcher, or automatic model download.
- Dictation is optional and requires a supported model imported from local disk. Read-aloud uses installed Windows voices.
- Local export means inspecting, copying, or saving a packet and attachments. It does not send them to an AI service.
- The application is Windows x64 software. Do not imply macOS or Linux support. Scrolling capture and audio recording remain Beta, subject to the documented hardware acceptance limits.
- The project uses the MIT license. Preserve upstream component and model licenses; do not invent testimonials, usage counts, performance claims, or public-release availability.

There is no checkout. The website's optional signup requires the separately deployed Railway API; the desktop remains entirely local. Update download links only when the exact release artifact, version, maturity, and checksum are available. Keep local validation, repository publication and hosted-site deployment as distinct recorded actions.

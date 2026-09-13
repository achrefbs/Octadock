# Landing-page design previews

These are three alternative directions for Octadock's landing page. They do not replace the current website at `/index.html` and do not change the native Windows application.

- `index.html`: comparison chooser, with rendered previews of all three options.
- `air.html`: quiet white space, a floating teal octopus and a separate product demonstration.
- `studio.html`: warm editorial typography, the octopus framed as a playful exhibit, and lime details.
- `nocturne.html`: a dark, atmospheric setting for the octopus, followed by a workspace with a left capture rail.

## Run locally

From the repository's `web` directory, use the existing static server:

```powershell
$env:PORT = '4174'
node tests/static-server.mjs
```

Open `http://127.0.0.1:4174/concepts/`. The comparison bar on each page switches between directions. The existing landing page remains available at `http://127.0.0.1:4174/index.html`.

## What the preview does

The product scene is an **interactive illustration**, not an embedded or remotely controlled version of Octadock. It does not capture your screen or access your capture library.

- Capture modes change the selection frame. The capture button adds a sample thumbnail to the illustration.
- Selecting a thumbnail changes the image framing. The pen toggles a sample annotation.
- Copy writes the stated sample text to the browser clipboard after you click it. Save downloads the included landscape SVG.
- Windows preview buttons open development-release information. They do not advertise a published binary or start an installer.
- Choosing a design stores its name only in this browser's `localStorage`, under `octadock-landing-preference`. **Nothing is submitted to a server or sent to the Codex task.** Share the choice in the task to request implementation. The chooser states this in its visible status text.

All fonts, icons, images, scripts, and styles are local files. The concepts use no CDN, analytics, external API, account service, or network model download. Local links lead to the repository's existing license and privacy pages. The landscape SVG is original illustrative artwork; the images in `previews/` are browser screenshots of these pages.

## The octopus

All three directions retain the existing mascot and logo. `mascot.js` loads the authored solid `octopus-rigged-v12.glb` from the existing asset directory and reuses its `HydrostatMotion` rig. Each page supplies a local Three.js import map. The model geometry is unchanged; the neutral exported body receives the familiar teal material and lighting appropriate to its setting.

`mascot.css` gives each direction its own hero composition and keeps the comparison navigation in a top bar. The octopus gently moves and responds to nearby mouse movement. The keyboard-operable motion button pauses or resumes it. Reduced-motion preference starts with a still frame. Rendering stops when the stage leaves the viewport or the page is hidden, and resources are disposed when leaving the page. WebGL failure or context loss displays a static poster; the page and product controls remain usable without animation or JavaScript.

The PNGs in `posters/` are deterministic renders of the same model used for the no-WebGL/no-JavaScript fallback. Keep them in sync with material or camera changes. The full aquarium on the original landing page remains available separately.

## Verify

From `web`, after installing the existing development dependencies:

```powershell
node --check concepts/concepts.js
npx playwright test tests/browser/design-options.spec.mjs tests/browser/mascot.spec.mjs
```

The checks cover capture/history interactions, clipboard contents, the sample download, release dialogs, browser-local preference persistence, network requests, reduced motion, automated accessibility, and narrow layouts. They include 390 × 844 and 320 × 480 viewports. Mascot tests require the local model to render, verify keyboard pause/resume and unchanged pixels while paused, and exercise a no-WebGL fallback. They use software WebGL in an isolated test browser for reproducibility on Windows. Automated accessibility checks are useful coverage, not a substitute for keyboard and assistive-technology review.

## Promote a chosen direction

1. Confirm the user's choice. The local preference button alone does not publish or apply it.
2. Move the selected composition into the main landing page, updating asset paths from `concepts/` to their final locations. Keep the shared scene controls only where they help explain the product.
3. Replace development-release information only when real release URLs and release details are available. Keep Windows support, local model imports, and release maturity explicit.
4. Remove the design comparison navigation from the public product page, update relevant static contracts, and run the full website validation suite.
5. Publish only with authorization. Until then, the alternatives and current website remain separate local previews.

When a concept changes, refresh its corresponding desktop screenshot in `previews/` so the chooser remains accurate. Mobile screenshots provide visual review evidence; they are not loaded by the chooser.

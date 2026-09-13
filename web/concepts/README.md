# Landing-page design previews

These are three alternative directions for Octadock's landing page. They do not replace the current website at `/index.html` and do not change the native Windows application.

- `index.html`: comparison chooser, with rendered previews of all three options.
- `air.html`: quiet white space and a centered product presentation.
- `studio.html`: warm editorial typography, layered captures, and lime details.
- `nocturne.html`: a dark workspace with a split introduction and a left capture rail.

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

## Verify

From `web`, after installing the existing development dependencies:

```powershell
node --check concepts/concepts.js
npx playwright test tests/browser/design-options.spec.mjs
```

The checks cover capture/history interactions, clipboard contents, the sample download, release dialogs, browser-local preference persistence, network requests, reduced motion, automated accessibility, and narrow layouts. They include 390 × 844 and 320 × 480 viewports. Automated accessibility checks are useful coverage, not a substitute for keyboard and assistive-technology review.

## Promote a chosen direction

1. Confirm the user's choice. The local preference button alone does not publish or apply it.
2. Move the selected composition into the main landing page, updating asset paths from `concepts/` to their final locations. Keep the shared scene controls only where they help explain the product.
3. Replace development-release information only when real release URLs and release details are available. Keep Windows support, local model imports, and release maturity explicit.
4. Remove the design comparison navigation from the public product page, update relevant static contracts, and run the full website validation suite.
5. Publish only with authorization. Until then, the alternatives and current website remain separate local previews.

When a concept changes, refresh its corresponding desktop screenshot in `previews/` so the chooser remains accurate. Mobile screenshots provide visual review evidence; they are not loaded by the chooser.

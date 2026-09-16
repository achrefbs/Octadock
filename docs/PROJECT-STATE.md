# Current project state — 16 September 2026

Version: **0.3.0-alpha.2**, Windows x64, unsigned public preview. Maintained branch: `main`.

## Desktop

- Free and local: no trial, license/account gate, activation server, telemetry, automatic updater or remote AI runner.
- Capture, OCR, annotation, read-aloud, dictation with explicitly imported models, clipboard history, Context bundles, redaction, comparison and export run on this PC.
- Dock positioning uses physical pixels and stores a separate normalized anchor per monitor. Utility windows fit the available work area; History adapts to narrow windows.
- The image-only Shelf reveals its action rail on hover/focus. The Library includes live search, filters, floating selection actions and an optional Info drawer.
- The per-user Windows installer and portable ZIP contain the same desktop payload. Installation does not need administrator rights or a separate .NET runtime. Uninstall preserves captures, settings and models.

The desktop payload published for this version is from `737a22b15b7f5453fd87249ee110a36137eaa219`; installer packaging is from `bf1f10a86dfd2fb70132cea37909dca12d5ff185`. Later website/documentation changes do not replace those versioned binaries.

## Website

The current website is [octadock.com](https://octadock.com), served by Railway. Its source is `web/`; `gh-pages` is historical source, not the hosting provider.

The approved design uses a full-width blue interactive hero, a collapsed Dock, a left-side Shelf, and a Shelf-to-message animation. The browser demo is illustrative and only captures the page. Download, release notes, MIT license and privacy pages share the same design and full footer.

Downloads are available without email. Optional marketing consent is unchecked by default. Signups are stored privately by the separate website service; ownership verification and campaign sending are not configured. Support mail is handled through Fastmail at `support@octadock.com`.

The three pages in `web/concepts/` are archived explorations. The chooser does not deploy anything.

## Validation and reproducibility

Run `./build/build.ps1 -Configuration Release` for the canonical Windows gate. See [TESTING.md](TESTING.md) for the latest measured counts, coverage and native acceptance limits, and [CONTRIBUTING.md](CONTRIBUTING.md) for fresh-machine setup. Current npm and NuGet vulnerability queries, including transitive NuGet packages, reported no known vulnerabilities on 16 September 2026.

GitHub Actions runs currently stop before any job starts because GitHub reports an account billing/spending restriction. Local Release results are separate evidence; they must not be described as a passing hosted CI run.

## Source publication

The MIT source, contribution guide and private security-reporting contact are prepared for public contribution. Historical paid-beta and recovery branches are unsupported. The old license service's deliberately committed development key is explained in [SECURITY.md](../SECURITY.md); current builds do not include the service or entitlement verification.

Changing repository visibility remains the owner's action. The existing public source ZIP is a versioned desktop snapshot. Research tooling in `tools/internal/` is source-only, opt-in, outside the desktop solution and excluded from release artifacts; no collected user data belongs in the repository.

## Known limits

- Windows x64 only; builds and installer are unsigned and may trigger SmartScreen.
- Scrolling capture and recording remain Beta. Scrolling is manual and vertical, requires overlapping viewports, and does not support auto-scroll or horizontal stitching.
- The Context window can need reopening after Add to Context; saved items still export correctly.
- Annotation text editing has had session-dependent issues; report a reproducible case with the app version.
- Protected, minimized and special GPU windows may refuse capture. Microphone/device loss, recording audio, speech-model accuracy, long recordings and clean-machine installation need broader hardware acceptance.
- No GIF export, pinning or upload provider is shipped.

These are alpha limitations, not claims of completed hardware certification. Older plans and acceptance documents preserve historical observations and do not override the current [local-software contract](LOCAL-SOFTWARE.md).

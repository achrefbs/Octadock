# Website and Windows installer

The canonical website source is `web/`, promoted from the newer product landing on `gh-pages` at `452f211` and revised with the interactive hero. `gh-pages` is a historical source branch; the site is hosted on Railway, inside `achrefbs/turing-league/sites/octadock`, at `https://octadock.com`. It is not hosted by GitHub Pages.

## Shared website design

All public pages use local Plus Jakarta Sans, `product.css`, the same header/footer and the same download dialog. `assets/js/demo.js` runs the illustrative browser capture demo; it does not capture the user's desktop, record a real video or upload anything. The second section shows a real Shelf screenshot. The master logo retains its approved two lateral ports and six lower ports from `docs/brand/assets/logo/octadock-symbol-master.svg`.

Download opens an optional signup dialog. The marketing checkbox starts unchecked. “Download without email” works without submitting the field. Without JavaScript, normal links lead to the installation page and file. Successful signup and skipped signup lead to the same installer. API failure displays an error and preserves the no-email option.

Optional website signup is separate from desktop software. The server and private persistent list are in the Turing League website directory. The app still has no accounts, telemetry or network client. Campaign sending is not configured. See that site's README for storage, retention, exports and unsubscribe management.

## Installer

Built using [Inno Setup](https://jrsoftware.org/isinfo.php), with the complete, self-contained, reviewed Windows payload. It does not download anything during installation. Installation is per-user and does not request administrator access. The optional desktop shortcut is unchecked; a Start menu entry and normal Windows uninstaller are included.

```powershell
./build/installer.ps1 -ReleaseDirectory <verified-release-directory> -CompilerPath <path-to-ISCC.exe> -OutputDirectory <output-directory>
```

`ReleaseDirectory` must contain `publish/octadock`, `LICENSE` and `release-manifest.json`. The installer and manifest are written separately from the existing portable ZIP. Do not relabel or replace an existing versioned artifact with different desktop code. Publish the installer checksum alongside the portable and source checksums.

The installer writes to `%LOCALAPPDATA%\Programs\Octadock`. The app's captures, settings, history and models stay separately under `%LOCALAPPDATA%\Octadock`. No uninstall directive removes that data. Uninstall removes startup/protocol entries only if they still point to this exact installation; a different portable copy's entries are preserved. There is no automatic updater.

Before publishing a new installer, test clean installation, installation over itself, payload hashes, uninstall registration and removal. Use an isolated directory/account when a normal installed copy already exists. Run the canonical Release gate for a release candidate. The installer remains unsigned until a code-signing certificate is configured; creating an installer does not remove SmartScreen warnings.

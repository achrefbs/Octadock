# Downloads and Windows installer

The public download site is https://octadock.com. Its marketing source, signup server and website tests are maintained in a separate private repository. They are not required to build, test or package the open-source desktop app.

The current unsigned alpha has an installer, a portable ZIP and a versioned app source ZIP. Downloads work without providing an email address. Support is available at support@octadock.com. Publisher signing status is documented in [Code signing](CODE-SIGNING.md).

## Installer

Built using [Inno Setup](https://jrsoftware.org/isinfo.php), with the complete, self-contained, reviewed Windows payload. It does not download anything during installation. Installation is per-user and does not request administrator access. The optional desktop shortcut is unchecked; a Start menu entry and normal Windows uninstaller are included.

```powershell
./build/installer.ps1 -ReleaseDirectory <verified-release-directory> -CompilerPath <path-to-ISCC.exe> -OutputDirectory <output-directory>
```

`ReleaseDirectory` must contain `publish/octadock`, `LICENSE` and `release-manifest.json`. The installer and manifest are written separately from the existing portable ZIP. Do not relabel or replace an existing versioned artifact with different desktop code. Publish the installer checksum alongside the portable and source checksums.

The installer writes to `%LOCALAPPDATA%\Programs\Octadock`. The app's captures, settings, history and models stay separately under `%LOCALAPPDATA%\Octadock`. No uninstall directive removes that data. Uninstall removes startup/protocol entries only if they still point to this exact installation; a different portable copy's entries are preserved. There is no automatic updater.

Before publishing a new installer, test clean installation, installation over itself, payload hashes, uninstall registration and removal. Use an isolated directory/account when a normal installed copy already exists. Run the canonical Release gate for a release candidate. The installer remains unsigned until a code-signing certificate is configured; creating an installer does not remove SmartScreen warnings.

# Documentation guide

Start with the [root README](../README.md) for downloading, running and building Octadock.

## Current guides

| Guide | What it covers |
| --- | --- |
| [Contributing](CONTRIBUTING.md) | Fresh checkout, prerequisites, desktop and website setup, tests and PRs |
| [Local software](LOCAL-SOFTWARE.md) | Current free/local product contract |
| [Project state](PROJECT-STATE.md) | Current release, measured validation and known limits |
| [Testing](TESTING.md) | Automated gates and native Windows acceptance |
| [Website and installer](WEB-AND-INSTALLER.md) | Current site, optional signup and per-user Windows Setup |
| [Code signing](CODE-SIGNING.md) | Signing preparation, verification and pending provider/build setup |
| [Versioning](VERSIONING.md) | Version metadata and release scripts |
| [Security](../SECURITY.md) | Private vulnerability reports and historical test credentials |
| [Changelog](../CHANGELOG.md) | Release history |

## Historical material

Older plans, capability matrices, architecture notes, support runbooks and acceptance records preserve earlier product decisions. Documents marked **Historical document** can describe removed paid licensing, cloud speech and AI integrations. They are not current setup instructions; the root README and local-software contract take precedence.

Use `main` for contributions. `gh-pages` preserves the earlier landing source; the live site is hosted on Railway. Design/recovery branches and the three `web/concepts/` pages remain archives, not alternative release instructions.

`tools/internal/` contains opt-in research source with synthetic regression tests. It is outside the desktop solution and excluded from release artifacts. Reading or building the public app does not run it; generated research data is not part of the repository.

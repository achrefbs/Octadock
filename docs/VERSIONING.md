# Versioning

Octadock uses Semantic Versioning for product builds:

- `MAJOR` changes for incompatible automation, storage, or package behavior.
- `MINOR` changes for new user-facing features.
- `PATCH` changes for compatible bug fixes.
- Pre-release suffixes mark alpha, beta, and release-candidate builds, for
  example `0.2.0-alpha.0`.

The current integrated alpha line is `0.2.0-alpha.0`.

## Source Of Truth

Version metadata is intentionally split by audience:

- `version.json` is the human/tool-readable release metadata.
- `build/version.props` stamps every .NET project through `Directory.Build.props`.
- `CHANGELOG.md` records user-facing changes.

Run this before publishing or opening a release PR:

```powershell
./build/version.ps1 -Check
```

To bump the version:

```powershell
./build/version.ps1 -Version 0.2.1 -Suffix alpha.1
./build/version.ps1 -Version 0.3.0 -Suffix ''
```

The About window and `octadock --version` both read the assembly
informational version produced from this metadata.

## Release Packaging

Use the release script from a Windows machine with the .NET 8 SDK installed:

```powershell
./build/release.ps1
```

The script performs the release gate in this order:

1. Validates `version.json` and `build/version.props` with
   `./build/version.ps1 -Check`.
2. Restores the solution.
3. Builds `Octadock.sln` in `Release`.
4. Runs the full Release test suite and writes TRX files into the versioned
   release folder.
5. Publishes `Octadock.App` and `Octadock.Cli`.
6. Copies release metadata, writes a manifest, creates checksums, and builds a
   zip archive.

Release outputs are staged under:

```text
artifacts/release/<version>/
```

For `0.2.0-alpha.0`, the important files are:

```text
artifacts/release/0.2.0-alpha.0/
  publish/octadock/Octadock.exe
  publish/octadock/cli/octadock.exe
  test-results/
  release-manifest.json
  SHA256SUMS.txt
  Octadock-0.2.0-alpha.0-windows.zip
```

Use `-Strict` when you want the CI analyzer/warnings-as-errors gate
(`ContinuousIntegrationBuild=true`) before packaging. Use `-SkipTests` only
after a separate Release test run, and record that choice in the release notes.
Use `-NoArchive` when you only need the staged publish folder for inspection.

The package is currently a self-contained single-file `win-x64` publish plus zip
for the app and CLI. Code signing, installer/MSIX generation, SmartScreen
reputation, clean-VM verification, and the update host are separate follow-up
release steps.

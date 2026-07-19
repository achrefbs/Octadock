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

## Experimental visual checkpoints

Large website/renderer iterations use annotated Git tags in addition to the
product SemVer source of truth. Tags such as `octopus-roam-v3`,
`octopus-aquarium-v1`, and `octopus-aquarium-v2` identify recoverable visual
checkpoints; they are not a second product version and do not replace
`version.json` or release tags.

Create a checkpoint only from a tested commit and push the commit before the
tag. This keeps animation experiments reversible without pretending that every
visual iteration is a new Octadock product release.

## Release Packaging

Use the release script from a Windows machine with the .NET 8 SDK installed:

```powershell
./build/release.ps1 -Strict
```

The script performs the release gate in this order:

1. Validates `version.json` and `build/version.props` with
   `./build/version.ps1 -Check`.
2. Validates any requested release tag and clean-source preconditions before it
   writes artifacts.
3. Restores the solution.
4. Builds `Octadock.sln` in `Release`.
5. Runs the full Release test suite and writes TRX files into the versioned
   release folder.
6. Publishes self-contained single-file `Octadock.App` and `Octadock.Cli`
   outputs for `win-x64`.
7. Runs `build/public-artifact-boundary.ps1` against the exact publish tree and
   records `public-artifact-boundary.json` evidence.
8. Copies release metadata, writes a manifest, builds the zip archive, writes
   `SHA256SUMS.txt` for the uploaded ZIP/evidence/test deliverables, and verifies
   every checksum entry before succeeding.

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
  public-artifact-boundary.json
  SHA256SUMS.txt
  Octadock-0.2.0-alpha.0-windows.zip
```

Use `-Strict` for anything intended to become a release artifact; it enables the
CI analyzer/warnings-as-errors gate (`ContinuousIntegrationBuild=true`). Use
`-SkipTests` only after a separate Release test run, and record that choice in
the release notes. Use `-NoArchive` only when you need the staged publish folder
for inspection.

For a fast, read-only preflight that does not restore, build, publish, or write
under `artifacts/`, run:

```powershell
$releaseTag = 'v0.2.0-alpha.0' # must exactly match version.json
./build/release.ps1 -ValidateOnly -Strict -ExpectedTag $releaseTag
```

Add `-RequireCleanWorkingTree` after the release commit is created. The tagged
workflow also adds `-RequireTagAtHead`, which proves the exact tag exists and
resolves to the checked-out commit.

## Product release tag path

Product release tags use exactly `v<SemVer>`, including any prerelease suffix:
`v0.2.0-alpha.0`, `v0.3.0-beta.1`, or `v1.0.0`. Experimental visual checkpoint
tags are never accepted as product release tags because they do not match
`version.json`.

Use this checklist for a release package:

1. Update `version.json`, `build/version.props`, `CHANGELOG.md`, and the optional
   `releaseDate`; merge that release commit to `main` through the normal review
   path and wait for the `CI` workflow to pass once A-03/A-04 are unblocked.
2. Start from a fresh, clean `main` checkout and run:

   ```powershell
   git pull --ff-only origin main
   ./build/version.ps1 -Check
   $releaseVersion = ((Get-Content ./version.json -Raw | ConvertFrom-Json).versionPrefix)
   $releaseSuffix = ((Get-Content ./version.json -Raw | ConvertFrom-Json).versionSuffix)
   if ($releaseSuffix) { $releaseVersion = "$releaseVersion-$releaseSuffix" }
   $releaseTag = "v$releaseVersion"
   ./build/release.ps1 -ValidateOnly -Strict -ExpectedTag $releaseTag -RequireCleanWorkingTree
   ./build/release.ps1 -Strict -ExpectedTag $releaseTag -RequireCleanWorkingTree
   ```

3. Inspect the ZIP, manifest, boundary evidence, and verified checksum file under
   `artifacts/release/<version>/`. Do not proceed if tests were skipped or any
   manifest gate is not `passed`.
4. Create an annotated tag on that already-pushed commit, verify it, then push
   only the explicit tag ref:

   ```powershell
   git tag -a $releaseTag -m "Octadock $releaseVersion"
   git rev-list -n 1 $releaseTag
   git rev-parse HEAD
   git push origin "refs/tags/${releaseTag}:refs/tags/${releaseTag}"
   ```

5. `.github/workflows/release.yml` checks out that exact tag, repeats the strict
   build, verifies tag/HEAD and source cleanliness, and uploads the ZIP plus its
   manifest, SHA-256 file, public-boundary evidence, and TRX results as one
   immutable-source Actions artifact. `workflow_dispatch` may rebuild an
   existing tag; it cannot package a branch as a release tag.
6. Download the Actions artifact and independently compare its ZIP hash with
   `SHA256SUMS.txt` before handing it to signing or clean-VM validation.

This Gate A workflow deliberately does not create a GitHub Release, publish a
download URL, sign binaries, or update production. Code signing and installer
publication remain Gate D-02 work and require the founder-owned signing identity.

The package is currently a self-contained single-file `win-x64` publish plus zip
for the app and CLI. Code signing, installer/MSIX generation, SmartScreen
reputation, clean-VM verification, and the update host are separate follow-up
release steps.

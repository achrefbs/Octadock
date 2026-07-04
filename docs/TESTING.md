# Octadock Testing

Octadock splits testing into two layers: **automated unit tests** for the
cross-platform libraries (which run on any OS and in CI), and a **manual test
matrix** for the Windows-specific capture, overlay, DPI, and recording behavior
that cannot be meaningfully unit-tested off Windows.

## Automated tests

The automated suites live under `tests/`. Cross-platform tests run on any OS;
Windows-targeted projects require the Windows SDK targeting pack.

- `Octadock.Core.Tests` — command parsing/formatting, geometry, settings, filename
  templating, `.octadock` project serialization, history retention, hotkey model,
  and the IPC contract.
- `Octadock.Data.Tests` — the SQLite repositories for `captures`, `actions`,
  `pins`, `clipboard_clips`, and `settings`, exercised against
  in-memory/temporary databases.
- `Octadock.Cli.Tests` — CLI option parsing and headless CLI behavior.
- `Octadock.Platform.Windows.Tests` — testable Windows-platform capture and
  recording helpers.
- `Octadock.App.Tests` — App-layer helpers that can run headless, including
  preview clipboard formatting and launch-safety policy.

Static inventory on 2026-07-03 found 315 xUnit `[Fact]`/`[Theory]` declarations.
The last local integration run reported 412 executed tests passing;
the higher number comes from theories expanding into multiple cases.

The stack is **xUnit + FluentAssertions + NSubstitute + coverlet**. Tests must be
fast, deterministic, and isolated: use fakes and in-memory SQLite instead of real
Windows APIs or the real filesystem where possible.

### Running unit tests

```powershell
# Full suite on Windows.
dotnet test Octadock.sln

# A single project.
dotnet test tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj
dotnet test tests/Octadock.Data.Tests/Octadock.Data.Tests.csproj
dotnet test tests/Octadock.Cli.Tests/Octadock.Cli.Tests.csproj
dotnet test tests/Octadock.Platform.Windows.Tests/Octadock.Platform.Windows.Tests.csproj
dotnet test tests/Octadock.App.Tests/Octadock.App.Tests.csproj

# With coverage (Cobertura + results under artifacts/test-results/).
./build/test.ps1
```

On Linux/macOS, run the cross-platform projects without the Windows SDK:

```bash
dotnet test tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj -c Release
dotnet test tests/Octadock.Data.Tests/Octadock.Data.Tests.csproj -c Release
```

### What automated tests cover

Per the architecture ADR's testing strategy: unit tests for command parsing,
settings, history retention, project serialization, and action routing;
render/export tests for annotation objects; and integration tests for the capture
abstractions using fixtures and mocked capture providers. These give the core loop
strong, OS-independent coverage.

## Manual test matrix

The following must be verified on real Windows hardware because they depend on
capture APIs, DPI, monitor topology, the taskbar, and audio devices. This matrix
is condensed from [`specs/implementation-backlog.md`](specs/implementation-backlog.md).

### Windows versions

- Windows 10 22H2.
- Windows 11 current stable.

### Displays

- Single monitor at 100% DPI.
- Single monitor at 150% / 200% DPI.
- Multi-monitor with **mixed DPI**.
- Portrait monitor.
- Taskbar positioned bottom, top, left, right, and auto-hide.

### System states

- Light mode, dark mode, and high-contrast mode.
- RDP session.
- Low disk space.
- Protected/secure windows or windows that return a black capture.
- App running elevated vs. as a normal user.

### Workflows

- Screenshot Edge/Chrome, File Explorer, Terminal, Visual Studio / VS Code,
  Teams/Discord/Slack, and a fullscreen app.
- Drag a shelf item into File Explorer, a browser upload field,
  Teams/Discord/Slack, email, and a docs editor.
- Capture while the shelf already contains previous captures (stacking).
- Annotate an 8K screenshot with ~100 vector objects and confirm the export
  matches the visible canvas.
- Pin an image and use lock-through over another app.
- OCR Latin text and mixed-language text.
- Record with no microphone, the default microphone, and an external audio
  interface.

### Recording-specific

Recording paths need dedicated manual checks: microphone present/absent, system
audio, long recordings, cancellation mid-recording, disk-full during recording,
and crash recovery.

### Overlay-inclusion (critical)

Confirm that Octadock's own shelf and overlay windows **do not appear** in
captures on the Windows versions/APIs where `SetWindowDisplayAffinity`
(`WDA_EXCLUDEFROMCAPTURE`) is supported (Windows 10 2004+), and that the hide-UI
fallback works where it is not.

## Acceptance checkpoints

Each implementation milestone has explicit acceptance criteria in
[`specs/implementation-backlog.md`](specs/implementation-backlog.md). The core-loop
definition of done is: **on a Windows PC, press the shortcut, drag a region,
release, see the shelf bottom-left, click copy, and paste the image into another
app.** Automation acceptance is: `octadock://capture-area?action=copy` works from
the Run dialog/browser, and `octadock.exe capture-area --action copy` works from
PowerShell, with invalid commands failing safely (non-zero exit code).

## Continuous integration

CI mirrors the OS split (see [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)):

- **`windows-build`** (`windows-latest`) restores, builds the **full** solution in
  Release with warnings-as-errors, runs the headless test projects, and uploads
  build artifacts.
- **`linux-tests`** (`ubuntu-latest`) builds and tests **only** `Octadock.Core` and
  `Octadock.Data` (and their test projects) with `ContinuousIntegrationBuild=true`
  and collects coverage.
- **`lint`** runs `dotnet format --verify-no-changes` on the cross-platform
  projects.

The Windows CI artifact is named `windows-build` and contains the app/CLI publish
outputs plus TRX test results from `artifacts/publish/**` and
`artifacts/test-results/**`. The Linux artifact is named `linux-test-results` and
contains cross-platform TRX and coverage output from `artifacts/test-results/**`.

For a release candidate package, run:

```powershell
./build/release.ps1
```

That script repeats the Release validation locally and stages a versioned bundle
under `artifacts/release/<version>/`, including publish outputs, TRX results,
`release-manifest.json`, `SHA256SUMS.txt`, and the release zip.
Pass `-Strict` to make that local package run use CI's
`ContinuousIntegrationBuild=true` analyzer gate.

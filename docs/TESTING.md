# Octadock Testing

Last audited: 2026-07-19

Octadock splits testing into four layers:

- automated tests for platform-agnostic and headless code;
- license-service tests in a separate service solution;
- locked Node and Chromium validation for the static website;
- manual Windows verification for capture, overlays, DPI, recording, visual
  design, and device-dependent behavior.

## Local Evidence

Latest Phase 1 candidate evidence, observed on 2026-07-19 from `main` at
`219b487` plus the recorded dirty candidate source state:

```powershell
.\build\build.ps1 -Configuration Release
# Build passed; desktop tests passed: 1,219/1,219
# Core 729; App 329; Data 88; Platform.Windows 50; CLI 23

dotnet test .\services\license-service\tests\Octadock.LicenseService.Tests\Octadock.LicenseService.Tests.csproj -c Release
# Passed: 75/75

dotnet test .\tests\internal\Octadock.WorkflowIntelligence.Internal.Tests\Octadock.WorkflowIntelligence.Internal.Tests.csproj -c Release
# Passed: 27/27
```

The same canonical command also passed 15 website syntax checks, 6 static web
contracts, 21 Chromium tests, self-contained single-file App/CLI publish,
version synchronization, copy-honesty, and the public-artifact-boundary gate.
The older clean-main and recovery snapshots remain historical evidence in the
roadmap; they are not current counts.

Do not reuse older test-count snapshots as the current count. They were earlier
states of the suite.

Neither state is remotely green yet: GitHub Actions currently fails at workflow
startup before jobs begin. Local success is evidence for the code baseline and
recovery candidate, not a substitute for repairing CI.

## Automated Tests

The desktop automated suites live under `tests/`:

- `Octadock.Core.Tests` - command parsing/formatting, geometry, settings,
  filename templating, `.octadock` project serialization, Context export,
  licensing/trial/update primitives, history retention, hotkey model, file
  safety, speech/session logic, and IPC contracts.
- `Octadock.Data.Tests` - SQLite repositories for `captures`, `actions`, `pins`,
  `settings`, `clipboard_clips`, and Context packages against temporary
  databases.
- `Octadock.Cli.Tests` - CLI option parsing and headless CLI behavior.
- `Octadock.Platform.Windows.Tests` - testable Windows-platform helpers such as
  capture/recording support code and provider availability logic.
- `Octadock.App.Tests` - App-layer helpers that can run headless, including
  launch safety, gates, preview formatting, shelf/context helpers, and view
  model/service logic.

The commercial service is intentionally isolated:

- `services/license-service/tests/Octadock.LicenseService.Tests` - Stripe
  webhook verification/dedupe, license issuance/revocation, activation,
  entitlement signing, admin health, reconciliation, and alerts.

The stack is xUnit, FluentAssertions, NSubstitute, and coverlet. Tests must be
fast, deterministic, and isolated: use fakes and temporary folders/databases
instead of real Windows APIs or real user data where possible.

## Running Tests

```powershell
# Full desktop suite on Windows.
dotnet test Octadock.sln

# Current fast local verification after a build.
dotnet test .\Octadock.sln -c Debug --no-build

# License service.
dotnet test .\services\license-service\Octadock.LicenseService.sln

# Individual desktop projects.
dotnet test tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj
dotnet test tests/Octadock.Data.Tests/Octadock.Data.Tests.csproj
dotnet test tests/Octadock.Cli.Tests/Octadock.Cli.Tests.csproj
dotnet test tests/Octadock.Platform.Windows.Tests/Octadock.Platform.Windows.Tests.csproj
dotnet test tests/Octadock.App.Tests/Octadock.App.Tests.csproj

# Coverage for the desktop solution.
./build/test.ps1

# Website: clean npm install, pinned Chromium, static contracts, axe, and smoke.
./build/validate-web.ps1
```

The website command is the fresh-checkout gate. Its committed package lock pins
the Node dependencies and Playwright browser revision; the web-validation CI
job runs the same npm run validate suite on Linux after installing Chromium's
system dependencies. It covers JavaScript syntax, every local link/asset/import
edge, static and JavaScript-disabled fallback, automated WCAG A/AA checks,
reduced motion, narrow viewports, browser runtime errors, local-only requests,
and the automation copy interaction.

Phase 1 C-04 through C-07 acceptance tooling is separate from the fast unit
suite because several rows require explicit desktop, microphone, assistive
technology, or long-run interaction:

    # Validate the harnesses under the Windows PowerShell 5.1 floor.
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\acceptance\AcceptanceTooling.Tests.ps1

    # Deterministic dictation coverage (74 rows); hardware rows remain pending in JSON.
    .\tools\acceptance\Invoke-DictationAcceptance.ps1 -Configuration Release

    # Versioned STT corpus benchmark: 23-case manifest, locally SAPI-generated
    # audio (never committed), cold/warm start, streaming latency/integrity,
    # and batch WER by category against the live local Parakeet engine (the
    # first run downloads the ~640 MB model). Missing-voice cases are reported
    # pending, never fabricated. Results live under artifacts/acceptance/stt/.
    .\tools\acceptance\stt\Invoke-SttBenchmark.ps1

    # Static WPF token/accessibility scan (current result: clean, 0/0 findings).
    .\tools\acceptance\Test-WpfStaticAcceptance.ps1 -NoBaseline

    # Inspect the non-interactive 50 capture / 10 scroll / 1 dictation soak plan.
    .\tools\acceptance\Invoke-SoakAcceptance.ps1 -PlanOnly

Process measurement and live/manual commands, schemas, isolation requirements,
and completion boundaries are documented in
[Phase 1 C-04 through C-07 acceptance](acceptance/PHASE-1-C04-C07.md). These
tools are intentionally not part of every build: they must never start capture,
microphone, model download, or a long-running soak without named opt-in.

Latest local acceptance-tool evidence on 2026-07-19: 30 harness assertions
passed; deterministic dictation passed 59/59; the no-baseline WPF scan reported
0 new and 0 known findings. Hardware/manual rows remain pending and are not
implied by those counts.

On Linux/macOS, run only the cross-platform desktop projects:

```bash
dotnet test tests/Octadock.Core.Tests/Octadock.Core.Tests.csproj -c Release
dotnet test tests/Octadock.Data.Tests/Octadock.Data.Tests.csproj -c Release
```

## What Automated Tests Cover

Automated tests cover parser behavior, settings, command routing, retention,
project serialization, Context export invariants, license/trial gates, safe file
write/revision behavior, repository migrations, headless view models, and many
failure paths.

Automated tests do **not** prove:

- visual quality or concept-board fidelity;
- WPF layout at real monitor sizes;
- mixed-DPI overlay placement;
- actual desktop capture exclusion on every Windows build;
- real microphone quality/device privacy behavior;
- real scrolling capture on browsers/docs/apps;
- clean-VM install/signing/SmartScreen behavior;
- Stripe/FastMail/DNS/legal production readiness.

## Manual Test Matrix

These must be verified on real Windows hardware because they depend on capture
APIs, DPI, monitor topology, taskbar behavior, windows focus, and devices.

### Windows Versions

- Windows 10 22H2.
- Windows 11 current stable.

### Displays

- Single monitor at 100 percent DPI.
- Single monitor at 150 percent / 200 percent DPI.
- Multi-monitor with mixed DPI.
- Portrait monitor.
- Taskbar bottom, top, left, right, and auto-hide.

### System States

- Light mode, dark mode, and high-contrast mode.
- RDP session.
- Low disk space.
- Protected/secure windows or windows that return a black capture.
- App running elevated vs. as a normal user.

### Core Workflows

- Screenshot Edge/Chrome, File Explorer, Terminal, Visual Studio / VS Code,
  Teams/Discord/Slack, and a fullscreen app.
- Drag a shelf item into File Explorer, a browser upload field,
  Teams/Discord/Slack, email, and a docs editor.
- Capture while the shelf already contains previous captures.
- Annotate an 8K screenshot with roughly 100 vector objects and confirm the
  export matches the visible canvas.
- Annotate a capture from Shelf and from History; edit, undo/redo, copy,
  export, and save; verify thumbnails refresh and an `Annotated` action is
  recorded. Confirm missing/corrupt sources fail visibly.
- Build a Context package, add files/captures, navigate packages, export folder
  and zip, then verify the manifest has no absolute path leaks.
- OCR Latin text and mixed-language text.
- Dictate short and long text with Parakeet, model-not-downloaded state, privacy
  failure state, hold-to-talk, and auto-stop on silence.
- Read aloud clipboard, file, OCR region, and `--explain`.

### Recording-Specific

Current build expectation: recording is **Beta** — MP4 video with optional
microphone (WASAPI) and system/app (loopback) AAC audio tracks. Both audio
tracks are explicit Settings opt-ins and default off; copy must not promise
audio beyond that until hardware/audio-device acceptance is recorded.

Manual checks:

- active-monitor recording;
- selected-area recording from tray/Dock Capture menu/CLI;
- microphone-track recording with the opt-in enabled (narration audible);
- system/app-audio-track recording with the opt-in enabled;
- microphone device loss during recording (truthful failure, no false success);
- stop/cancel behavior;
- long recording;
- disk-full during recording;
- app crash/shutdown during recording (remnant sweep on next start);
- failure cleanup of incomplete MP4 outputs (no History row on failure).

### Scrolling Capture

Test both ordinary and adversarial pages:

- top-to-bottom and bottom-to-top manual scroll attempts;
- sticky headers/footers;
- sparse dark pages;
- long browser pages;
- document viewers;
- pages where the scroll wheel moves a nested element.

The current feature is manual vertical only; horizontal and auto-scroll must be
rejected honestly.

### Reviewed AI handoff engine

Automated coverage must include deterministic packet/manifest hashes, every
outbound text field under secret redaction, prompt-injection framing, text-only
source snapshots, reference changes, mapped/UNC/reparse rejection, bounded and
aggregate attachment I/O, `SHA256SUMS`, grouped visual verification, cancellation,
temporary leases/retention, provider-only routing, and stale review/result guards.

Before a release, validate the installed CLI versions without sending content:

```powershell
codex -a never exec --ephemeral --ignore-user-config --ignore-rules --disable shell_tool --disable unified_exec --disable shell_snapshot --help
claude --permission-mode dontAsk --tools Read,Glob --allowedTools "Read(./**),Glob(./**)" --safe-mode --no-session-persistence --version
```

The privacy canary is a separate, explicit network test: create a harmless
sentinel outside the packet and ask each reviewed CLI profile to read it. The
test passes only when the provider cannot return the sentinel. Never use a real
credential as the canary, and do not run this paid/remote test silently.

### Visual Acceptance

UI work cannot be accepted by unit tests alone. For Dock, shelf, Context Stack,
settings, dialogs (including the shared ConfirmationDialog), and history
changes, capture before/after screenshots at fixed sizes and compare against
the concept board. Check:

- compactness;
- no overlapping text/buttons;
- hover states;
- icon consistency;
- glass/transparent surface consistency;
- no hidden primary actions;
- mobile/narrow-equivalent WPF sizing where applicable.

## Continuous Integration

CI mirrors the OS split in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml):

- `windows-build` (`windows-latest`) restores, builds the full solution in
  Release with warnings-as-errors, runs headless test projects, and uploads build
  artifacts.
- `linux-tests` (`ubuntu-latest`) builds and tests only `Octadock.Core` and
  `Octadock.Data` plus their tests with `ContinuousIntegrationBuild=true`.
- `lint` runs `dotnet format --verify-no-changes` on cross-platform projects.

For a release candidate package:

```powershell
./build/release.ps1 -Strict
```

The script validates version metadata, builds/tests Release, publishes
self-contained single-file win-x64 app and CLI outputs, stages a versioned
release folder, executes the public-artifact-boundary gate against the publish
tree, writes machine-readable boundary evidence and `release-manifest.json`,
creates the release ZIP, then writes and verifies `SHA256SUMS.txt`.
The checksum file covers the ZIP, manifest, public-boundary evidence, and any
uploaded TRX results so it remains usable after downloading the Actions artifact.

Validate the release contract without building or writing artifacts:

```powershell
./build/release.ps1 -ValidateOnly -Strict -ExpectedTag v0.2.0-alpha.0
```

Tagged builds run through [`.github/workflows/release.yml`](../.github/workflows/release.yml).
The workflow accepts only an existing exact `v<version>` tag at HEAD, requires a
clean checkout, and uploads the package plus manifest, checksums,
public-boundary evidence, and TRX results. See
[`docs/VERSIONING.md`](VERSIONING.md) for the safe tag checklist. The workflow
does not sign or publicly publish the artifact.

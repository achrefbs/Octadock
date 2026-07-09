# Octadock Testing

Last audited: 2026-07-09

Octadock splits testing into three layers:

- automated tests for platform-agnostic and headless code;
- license-service tests in a separate service solution;
- manual Windows verification for capture, overlays, DPI, recording, visual
  design, and device-dependent behavior.

## Latest Local Evidence

Observed on 2026-07-09:

```powershell
dotnet test .\Octadock.sln -c Debug
# Passed: 773/773

dotnet test .\services\license-service\Octadock.LicenseService.sln -c Debug --no-build
# Passed: 65/65
```

Do not reuse older test-count snapshots as the current count. They were earlier
states of the suite.

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
```

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
- Open an image from shelf, Explorer, CLI, Context, and file preview paths; draw
  quick-pen ink; save same/new; verify thumbnails refresh.
- Pin/unpin image topmost, lock position, change opacity, close/reopen, and
  gather after monitor layout changes.
- Build a Context package, add files/captures, navigate packages, export folder
  and zip, then verify the manifest has no absolute path leaks.
- OCR Latin text and mixed-language text.
- Dictate short and long text with Parakeet, model-not-downloaded state, privacy
  failure state, hold-to-talk, and auto-stop on silence.
- Read aloud clipboard, file, OCR region, and `--explain`.

### Recording-Specific

Current build expectation: recording is **video-only**. Microphone/system-audio
settings are disabled/normalized off and audio should not be promised in the UI.

Manual checks:

- active-monitor recording;
- selected-area recording from tray/HUD/CLI;
- stop/cancel behavior;
- long recording;
- disk-full during recording;
- app crash/shutdown during recording;
- failure cleanup of incomplete MP4 outputs.

Future audio tests are deferred until audio encoding exists.

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

### Visual Acceptance

UI work cannot be accepted by unit tests alone. For Dock, shelf, Context Stack,
image surface, preview, settings, and history changes, capture before/after
screenshots at fixed sizes and compare against the concept board. Check:

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
./build/release.ps1
```

The script validates version metadata, builds/tests Release, publishes
self-contained single-file win-x64 app and CLI outputs, stages a versioned
release folder, writes `release-manifest.json`, `SHA256SUMS.txt`, and creates the
release zip. Pass `-Strict` to use the CI analyzer gate locally.

# Octadock

Octadock is a Windows screenshot, file-preview, voice, and screen-recording
utility built around a **Capture Shelf**: every capture becomes a small,
temporary object docked at the bottom-left of your active monitor, ready to copy,
open, annotate, OCR, add to Context, or drag straight into another app without
hunting through folders.

> **Original work.** Octadock is an independent, clean-room implementation by
> Surus Labs. It is **not affiliated with, endorsed by, or a clone of** any other
> product. It ships its own UI, interactions, naming, icons, file formats, and
> `octadock://` protocol, and reuses no third-party branding, assets, or
> proprietary file/endpoint formats. It targets Windows 10/11 and is built on
> public Windows APIs and permissively licensed open-source dependencies.

## Features

The current alpha build includes:

- **Capture** - area (crosshair, live dimensions, magnifier), window,
  fullscreen, all-monitors, previous-area repeat, self-timer, and manual vertical
  scrolling capture.
- **Permanent Dock and Capture Shelf** - a small glass Dock for common actions,
  plus bottom-left shelf cards for images and videos. Image shelf cards stay
  image-first at rest, show hover actions, and open the image surface on click;
  shelf flows support copy, save/save as, discard, drag-out, reveal in Explorer,
  and restore recently closed.
- **Clipboard and drag/drop** - copy writes bitmap data to the Windows clipboard
  and drag-out offers file payloads for File Explorer, browsers, chat apps, and
  editors.
- **Annotation editor** - crop, select/move, arrow, rectangle, ellipse, line,
  text, highlighter, blur, pixelate, counter, freehand, undo/redo, flatten export
  to PNG/JPEG, drag-out, and editable `.octadock` project packages.
- **Image surface / pins** - images open in a floating surface with move/resize,
  opacity, pin/unpin topmost, position lock, quick pen annotations, copy/save,
  advanced annotation, source reveal/open, add to Context, keyboard nudging, and
  persisted pin state.
- **Local history** - SQLite-backed capture/action/pin/settings storage with
  filters, soft-delete/restore, retention cleanup, and thumbnail cache.
- **Context Stack** - build a local context package from captures and files,
  navigate packages, open items through Octadock, and export either a normal
  folder or a zip with a relative-path manifest. This is the first Context slice,
  not the future AI/MCP/redaction system.
- **OCR** - local `Windows.Media.Ocr` recognition on a selected region or file,
  with compact, lines, and layout output modes copied to the clipboard.
- **Read aloud** - `octadock read`, tray, Dock, or `Ctrl+Shift+0` speaks text,
  clipboard content, files, image OCR, or selected screen regions **verbatim**
  with the built-in Windows voices — on-device, audio starts after the
  first sentence, and a playback pill offers pause/stop. `read --explain` (or
  the `explain`/`summarize` verbs) first runs the text through your Codex/Claude
  CLI — the selected text is sent to that AI provider; ElevenLabs voices are
  opt-in via settings and billed by your provider.
- **Screen recording** - active-monitor and command-selected/fixed-region MP4
  recording, plus tray/HUD selected-area recording, with countdown, timer pill,
  stop control, history entry, shelf video card, and a "Video saved"
  notification that reveals the file. Failed starts/stops clean incomplete MP4s;
  audio is still planned.
- **File preview** - `octadock open --filepath <path>` plus Dock/Explorer entry
  points. Raster images open in the image surface; CSV/TSV, JSON, log, Markdown,
  broad text/code/config files, and unsupported file-info cards open in Octadock
  preview surfaces with confirmation before external open.
- **Dictation** - dock, `Ctrl+Shift+2`, or `octadock dictation` toggles local
  speech-to-text after the model is available. NVIDIA Parakeet TDT 0.6B v3 (via
  sherpa-onnx) is the default engine - 20-30x realtime on CPU with native
  punctuation/casing across 25 European languages - with local Whisper covering
  99 languages and explicit `OCTADOCK_OPENAI_API_KEY` opt-in OpenAI cloud
  transcription. The pill shows the transcript live while you
  speak (embedded Silero VAD; finished sentences freeze, the tail refines), so
  stopping inserts text near-instantly; optional auto-stop on silence and a
  discard button. Activation is configurable: toggle (default), hold-to-talk
  (push the shortcut, speak, release to insert), or both. WASAPI mic capture,
  one-time resumable model download, code-term replacements, and
  paste-at-cursor insertion.
- **Automation** - `octadock://` protocol URLs, `octadock.exe`, global hotkeys,
  file-association registration, `open-context`, activation deep links, and
  per-user IPC forwarding to the running tray instance.
- **Licensing/trial spine** - local 14-day trial, signed entitlement outside the
  SQLite database, Account & Billing activation UI, `octadock://activate`, and
  service-seam gates for paid features after expiry.

Privacy-first by default: no network requests during capture, annotation, OCR,
recording, local history, or local Context exports unless you explicitly enable a
feature that needs the network. Dictation model downloads, license activation,
optional OpenAI/ElevenLabs, and `read --explain` through your AI CLI are the
notable network-capable paths. Crash reports are local and opt-in.

For the full as-built inventory, limitations, and live roadmap, see
[docs/PROJECT-STATE.md](docs/PROJECT-STATE.md), [docs/ROADMAP.md](docs/ROADMAP.md),
and the planning specs under [docs/specs/](docs/specs/).

Current integrated alpha version: `0.2.0-alpha.0`. Version metadata is managed
through [docs/VERSIONING.md](docs/VERSIONING.md), `version.json`, and
`build/version.props`.

## Solution architecture

Octadock is a multi-project .NET 8 solution. Cross-cutting logic lives in
platform-agnostic libraries so it can be unit-tested anywhere, while all
Windows/WPF integration is isolated in Windows-only projects.

- **Octadock.Core** (`net8.0`) - the platform-agnostic heart: domain models,
  geometry, settings, the automation command parser/formatter, the `.octadock`
  project serializer, retention policy, the line-based IPC contract
  (`IpcProtocol`), Context/export models, licensing/update primitives, trial
  logic, and the abstractions every other layer implements. No Windows, WPF, or
  WinRT dependencies, so it builds and tests on any OS.
- **Octadock.Data** (`net8.0`) - SQLite-backed persistence for `captures`,
  `actions`, `pins`, `settings`, clipboard clips, and Context packages. Depends
  on Core and builds/tests on Linux/CI.
- **Octadock.Platform.Windows** (`net8.0-windows`) - the Win32/WinRT
  implementations of the Core abstractions: screen capture, monitor/DPI services,
  global hotkeys, window enumeration/capture exclusion, OCR, recording, audio
  capture, STT/TTS providers, file association registration, protocol/startup,
  machine identity, and single-instance behavior. Depends on Core; Windows-only.
- **Octadock.App** (`net8.0-windows`, WPF) - the desktop application shell: tray
  icon, first-run wizard, settings, selection overlays, Capture Shelf, Context
  Stack, image surface, preview surfaces, annotation editor, history, clipboard,
  voice/read UI, licensing UI, and IPC pipe server. Depends on Core, Data, and
  Platform.Windows.
- **Octadock.Cli** (`net8.0-windows`) - `octadock.exe`, a thin, dependency-light
  forwarder. It validates a command locally with Core's `CommandParser`, then
  hands the raw arguments to the running tray instance over the per-user named
  pipe (starting `Octadock.exe` if nothing is listening) and prints the reply.
  Depends only on Core (plus `System.CommandLine`). See
  [docs/AUTOMATION.md](docs/AUTOMATION.md).
- **services/license-service** - isolated ASP.NET Core service for Stripe
  webhooks, license issuance, activation, signed entitlements, trust anchor, and
  launch-health/admin status. It has its own solution and tests.
- **tests/** - desktop test projects cover Core, Data, CLI, Platform.Windows,
  and App headless behavior. The license service has a separate test suite.

Dependency direction (leaf to root): `Core` has no project dependencies; `Data`,
`Platform.Windows`, and `Cli` depend on `Core`; `App` depends on `Core`, `Data`,
and `Platform.Windows`. A dependency diagram is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Build and run

### Prerequisites

- **.NET 8 SDK** (see `global.json`; `8.0.x`).
- **Windows 10 version 2004 (build 19041) or Windows 11** to build and run the
  WPF/Windows projects (`Octadock.App`, `Octadock.Platform.Windows`,
  `Octadock.Cli`). The `net8.0-windows` target framework requires the Windows SDK
  targeting pack, which is Windows-only.
- **Visual Studio 2022 (17.10+)** with the *.NET desktop development* workload is
  recommended; the [Directory.Build.props](Directory.Build.props) and
  [Directory.Packages.props](Directory.Packages.props) files enable analyzers,
  nullable reference types, and Central Package Management.

### Build the full solution (Windows)

Open `Octadock.sln` in Visual Studio 2022 and build, or from a Developer prompt:

```powershell
dotnet restore Octadock.sln
dotnet build Octadock.sln -c Release
```

Or use the helper script, which restores, builds Release, and runs the tests:

```powershell
./build/build.ps1                 # Debug by default
./build/build.ps1 -Configuration Release
./build/build.ps1 -Configuration Release -Pack   # also 'dotnet pack'
```

Validate the product version metadata:

```powershell
./build/version.ps1 -Check
```

Create a versioned Release package:

```powershell
./build/release.ps1
```

The release script validates version metadata, builds and tests Release, publishes
the tray app plus CLI, and stages outputs under `artifacts/release/<version>/`.
See [docs/VERSIONING.md](docs/VERSIONING.md) for the release checklist and
artifact layout.

### Run

Start the tray app from Visual Studio (set `Octadock.App` as the startup project)
or:

```powershell
dotnet run --project src/Octadock.App/Octadock.App.csproj
```

Then drive it from the CLI (see [docs/AUTOMATION.md](docs/AUTOMATION.md)):

```powershell
octadock capture-area --action copy
octadock ocr --area 100,120,800,600 --mode lines
octadock read --clipboard
octadock open --filepath "C:\path\to\data.csv"
octadock --help
```

## Testing

Run the automated test suite:

```powershell
dotnet test Octadock.sln
```

Or the coverage script (writes results and a Cobertura report under
`artifacts/test-results/`):

```powershell
./build/test.ps1
```

See [docs/TESTING.md](docs/TESTING.md) for the full test matrix and the list of
scenarios that must be verified manually on Windows hardware.

## Project status

Octadock is under active alpha development. The original capture/shelf/history/
annotation/pin loop is built. OCR, scrolling capture, recording, file preview,
and dictation now exist as partial slices with known limitations.

Current priorities are:

- enforce one compact design system across Dock, shelf, Context Stack, settings,
  preview, history, and menus, with screenshot-based acceptance;
- harden the image surface, Context Stack, and shelf flows that the user touches
  every minute;
- expand file preview toward PDF, Office, archives, design files, and safer
  non-image annotation/writeback;
- finish Context as a real work surface: item controls, notes/reorder, redaction,
  export preview, source integrations, AI, and MCP;
- keep scrolling capture, mixed-DPI, recording, STT, and read-aloud under real
  Windows device verification;
- finish distribution/commercial external gates: installer/signing, update host,
  DNS/download URL, legal review, Stripe production wiring, and support ops.

See [docs/PROJECT-STATE.md](docs/PROJECT-STATE.md) for the audited current
state, [docs/ROADMAP.md](docs/ROADMAP.md) for the active implementation plan,
[docs/specs/octadock-build-plan.md](docs/specs/octadock-build-plan.md) for the
new build plan, and
[docs/specs/read-aloud-screen-discovery.md](docs/specs/read-aloud-screen-discovery.md)
for the read-aloud/discovery direction.

## Sandbox / CI note

The cross-platform projects (`Octadock.Core`, `Octadock.Data`) and their tests
build and test on **any OS**, including Linux CI runners and sandboxes. The
Windows projects (`Octadock.App`, `Octadock.Platform.Windows`, `Octadock.Cli`)
target `net8.0-windows` and need the Windows SDK targeting pack. See
[docs/TESTING.md](docs/TESTING.md) for the current automated/manual matrix.

## License

MIT. See [LICENSE](LICENSE). Copyright (c) Surus Labs.

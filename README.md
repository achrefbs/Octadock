# Octadock

Free, local capture tools for Windows 10 (2004+) and Windows 11. MIT licensed.

Capture a region, window, display, or scrolling viewport. Keep recent shots on a small shelf, annotate them, extract text, dictate, read aloud, and collect evidence into local Context bundles.

## Free and local

All features are available without an account, activation, trial, device limit, or subscription. The desktop app has no telemetry, licensing server, automatic updater, cloud speech provider, remote AI process launcher, or automatic model downloader. Communication between the CLI and desktop app uses a local named pipe.

Captures, history, settings, annotations and models live in `%LOCALAPPDATA%\Octadock`. Exports go to folders you select. Other software may sync those folders; Octadock itself does not upload them.

## Run

Extract the portable Windows x64 release and run `Octadock.exe`. A .NET runtime installation is not needed for the self-contained package. Builds in this branch are unsigned development releases.

Default shortcuts: **Ctrl+Shift+4** selects an area, **Ctrl+Shift+3** captures the full screen, and **Ctrl+Shift+2** toggles dictation. Configure shortcuts in Settings. Scrolling capture is manual: select the scrollable content, scroll with overlapping viewports, then click Finish. You can pause without ending the session.

The dock remembers a separate position for each monitor. History rearranges its controls on narrow screens, and utility windows fit within the available work area.

## Local voice models

Read-aloud uses installed Windows voices. Dictation requires a model already on disk. In **Settings → Voice → Advanced**, choose **Import**:

- **Parakeet:** choose a folder containing `encoder.int8.onnx`, `decoder.int8.onnx`, `joiner.int8.onnx`, and `tokens.txt` for the supported TDT 0.6B v3 int8 model. Exact sizes and SHA-256 hashes are pinned in `ParakeetModelStore.cs`. Imports are verified before installation.
- **Whisper:** select a GGML `.bin` model matching the chosen variant. The import checks its header and copies it into the local models directory. This is a format check, not a cryptographic authenticity guarantee.

Octadock does not download model files. Existing installed models remain usable. Legacy cloud provider preferences migrate to a local engine. Speech models retain their upstream licenses; see About and the license accompanying each model.

## Local export

The former AI handoff opens **Local export**. Attach captures, notes, clipboard content and files, inspect the Markdown, then copy it or save a bundle. Local OCR, text-secret redaction and visual comparisons remain available. Octadock does not run Codex, Claude or a remote image generation service.

## Build and verify

Prerequisites: .NET 8 SDK; Node 20+ for website validation; Windows for WPF and native capture. Dependency restore requires network access on a fresh machine; the built application does not.

```powershell
./build/build.ps1 -Configuration Release
```

The gate checks version metadata, the local-only source boundary, website syntax/assets/accessibility/browser behavior, desktop and internal regression tests, self-contained publishing, and the public artifact boundary.

For a focused iteration:

```powershell
dotnet test Octadock.sln -c Release
```

Optional native acceptance, with synthetic data and an isolated profile:

```powershell
dotnet build tools/acceptance/DesktopProbe/DesktopProbe.csproj -c Release
./tools/acceptance/DesktopProbe/bin/Release/net8.0-windows10.0.19041.0/DesktopProbe.exe ./artifacts/desktop-probe
```

This opens test windows briefly on connected displays and saves renderings and JSON evidence. It does not register integrations or read your capture library.

## Structure

- `src/Octadock.Core`: settings, geometry, commands, storage contracts and local packet models.
- `src/Octadock.Data`: SQLite persistence.
- `src/Octadock.Platform.Windows`: capture, OCR, audio, local speech, monitor and OS integration.
- `src/Octadock.App`: WPF dock, shelf, history, settings, annotations and local exports.
- `src/Octadock.Cli`: local command client.
- `tests`: regression tests. `tools/acceptance`: optional hardware probes.
- `web`: static website with local assets. `tools/internal`: research tooling excluded from desktop release artifacts.

## Release status

This is **0.3.0-alpha.1**, the free/local transition. See [current status](docs/PROJECT-STATE.md), [testing](docs/TESTING.md), and [contributing](docs/CONTRIBUTING.md). Historical plans are superseded by [the local-software contract](docs/LOCAL-SOFTWARE.md).

The application is Windows software; this change does not add macOS or Linux desktop support. Scrolling capture and audio recording remain Beta. Synthetic tests do not establish compatibility with every GPU, protected window, microphone or monitor topology.

## License

[MIT](LICENSE), copyright Surus Labs. Third-party components retain their own licenses.

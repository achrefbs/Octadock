# Octadock Automation

Octadock exposes two automation surfaces that share one command vocabulary:

- **Protocol URLs** — `octadock://<command>?<query>` for the Run dialog, browser
  bookmarks, desktop shortcuts, and any app that opens URIs.
- **CLI** — `octadock.exe <command> [options]` for PowerShell, AutoHotkey, Flow
  Launcher, PowerToys Run, and scripts.

Both are handled by the running Octadock tray instance. The CLI is a thin
forwarder: it validates the command locally with the same parser the app uses,
connects to the app over a per-user named pipe, launches `Octadock.exe` if it is
not already running, then prints the app's reply and returns its exit code.

> **Coordinates.** Regions accept **physical pixels by default**. Pass
> `units=dip` (URL) or `--units dip` (CLI) for WPF device-independent pixels. The
> origin is the virtual-screen top-left. `monitor` identifies a monitor by current
> index or stored monitor id.

## CLI usage

```powershell
octadock.exe <command> [options]
octadock.exe "octadock://<command>?<query>"   # a full URI also works as one arg
```

Options may be written as `--key value` or `--key=value`. A bare `--flag` is
treated as `true`. Region shorthand `--area x,y,width,height` expands to
`--x --y --width --height`.

### Global switches

These are consumed by `octadock.exe` itself (everything else is the command and
its options):

| Switch | Effect |
| --- | --- |
| `--json` | Emit a single line of machine-readable JSON for the result or error instead of plain text. |
| `--timeout <seconds>` | Global timeout for the connect + reply round-trip (default `15`). |
| `--no-launch` | Fail with a runtime error instead of starting Octadock if it is not already running. |
| `--version` | Print the CLI version and exit. |
| `-h`, `--help`, `-?` | Show top-level help, or help for a specific command (`octadock <command> --help`). |

### Exit codes

`octadock.exe` returns a code scripts can branch on:

| Code | Meaning |
| --- | --- |
| `0` | The command completed successfully. Capture commands created a durable artifact. |
| `1` | Runtime/IPC error — pipe timeout, broken pipe, Octadock could not be started, or Octadock reported a failure. |
| `2` | Parse error — unknown command, malformed option, or invalid coordinates. |

The parse code (`2`) is produced locally by the parser before any pipe connection,
so obviously-bad input fails fast.

### JSON output

With `--json`, results and errors are emitted as one JSON line:

```jsonc
// capture success (captureId is the exact durable History/SQLite identity)
{"success":true,"exitCode":0,"message":"Capture completed.","captureId":"8f8faec0-e637-4be8-852e-7643414b6674"}

// non-capture success
{"success":true,"exitCode":0,"message":"Octadock is shutting down."}

// failure (parse error)
{"success":false,"exitCode":2,"message":"Unknown command 'foo'. Expected one of: ...","error":"parse_error"}

// failure (runtime/IPC error)
{"success":false,"exitCode":1,"message":"Timed out waiting for Octadock to respond.","error":"timeout"}
```

`success` and `exitCode` are always present. `message` is included when the app
provides one; `error` (a stable slug such as `parse_error`, `runtime_error`, or
`timeout`) is present only on failures. Successful capture commands also include
`captureId`; cancellation before an artifact exists returns exit code `1` and no
identifier. The field is additive to IPC protocol v1, so older replies remain
readable.

### CLI aliases

For convenience the CLI accepts a few friendly aliases that resolve to canonical
verbs: `area`→`capture-area`, `window`→`capture-window`,
`fullscreen`→`capture-fullscreen`, `all`→`all-in-one`, `ocr`/`text`→`capture-text`,
`capture-ocr`→`capture-text`, `record`/`recording`→`record-screen`,
`dictate`/`speech`→`dictation`,
`allinone`→`all-in-one`, `annotate`/`edit`→`open-annotate`,
`history`→`open-history`, `shelf`→`add-shelf-item`, `settings`→`open-settings`,
`clipboard`/`clipboard-history`/`clips`→`open-clipboard-history`,
`context`/`context-stack`→`open-context`, `text-tools`/`transforms`→`open-text-tools`,
`read-aloud`→`read`, `ask-ai`/`ai-actions`/`explain`/`summarize`→`ai`, and
`exit`/`shutdown`→`quit`. Legacy AI aliases map into the nearest outcome-first
workflow while remaining editable.

## Command reference

Except where explicitly marked CLI-only, every command below works on both
surfaces. The canonical verb is the token after `octadock://` and the first CLI
argument. Common parameters
(`x`, `y`, `width`, `height`, `monitor`, `units`, `action`, `filename`, `preset`,
`silent`) are described in the [API spec](specs/api-and-data-spec.md).

The `action` parameter chooses what happens to the capture and is one of:
`shelf` (default), `copy`, `save`, `annotate`, `upload`, `pin`, `discard`.

### all-in-one

Opens the compact capture HUD (area / window / fullscreen / scrolling / OCR /
record). The implementation honors `mode` and preloads supplied region or
width/height values into the HUD's remembered fixed-size target.

Parameters: `x`, `y`, `width`, `height`, `monitor`, `units`,
`mode` (`area` | `window` | `fullscreen` | `scrolling` | `ocr` | `record`).

```powershell
octadock all-in-one
octadock all-in-one --mode ocr
```
```text
octadock://all-in-one
octadock://all-in-one?mode=area&width=1200&height=800
```

### capture-area

Captures a rectangular region. Without coordinates it opens the area selection
overlay; with a complete region it captures immediately after hiding Octadock's
own UI. Runs the requested `action`, defaulting to the shelf.

Parameters: `x`, `y`, `width`, `height`, `monitor`, `units`, `action`.

```powershell
octadock capture-area
octadock capture-area --action copy
octadock capture-area --area 100,120,800,600 --monitor 1 --action annotate
```
```text
octadock://capture-area?action=copy
octadock://capture-area?x=100&y=120&width=800&height=600&monitor=1&action=annotate
```

### capture-previous-area

Reuses the most recent area selection rectangle. If there is no previous area, the
selection overlay opens.

Parameters: `action`.

```powershell
octadock capture-previous-area --action save
```
```text
octadock://capture-previous-area?action=copy
```

### capture-fullscreen

Captures the active monitor by default; can capture all monitors when configured.

Parameters: `monitor`, `allMonitors` (bool), `action`. (CLI: `--all-monitors`.)

```powershell
octadock capture-fullscreen --monitor 1 --action save
octadock capture-fullscreen --all-monitors --action copy
```
```text
octadock://capture-fullscreen?monitor=1&action=save
octadock://capture-fullscreen?allMonitors=true
```

### capture-window

Opens the window picker unless a valid `hwnd` is supplied. Captures the target
window when Windows capture APIs allow it.

Parameters: `action`, `includeShadow` (bool), `hwnd` (hexadecimal HWND string for
trusted local automation). (CLI: `--include-shadow`, `--hwnd`.)

```powershell
octadock capture-window --action copy
octadock capture-window --hwnd 0x000A1B2C --include-shadow
```
```text
octadock://capture-window?action=annotate
octadock://capture-window?hwnd=0x000A1B2C
```

### self-timer

Opens an area capture with a visible countdown pill after the region is chosen.

Parameters: `seconds` (defaults to the settings value), `action`.

```powershell
octadock self-timer --seconds 5 --action shelf
```
```text
octadock://self-timer?seconds=5
```

### scrolling-capture

Opens manual vertical scrolling capture mode and stitches the region into one
image. Current implementation rejects horizontal scrolling and auto-scroll.
`start=true` is accepted by the parser but does not enable a separate auto-start
path.

Parameters: `x`, `y`, `width`, `height`, `monitor`, `units`, `start` (bool),
`autoscroll` (bool; currently rejected when true), `direction` (`vertical`
supported; `horizontal` rejected), `action`.

```powershell
octadock scrolling-capture --direction vertical --start
```
```text
octadock://scrolling-capture?direction=vertical&start=true
```

### pin

Opens an image (from a file or the clipboard) in the floating image surface. The
window can be pinned/unpinned topmost from inside the surface. If no input is
supplied, Octadock prompts for a file.

Parameters: `filepath` (PNG/JPEG/WebP/BMP/GIF first frame), `clipboard` (bool).

```powershell
octadock pin --filepath "C:\Users\me\Desktop\reference.png"
octadock pin --clipboard
```
```text
octadock://pin?clipboard=true
```

### record-screen

Toggles screen recording: starts recording when idle, or stops and saves the
current recording when one is active. By default it records the active monitor.
Complete region coordinates record a fixed physical-pixel/DIP region, and the
CLI `--select-area` option opens the region selector before recording. The
current build is video-only. Audio and camera parameters are parsed for future
compatibility but are disabled/normalized off and not encoded.

Parameters: `x`, `y`, `width`, `height`, `monitor`, `units`, `microphone` (bool),
`systemAudio` (bool), `cursor` (bool), `camera` (bool), `selectArea` (bool).
(CLI: `--microphone`, `--system-audio`, `--cursor`, `--camera`,
`--select-area`, `--area x,y,width,height`.)

```powershell
octadock record-screen --select-area
octadock record-screen --area 100,120,800,600 --cursor
```
```text
octadock://record-screen?x=100&y=120&width=800&height=600&cursor=true
```

### capture-text

Runs local OCR on a file, a fixed region, or an interactively selected region and
copies the recognized text to the clipboard. Dispatch honors `filepath`, complete
region coordinates, `mode`, `linebreaks`, and `language`.

Parameters: `filepath`, `x`, `y`, `width`, `height`, `monitor`, `units`,
`linebreaks` (bool), `mode` (`compact` | `lines` | `layout`).

```powershell
octadock ocr --area 100,120,800,600 --mode lines
octadock capture-text --filepath "C:\shots\invoice.png" --mode layout
```
```text
octadock://capture-text?x=100&y=120&width=800&height=600&mode=lines
```

### dictation

CLI only. Toggles speech-to-text dictation using the current Speech settings.
The same command starts listening when idle, then stops/transcribes/inserts when
invoked again. `octadock://dictation` protocol URLs are blocked so a website or
external URI activation cannot start the microphone.

```powershell
octadock dictation
octadock dictate
```

### read

Reads text aloud. By default Octadock speaks the supplied text verbatim through
the configured TTS provider; Windows voices are keyless and local. Passing
`--explain` opens Use with AI with the source framed as untrusted evidence and
an editable investigation goal;
nothing is sent automatically. After an explicit send, use **Read aloud** on the
result. Protocol URLs are blocked so websites cannot trigger AI/TTS reads.

Parameters: `filepath`, `clipboard` (bool), `text`, `x`, `y`, `width`, `height`,
`monitor`, `units`, `explain` (bool), `provider`, `voiceId`, `modelId`, `stop`
(bool). `provider` is used only to preselect the destination in Use with AI.

```powershell
octadock read --clipboard
octadock read --filepath "C:\notes\brief.md"
octadock read --area 100,120,800,600 --explain
```

### ai

Opens Use with AI. Start with `build`, `investigate`, `verify`, `extract`, or
`handoff`; each workflow supplies a concrete starting goal and acceptance
criteria. Evidence may come from captures, Context packages, clipboard content,
local files, OCR, and before/after screenshots. The exact deterministic `TASK.md`,
review hash, attachment boundary, and detected-secret count are visible before
handoff. Text redaction defaults on; image pixels and binary attachments are
explicitly disclosed as unchanged.

Octadock invokes only the selected installed CLI; it does not silently fall back
to another provider, store API keys, or log/persist source or result text. The
provider may use its configured remote service. Codex runs without shell/exec
tools and receives reviewed text plus explicit images; Claude file reads are
scoped to the temporary packet. Results can be copied or read aloud. Opening
this window never sends data.

Parameters: `workflow` (`build`, `investigate`, `verify`, `extract`, or
`handoff`), `goal`, `criteria` (pipe-separated), `text`, `filepath`, `clipboard`
(bool), `captureid`, `contextid`, `project`, `target`, `environment`, `action`,
`source`, and `provider`. Historical action values and aliases remain accepted
as editable goal templates.

```powershell
octadock agent --workflow investigate --captureid 34dc8a55-77c9-4fc0-91b2-fb10853d3fe4 --goal "The compact overlay grows beyond short screenshots" --provider codex
octadock ai --workflow build --filepath "C:\notes\brief.md" --provider claude
octadock explain --text "What does this error mean?" --source "Build output"
```

### open-annotate

Opens the annotation editor for an image file. History capture routing is handled
inside the History UI; `captureId` is parsed but not dispatched by this command.

Parameters: `filepath`, `captureId` (history id). (CLI: `--filepath`,
`--capture-id`.)

```powershell
octadock open-annotate --filepath "C:\shots\bug.png"
octadock annotate --capture-id 6f9619ff-8b86-d011-b42d-00cf4fc964ff
```
```text
octadock://open-annotate?filepath=C:\shots\bug.png
```

### open

Opens a local file in Octadock. Supported raster images open in the floating
image surface. CSV/TSV, JSON, log, Markdown, text/code/config, and unsupported
files open in the preview/fallback surface.

Parameters: `filepath`.

```powershell
octadock open --filepath "C:\shots\data.csv"
```
```text
octadock://open?filepath=C:\shots\data.csv
```

### open-from-clipboard

Opens the current clipboard image in the annotation editor. No parameters.

```powershell
octadock open-from-clipboard
```
```text
octadock://open-from-clipboard
```

### add-shelf-item

Adds an external image or video to the Capture Shelf and history.

Parameters: `filepath`.

```powershell
octadock add-shelf-item --filepath "C:\path\to\file.png"
```
```text
octadock://add-shelf-item?filepath=C:\path\to\file.png
```

### open-history / restore-recently-closed / clear-history

Opens the local history window, restores the most recently closed shelf item, or
clears history after confirmation. No parameters.

```powershell
octadock open-history
octadock restore-recently-closed
octadock clear-history
```
```text
octadock://open-history
octadock://restore-recently-closed
octadock://clear-history
```

### open-clipboard-history

Opens the local clipboard history window: a searchable list of recorded text
and image clips with copy/favorite/delete actions. Monitoring is controlled by
the Settings → Clipboard tab and everything stays on the local machine. No
parameters.

```powershell
octadock open-clipboard-history
octadock clipboard
```
```text
octadock://open-clipboard-history
```

### open-text-tools

Opens the text-transform toolbox: JSON pretty-print/minify, Base64/URL/HTML
encode-decode, JWT decode, identifier casing, hashes, Unix-timestamp
conversion, and line utilities. All transforms run locally. No parameters.

```powershell
octadock open-text-tools
octadock transforms
```
```text
octadock://open-text-tools
```

### open-context

Opens the floating Context window. Context is separate from the Capture
Shelf. The current build can navigate packages, add files/captures from wired
surfaces, open items, delete items, and export a normal folder or zip package.

```powershell
octadock open-context
octadock context
```
```text
octadock://open-context
```

### activate

Activates a license key on this device. Activation is intentionally allowed from
`octadock://activate?key=...` even if protocol automation is disabled, so a
checkout/deep link can take the buyer to Account & Billing.

Parameters: `key`.

```powershell
octadock activate --key OCTA-XXXXX-XXXXX-XXXXX-XXXXX
```
```text
octadock://activate?key=OCTA-XXXXX-XXXXX-XXXXX-XXXXX
```

### quit

CLI only. Cleanly shuts down the running Octadock instance. Protocol URLs are
blocked for this command.

```powershell
octadock quit
octadock exit
```

### open-settings

Opens settings, optionally on a specific tab.

Parameters: `tab` (`general` | `shortcuts` | `shelf` | `capture` | `annotate` |
`recording` | `ocr` | `history` | `clipboard` | `automation` | `advanced`).

```powershell
octadock settings --tab shortcuts
```
```text
octadock://open-settings?tab=shortcuts
```

## Examples by tool

### PowerShell

```powershell
# Copy an area capture and check the result.
octadock.exe capture-area --action copy
if ($LASTEXITCODE -ne 0) { Write-Error "Octadock capture failed ($LASTEXITCODE)" }

# Parse the JSON result.
$result = octadock.exe --json capture-fullscreen --monitor 1 --action save | ConvertFrom-Json
if (-not $result.success) { Write-Warning $result.message }
if ($result.captureId) { Write-Host "Saved capture $($result.captureId)" }

# OCR a fixed region into the clipboard, preserving line breaks.
octadock.exe ocr --area 100,120,800,600 --mode lines
```

### AutoHotkey (v2)

```autohotkey
; Ctrl+Alt+A: copy an area capture.
^!a::Run '"octadock.exe" capture-area --action copy'

; Ctrl+Alt+O: OCR a fixed region.
^!o::Run '"octadock.exe" ocr --area 100,120,800,600 --mode lines'

; Ctrl+Alt+P: pin a reference image.
^!p::Run '"octadock.exe" pin --filepath "C:\Users\me\Desktop\reference.png"'
```

### Run dialog (Win+R) and browser/bookmarks

Type or bookmark a protocol URL:

```text
octadock://capture-area?action=copy
octadock://open-settings?tab=automation
```

### Flow Launcher / PowerToys Run

Add a shortcut/plugin action that runs `octadock.exe capture-area --action copy`
(or any command above), or that opens a `octadock://...` URL.

## Protocol registration

- **Unpackaged app** — the `octadock` protocol is registered under current-user
  registry keys during install, and activation is routed to the single running
  instance.
- **File associations** — on startup, Octadock registers a per-user
  `Octadock.Preview` ProgID under `HKCU\Software\Classes` and advertises itself
  in Explorer's "Open with" list for previewable text, CSV, code, and image file
  types. Images also get an "Add to Octadock dock" verb. The open command is
  routed through `octadock open --filepath "%1"`.
- **Packaged / MSIX app** — the protocol is declared in the app manifest and
  activation flows through the app's lifecycle activation args.

## Privacy

No network requests are made during capture, annotation, OCR, recording, local
history, or local Context export unless you enable a feature that needs the
network. Network-capable paths are model downloads for local STT, license
activation/update checks, opt-in OpenAI STT (`OCTADOCK_OPENAI_API_KEY` only),
opt-in ElevenLabs TTS, and explicitly confirmed Use with AI handoffs through
the selected Codex/Claude CLI. Use with AI shows the exact outbound task,
attachment boundary, and destination first, defaults local text-secret redaction
on, and has no fallback provider. Crash reports
are saved locally only when enabled; there is no uploader.

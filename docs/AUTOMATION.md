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
| `0` | The command was accepted and dispatched to Octadock. |
| `1` | Runtime/IPC error — pipe timeout, broken pipe, Octadock could not be started, or Octadock reported a failure. |
| `2` | Parse error — unknown command, malformed option, or invalid coordinates. |

The parse code (`2`) is produced locally by the parser before any pipe connection,
so obviously-bad input fails fast.

### JSON output

With `--json`, results and errors are emitted as one JSON line:

```jsonc
// success
{"success":true,"exitCode":0,"message":"Copied capture to clipboard."}

// failure (parse error)
{"success":false,"exitCode":2,"message":"Unknown command 'foo'. Expected one of: ...","error":"parse_error"}

// failure (runtime/IPC error)
{"success":false,"exitCode":1,"message":"Timed out waiting for Octadock to respond.","error":"timeout"}
```

`success`/`exitCode`/`message` are always present; `error` (a stable slug such as
`parse_error`, `runtime_error`, or `timeout`) is present only on failures.

### CLI aliases

For convenience the CLI accepts a few friendly aliases that resolve to canonical
verbs: `area`→`capture-area`, `window`→`capture-window`,
`fullscreen`→`capture-fullscreen`, `all`→`all-in-one`, `ocr`/`text`→`capture-text`,
`capture-ocr`→`capture-text`, `record`/`recording`→`record-screen`,
`dictate`/`speech`→`dictation`,
`allinone`→`all-in-one`, `annotate`/`edit`→`open-annotate`,
`history`→`open-history`, `shelf`→`add-shelf-item`, `settings`→`open-settings`,
`ai`/`ai-sessions`/`sessions`→`open-ai-sessions`.

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

Opens an image (from a file or the clipboard) as a floating, always-on-top pin. If
no input is supplied, prompts for a file.

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
CLI `--select-area` option opens the region selector before recording. Audio and
camera parameters are parsed for the future but are not encoded yet.

Parameters: `x`, `y`, `width`, `height`, `monitor`, `units`, `microphone` (bool),
`systemAudio` (bool), `cursor` (bool), `camera` (bool), `selectArea` (bool).
(CLI: `--microphone`, `--system-audio`, `--cursor`, `--camera`,
`--select-area`, `--area x,y,width,height`.)

```powershell
octadock record-screen --microphone --cursor
octadock record-screen --select-area
octadock record-screen --area 100,120,800,600 --cursor
```
```text
octadock://record-screen?microphone=true&cursor=true
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

Previews a file in Octadock's Quick Look-style preview card.

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

Adds an external image to the Capture Shelf and history.

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

### open-settings

Opens settings, optionally on a specific tab.

Parameters: `tab` (`general` | `shortcuts` | `shelf` | `capture` | `annotate` |
`recording` | `ocr` | `history` | `automation` | `advanced`).

```powershell
octadock settings --tab shortcuts
```
```text
octadock://open-settings?tab=shortcuts
```

### open-ai-sessions

Opens the Active AI Sessions window, showing recent `run`/`watch` sessions plus
auto-discovered Codex runtime and Claude Code sessions, with status, details,
and timeline events. No parameters.

```powershell
octadock open-ai-sessions
octadock ai
```
```text
octadock://open-ai-sessions
```

### run / watch (CLI only)

Starts or attaches to a local process and tracks it as an Active AI Session.
`run` launches the command after the `--` delimiter; `watch` attaches to an
already-running PID. Octadock stores the session and updates it when the process
exits while Octadock remains running. These verbs are intentionally blocked from
`octadock://` URLs because they can launch or observe local processes.

Parameters: `title`, `cwd`, `command`, `pid`, `notify`
(`silent` | `toast` | `toastAndSound` | `toastAndTts`).

```powershell
octadock run --title "Core tests" --cwd "C:\repo" -- dotnet test
octadock watch --pid 1234 --title "Claude Code"
```

### ai-session-event (CLI only)

Adds a local hook event to an existing Active AI Session. Tool hooks can use this
to push lifecycle updates without process scraping. `waiting`/`needs-input`
events mark the session as waiting and notify unless the session is silent;
terminal statuses such as `completed` or `failed` update the final state.
This verb is intentionally blocked from `octadock://` URLs.

Parameters: `session-id`, `event`, optional `status`, `message`, `source`,
`metadata-json`, and `exit-code`.

```powershell
octadock ai-session-event --session-id 6f41c8bf-7b44-45ea-8f52-8221d9a4b59e --event waiting --message "Approval required" --source claude-hook
octadock ai-session-event --session-id 6f41c8bf-7b44-45ea-8f52-8221d9a4b59e --event status-changed --status failed --exit-code 7
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
  types. The command is routed through `octadock open --filepath "%1"`.
- **Packaged / MSIX app** — the protocol is declared in the app manifest and
  activation flows through the app's lifecycle activation args.

## Privacy

No network requests are made during capture, annotation, OCR, or recording unless
you configure an upload destination. OCR runs locally, history is local, and
crash reports are saved locally only when enabled. Upload plugins must show their
destination before first use.

# Octadock Windows API And Data Spec

Date: 2026-07-01
Status: Draft, partially implemented. See `../PROJECT-STATE.md` for current
runtime behavior.

## Automation Surfaces

Octadock exposes two automation surfaces:

- Protocol URL: `octadock://...`
- CLI: `octadock.exe ...`

Use cases:

- PowerShell scripts.
- AutoHotkey shortcuts.
- Flow Launcher and PowerToys Run actions.
- Desktop shortcuts.
- Browser/bookmark actions.
- Test automation.

## Protocol Registration

Unpackaged app:

- Register `octadock` protocol under current-user registry keys during install.
- Route protocol activation to the single running instance.

Packaged/MSIX app:

- Register protocol in app manifest.
- Route activation through app lifecycle activation args.

## Coordinate Model

Coordinates:

- Public APIs accept physical pixels by default.
- Optional `units=dip` accepts WPF device-independent pixels.
- `monitor` identifies a monitor by current index or stored monitor id.
- Origin is the virtual screen top-left for Windows consistency.

Common parameters:

- `x`: optional number.
- `y`: optional number.
- `width`: optional number.
- `height`: optional number.
- `monitor`: optional string or number.
- `units`: optional enum: `px`, `dip`.
- `action`: optional enum: `shelf`, `copy`, `save`, `annotate`, `upload`, `pin`, `discard`.
- `filename`: optional string.
- `preset`: optional string.
- `silent`: optional boolean. If true, avoid modal UI unless impossible.

## URL Commands

### All-In-One

`octadock://all-in-one`

Optional parameters:

- `x`, `y`, `width`, `height`, `monitor`, `units`
- `mode`: `area`, `window`, `fullscreen`, `scrolling`, `ocr`, `record`

Behavior:

- Opens compact capture HUD.
- Current implementation honors `mode`. Complete coordinates preload the HUD's
  remembered fixed capture target; width/height-only parameters preload the
  fixed-size fields.

### Capture Area

`octadock://capture-area`

Examples:

- `octadock://capture-area`
- `octadock://capture-area?action=copy`
- `octadock://capture-area?x=100&y=120&width=800&height=600&monitor=1&action=annotate`

Behavior:

- Without coordinates, opens area selection overlay.
- With complete coordinates, captures immediately after hiding Octadock UI.
- Performs requested action or defaults to shelf.

### Capture Previous Area

`octadock://capture-previous-area`

Parameters:

- `action`

Behavior:

- Reuses most recent area selection rectangle.
- If no previous area exists, opens area selection overlay.

### Capture Fullscreen

`octadock://capture-fullscreen`

Parameters:

- `monitor`
- `allMonitors`: optional boolean
- `action`

Behavior:

- Captures active monitor by default.
- Can capture all monitors if configured.

### Capture Window

`octadock://capture-window`

Parameters:

- `action`
- `includeShadow`: optional boolean.
- `hwnd`: optional hexadecimal HWND string for trusted local automation.

Behavior:

- Opens window picker unless a valid `hwnd` is supplied.
- Captures target window when allowed by Windows capture APIs.

### Self Timer

`octadock://self-timer`

Parameters:

- `seconds`: number, default from settings.
- `action`

Behavior:

- Opens area capture with countdown.

### Scrolling Capture

`octadock://scrolling-capture`

Parameters:

- `x`, `y`, `width`, `height`, `monitor`, `units`
- `start`: optional boolean
- `autoscroll`: optional boolean
- `direction`: `vertical`, `horizontal`
- `action`

Behavior:

- Opens manual vertical scrolling capture mode.
- Current implementation rejects `direction=horizontal` and `autoscroll=true`.
- `start=true` is parsed but does not enable a separate auto-start path yet.

### Pin

`octadock://pin`

Parameters:

- `filepath`: optional path to PNG/JPEG/WebP/BMP/GIF first frame.
- `clipboard`: optional boolean.

Behavior:

- Opens specified image or clipboard image as a floating Pin.
- If no input is supplied, prompts for a file.

### Record Screen

`octadock://record-screen`

Parameters:

- `x`, `y`, `width`, `height`, `monitor`, `units`
- `microphone`: optional boolean
- `systemAudio`: optional boolean
- `cursor`: optional boolean
- `camera`: optional boolean

Behavior:

- Current implementation toggles MP4 recording: active monitor by default,
  explicit `x`/`y`/`width`/`height` when supplied, or interactive area selection
  with `select-area=true` / `selected-area=true`.
- Microphone, system-audio, cursor, and camera parameters are planned but not
  honored by current dispatch. Audio tracks are not encoded yet.

### Capture Text

`octadock://capture-text`

Parameters:

- `filepath`: optional path.
- `x`, `y`, `width`, `height`, `monitor`, `units`
- `linebreaks`: optional boolean.
- `mode`: `compact`, `lines`, `layout`.

Behavior:

- Runs OCR on selected region or file.
- Copies recognized text to clipboard.
- Current implementation honors file OCR and interactive region OCR. Parsed
  region coordinates fall back to the interactive picker unless `filepath` is
  supplied.

### Dictation

CLI only:

- `octadock.exe dictation`
- `octadock.exe dictate`

Behavior:

- Toggles speech-to-text dictation with the configured Speech provider/model,
  language, insertion mode, and dictionary.
- `octadock://dictation` protocol activation is blocked because it can start
  microphone capture.

### Open Annotate

`octadock://open-annotate`

Parameters:

- `filepath`: optional path.
- `captureId`: optional history id.

Behavior:

- Opens editor with the specified image file.
- `captureId` routing is handled by the History UI, not this command dispatch
  path.

### Open From Clipboard

`octadock://open-from-clipboard`

Behavior:

- Opens clipboard image in annotation editor.

### Open

`octadock://open`

Parameters:

- `filepath`: required path to a previewable local file.

Behavior:

- Opens the Quick Look-style file preview card.
- Current providers cover CSV/TSV, text/code/config, image files, and a generic
  file-info fallback. UNC paths are rejected.

### Capture Shelf

`octadock://add-shelf-item?filepath=C:\path\to\file.png`

Behavior:

- Adds an external image to Capture Shelf and history.

### History

Commands:

- `octadock://open-history`
- `octadock://restore-recently-closed`
- `octadock://clear-history`

Behavior:

- Opens local history, restores most recently closed shelf item, or clears history after confirmation.

### Settings

`octadock://open-settings`

Parameters:

- `tab`: `general`, `shortcuts`, `shelf`, `capture`, `annotate`, `recording`, `ocr`, `speech`, `ai-sessions`, `history`, `automation`, `advanced`.

## CLI Commands

Pattern:

```powershell
octadock.exe <command> [options]
```

Examples:

```powershell
octadock.exe capture-area --action copy
octadock.exe capture-fullscreen --monitor 1 --action save
octadock.exe pin --filepath "C:\Users\me\Desktop\reference.png"
octadock.exe ocr --area 100,120,800,600 --mode lines
octadock.exe settings --tab shortcuts
```

CLI should:

- Forward commands to the running tray instance.
- Start the tray instance if it is not running.
- Return nonzero exit codes for invalid commands or unavailable capabilities.
- Print machine-readable JSON with `--json`.

## Local Data Model

Base path:

`%LOCALAPPDATA%\Octadock\`

### Capture

```json
{
  "id": "uuid",
  "type": "area|window|fullscreen|scrolling|recording|ocr-source|external",
  "createdAt": "2026-07-01T08:00:00Z",
  "sourceProcess": "explorer.exe",
  "sourceWindow": "Documents",
  "hwndHash": "sha256-prefix",
  "monitorId": "DISPLAY1",
  "pixelWidth": 1800,
  "pixelHeight": 1200,
  "dpiScale": 1.5,
  "originalPath": "Captures\\2026\\07\\01\\uuid.png",
  "thumbnailPath": "Thumbnails\\uuid.jpg",
  "projectPath": null,
  "durationMs": null,
  "deletedAt": null
}
```

### Annotation Project Manifest

```json
{
  "format": "octadock-project",
  "version": 1,
  "createdAt": "2026-07-01T08:00:00Z",
  "baseImage": "original.png",
  "canvas": {
    "width": 1800,
    "height": 1200,
    "background": "transparent"
  },
  "objectsFile": "objects.json",
  "metadata": {
    "sourceCaptureId": "uuid"
  }
}
```

### Annotation Object

```json
{
  "id": "uuid",
  "type": "arrow|rect|text|blur|pixelate|highlight|crop|image",
  "frame": { "x": 100, "y": 100, "width": 300, "height": 80 },
  "style": {
    "stroke": "#0078D4",
    "fill": null,
    "lineWidth": 4,
    "opacity": 1
  },
  "payload": {
    "text": null,
    "points": []
  },
  "locked": false,
  "zIndex": 10
}
```

### Settings Keys

- `general.launchAtLogin`
- `general.showTrayIcon`
- `general.showTaskbarIcon`
- `capture.defaultAction`
- `capture.saveDirectory`
- `capture.filenameTemplate`
- `capture.includeCursor`
- `capture.windowShadow`
- `capture.excludeOctadockWindows`
- `shelf.anchor`
- `shelf.size`
- `shelf.autoClose`
- `shelf.restoreEnabled`
- `dock.enabled`
- `dock.hasCustomAnchor`
- `dock.anchorX`
- `dock.anchorY`
- `history.retention`
- `history.enabled`
- `ocr.provider`
- `ocr.outputMode`
- `recording.fps`
- `recording.quality`
- `recording.includeCursor`
- `recording.includeMicrophone`
- `recording.includeSystemAudio`
- `shortcuts.captureArea`
- `shortcuts.captureWindow`
- `shortcuts.captureFullscreen`
- `shortcuts.capturePreviousArea`
- `shortcuts.allInOne`
- `shortcuts.ocr`
- `shortcuts.record`

Settings groups still partial or planned:

- `speech.*` is partially implemented for STT provider/model/language,
  insertion mode, dictionary, OpenAI provider availability, and the toggle
  dictation shortcut/CLI command; hold-to-talk and live partials are still
  planned.
- `ai.*` for local/cloud model providers and privacy controls.
- `aiSessions.*` now includes passive overlay and recent-completion visibility
  controls, alongside persisted AI sessions/events, generic run/watch
  notifications, and the AI Sessions window; dedicated settings for
  notifications/adapters are still planned.
- `mcp.*` for local MCP server exposure.

## Active AI Session Data Model

The first Active AI Sessions / Agent Mission Control schema is implemented for
generic local process watching, CLI-wrapped runs, bounded stdout/stderr timeline
events, full stdout/stderr log artifacts for wrapped runs, generic input-prompt
waiting status for wrapped runs, local CLI hook events, and artifact links. The
tray/Dock/CLI window can list recent sessions, their event timelines, and open
wrapped-run log files. Provider-specific hook adapters, provider-specific
waiting detection/log enrichment, and cloud/background agent adapters are still
planned.

### AI Session

```json
{
  "id": "uuid",
  "provider": "generic|claude-code|codex|cursor|copilot|jules|vercel|ollama",
  "title": "Fix failing tests",
  "cwd": "C:\\repo",
  "gitBranch": "feature/session-watch",
  "command": "npm test",
  "pid": 12345,
  "status": "running|waiting|failed|completed|cancelled|pr-ready",
  "startedAt": "2026-07-03T10:00:00Z",
  "endedAt": null,
  "exitCode": null,
  "lastEventAt": "2026-07-03T10:03:10Z",
  "notificationMode": "toast|tts|silent",
  "metadataJson": "{}"
}
```

### AI Session Event

```json
{
  "id": "uuid",
  "sessionId": "uuid",
  "createdAt": "2026-07-03T10:03:10Z",
  "kind": "stdout|stderr|status|tool-call|needs-input|artifact|pr",
  "message": "Tests completed.",
  "payloadJson": "{}"
}
```

### AI Session Artifact Link

```json
{
  "id": "uuid",
  "sessionId": "uuid",
  "captureId": "uuid",
  "filePath": "C:\\repo\\test.log",
  "kind": "capture|recording|ocr|clipboard|file|pr|log"
}
```

## File Naming Template

Default:

`Screenshot {yyyy}-{MM}-{dd} at {HH}.{mm}.{ss}`

Tokens:

- `{yyyy}`, `{MM}`, `{dd}`, `{HH}`, `{mm}`, `{ss}`
- `{process}`
- `{window}`
- `{type}`
- `{counter}`

Illegal filename characters are replaced with safe equivalents.

## Privacy Defaults

- No network requests during capture, annotation, OCR, or recording unless user configures upload.
- OCR runs locally.
- History is local.
- Crash reports are saved locally only when enabled.
- Upload plugins must show destination before first use.

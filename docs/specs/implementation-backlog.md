# Octadock Windows Implementation Backlog

Date: 2026-07-01
Status: Historical backlog plus current status notes. See `../PROJECT-STATE.md`
for current reality and `../ROADMAP.md` for the active plan.

## Current Milestone Status (2026-07-03)

| Milestone | Status | Notes |
| --- | --- | --- |
| 0 Project Skeleton | Built | Tray app, single instance, settings, first-run, startup/protocol registration, build scripts. |
| 1 Hotkeys And Selection Overlay | Built | Needs ongoing mixed-DPI regression testing. |
| 2 Capture Engine And Shelf MVP | Built | Capture engine uses GDI for area/all-monitor and WGC with GDI fallback for window/single-monitor. |
| 3 History And File Pipeline | Built | Date-filter UI and some retention edge cases still need polish. |
| 4 Annotation MVP | Built | Additional advanced tools have also landed; embedded image annotation remains incomplete. |
| 5 Pins | Built | Restored pins are clamped/gatherable after monitor layout changes; close persistence remains a regression-risk area. |
| 6 OCR And Automation | Partial | Windows.Media.Ocr and CLI/protocol are built; Windows AI/Tesseract fallback and some command docs/params are not. |
| 7 Scrolling Capture | Partial | Manual vertical capture is built; auto-scroll and horizontal are not. |
| 8 Recording MVP | Partial | Active-monitor and selected-area MP4 video paths are built; audio is not. |
| 9 Advanced Recorder | Planned | System audio, GIF, camera, click/keystroke overlays, trim/compress. |
| 10 Polish And Distribution | Partial | Installer, signing, update strategy, and manual matrix remain; local opt-in crash reports are wired. |

## Milestone 0: Project Skeleton

Goal: A Windows tray app that can start, run, and receive commands.

Tasks:

- Create C#/.NET WPF solution.
- Add tray icon and tray context menu.
- Add single-instance guard.
- Add hidden message window for hotkeys and protocol dispatch.
- Add settings window shell.
- Add first-run setup shell.
- Add startup registration setting.
- Add logging and error reporting abstraction.
- Add build scripts for debug/release.

Acceptance:

- App launches and quits cleanly.
- Tray icon works.
- Settings opens.
- Second launch forwards to existing instance.

## Milestone 1: Hotkeys And Selection Overlay

Goal: Press shortcut, draw region, produce selected rectangle data.

Tasks:

- Implement Win32 `RegisterHotKey` wrapper.
- Handle `WM_HOTKEY` in hidden message window.
- Add configurable shortcuts.
- Detect hotkey registration conflicts.
- Make app per-monitor DPI aware.
- Implement monitor/work-area service.
- Implement transparent topmost selection overlay per monitor.
- Add drag selection, resize handles, dimensions, magnifier, and Escape cancel.
- Store previous area rectangle.

Acceptance:

- Default capture-area hotkey opens overlay.
- User can select region across mixed-DPI monitor setups.
- Escape cancels.
- Previous-area command recalls last rectangle.

## Milestone 2: Capture Engine And Shelf MVP

Goal: The core "capture then dock bottom-left" loop works.

Tasks:

- Prototype still capture using Windows.Graphics.Capture.
- Add DXGI Desktop Duplication fallback spike if Windows.Graphics.Capture blocks area capture UX.
- Add window exclusion for Octadock windows with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.
- Hide Octadock UI before capture when exclusion is unavailable.
- Implement fullscreen active-monitor capture.
- Implement previous-area capture.
- Implement basic window picker and window capture.
- Write captures to temp/history file.
- Generate thumbnails.
- Implement Capture Shelf anchored bottom-left of active monitor.
- Add shelf actions: copy, save, discard, drag/drop.
- Add context menu and restore recently closed.
- Add shelf settings: anchor, auto-close, size.

Acceptance:

- Press hotkey, drag region, release, see shelf bottom-left.
- Click copy, paste image into another app.
- Drag shelf item into File Explorer and browser upload field.
- Shelf does not appear in supported captures.
- Multiple captures stack.

## Milestone 3: History And File Pipeline

Goal: Captures are durable, recoverable, and cleaned up predictably.

Tasks:

- Add SQLite database.
- Add `captures`, `actions`, `pins`, and `settings` tables.
- Store source process/window metadata when available.
- Add thumbnail cache.
- Add retention cleanup job.
- Add history window with filters.
- Add soft-delete and restore.
- Add "open file location" and "save as" actions.

Acceptance:

- User can recover a recently closed shelf item.
- User can delete and restore captures.
- Retention settings clean old files without blocking capture.

## Milestone 4: Annotation MVP

Goal: Useful annotation without leaving the app.

Tasks:

- Build editor window with image canvas.
- Implement tool model and selection model.
- Add crop, arrow, rectangle, text, blur, pixelate, highlighter.
- Add color/line width controls.
- Add undo/redo.
- Add export flattening to PNG/JPEG.
- Add copy/save/save-as.
- Add project package save/load with editable vector objects.
- Add editor entry points from shelf, file, clipboard, and history.

Acceptance:

- User can annotate a screenshot and copy/export it.
- Project can be reopened and edited.
- Exported image matches visible canvas.

## Milestone 5: Pins

Goal: Captures become reusable desktop references.

Tasks:

- Implement borderless topmost pin window.
- Add pin creation from shelf, editor, file, clipboard, and history.
- Add pin actions: close, copy, save, annotate, opacity, resize.
- Add click-through lock mode using extended window styles.
- Add keyboard nudging.
- Add hide all/close all pins commands.
- Store pin state in SQLite.

Acceptance:

- User can pin an image above other windows.
- User can click through locked pin.
- User can hide and restore pins.

## Milestone 6: OCR And Automation

Goal: Power-user workflows become scriptable and text extraction works locally.

Tasks:

- Build OCR provider interface.
- Spike Windows App SDK AI Text Recognition.
- Spike Windows.Media.Ocr with MSIX/package identity.
- Decide on Tesseract fallback.
- Add region OCR overlay.
- Add file/shelf OCR.
- Add output modes: compact, lines, layout.
- Copy OCR result to clipboard.
- Register `octadock://` protocol.
- Implement `octadock.exe` CLI forwarding.
- Implement command parser and dispatcher.
- Add automation settings.

Acceptance:

- `octadock://capture-area?action=copy` works from Run dialog/browser.
- `octadock.exe capture-area --action copy` works from PowerShell.
- Region OCR copies text to clipboard.
- Invalid commands fail safely.

## Milestone 7: Scrolling Capture

Goal: Capture long pages and documents.

Tasks:

- Build scrolling capture setup UI.
- Capture selected viewport frames.
- Detect scroll delta and stitch images.
- Implement manual scroll mode.
- Investigate auto-scroll through UI Automation and simulated wheel input.
- Add output size warning.
- Add vertical direction first, horizontal later.
- Add scrolling captures to history and shelf.

Acceptance:

- User can capture a long webpage into one image.
- Obvious stitch artifacts are minimized.
- Very large captures warn before exhausting memory.

## Milestone 8: Recording MVP

Goal: Quick MP4 bug recording.

Tasks:

- Implement recording area selector.
- Integrate Windows.Graphics.Capture frame stream.
- Add microphone capture through WASAPI.
- Add cursor show/hide.
- Add countdown.
- Add tray timer and stop control.
- Encode MP4 through Media Foundation or FFmpeg after spike.
- Add finished recording to Capture Shelf.
- Add simple playback preview.
- Done partial failure cleanup: failed recording start/stop paths delete
  incomplete MP4 outputs before notifying.

Acceptance:

- User can record a selected area with microphone.
- Recording appears in shelf and can be dragged/saved.
- Stopping/canceling behaves predictably.

## Milestone 9: Advanced Recorder

Goal: Match power-user recording expectations.

Tasks:

- Add system audio through WASAPI loopback.
- Add GIF export.
- Add click visualization.
- Add keystroke visualization.
- Add camera overlay.
- Add pause/resume/restart.
- Add crash/disk-full recovery.
- Add trim/compress editor.

Acceptance:

- User can create a bug-report video with cursor, mic, and visible clicks.
- User can trim and reduce file size before sharing.

## Milestone 10: Polish And Distribution

Goal: A trustworthy Windows 1.0 utility.

Tasks:

- Full settings coverage.
- Light/dark/high-contrast polish.
- Multi-monitor and DPI testing.
- Performance profiling.
- App icon and original visual identity.
- Installer.
- Optional MSIX package.
- Code signing.
- Auto-update strategy.
- Local crash-report opt-in is wired; no upload/telemetry transport exists.
- User docs and automation docs.

Acceptance:

- App can be installed on a fresh Windows 10/11 PC.
- Core flows pass manual matrix.
- No network activity unless user enables upload/checks update.

## Test Matrix

Windows versions:

- Windows 10 22H2.
- Windows 11 current stable.

Displays:

- Single 100 percent DPI.
- Single 150/200 percent DPI.
- Multi-monitor mixed DPI.
- Portrait monitor.
- Taskbar bottom, top, left, right, and auto-hide.

System states:

- Light/dark mode.
- High contrast mode.
- RDP session.
- Low disk space.
- Protected windows or windows that return black capture.
- App running elevated vs normal user.

Workflows:

- Screenshot Edge/Chrome, File Explorer, Terminal, Visual Studio/VS Code, Teams/Discord/Slack, fullscreen app.
- Drag shelf item to File Explorer, browser, Teams/Discord/Slack, email, docs editor.
- Capture while shelf contains previous captures.
- Annotate 8k screenshot.
- Pin and lock-through over another app.
- OCR Latin text and mixed-language text.
- Record with no mic, default mic, external audio interface.

## Risk Register

- Overlay appears in captures. Mitigation: `SetWindowDisplayAffinity`, hide windows before capture, and test per API/Windows version.
- Windows capture APIs may require picker/consent or show capture borders. Mitigation: prototype first and isolate capture engine.
- DPI coordinate bugs. Mitigation: store physical pixels and test mixed-DPI early.
- OCR availability varies. Mitigation: pluggable OCR providers and fallback.
- Scrolling capture stitch quality is hard. Mitigation: ship manual scroll first and add auto-scroll later.
- Global hotkeys conflict with OS/apps. Mitigation: conflict detection and user-configurable shortcuts.
- System audio recording delays recorder. Mitigation: ship microphone-only recording MVP first.

## Suggested First Sprint

1. Scaffold C#/.NET WPF tray app.
2. Add single-instance and hidden message window.
3. Add `RegisterHotKey` capture-area shortcut.
4. Build per-monitor selection overlay.
5. Capture selected rectangle to file.
6. Show Capture Shelf bottom-left with copy/save/discard.

Definition of done:

- On a Windows PC, press shortcut, drag region, release, see shelf, click copy, paste the image into another app.

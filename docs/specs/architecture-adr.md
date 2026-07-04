# ADR-001: Windows Desktop Architecture For Octadock

Status: Proposed
Date: 2026-07-01
Deciders: Product/engineering owner

Status note 2026-07-03: the WPF/.NET direction was accepted and implemented.
For the as-built architecture, see `../ARCHITECTURE.md`; for current feature
state, see `../PROJECT-STATE.md`.

## Context

Octadock needs deep Windows integration: global hotkeys, high-fidelity screenshots, multi-monitor DPI handling, screen recording, local OCR, always-on-top floating pins, drag/drop, clipboard integration, local history, tray behavior, and a docked post-capture shelf. The app must feel instant and must avoid capturing its own UI.

## Decision

Build Octadock as a Windows desktop app in C#/.NET with WPF for the MVP. Use Win32 and WinRT interop for capture, hotkeys, capture exclusion, window enumeration, clipboard/drag-drop, and recording. Revisit WinUI 3 later for settings/polish only if it does not slow down overlay and capture work.

## Options Considered

### Option A: C#/.NET WPF + Win32/WinRT Interop

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium |
| Cost | Fastest MVP path |
| Performance | Good enough for overlays/editor; native APIs for capture |
| OS Integration | Strong through P/Invoke and WinRT |
| Team familiarity | High for Windows desktop development |

Pros:

- Mature support for transparent borderless windows, topmost panels, drag/drop, clipboard, tray apps, and custom drawing.
- Easy Win32 interop for `RegisterHotKey`, `SetWindowDisplayAffinity`, window enumeration, and window styles.
- Works well unpackaged during early development.
- Fastest path to the shelf/overlay MVP.

Cons:

- WPF is older than WinUI 3.
- Some modern Windows AI/OCR APIs may require package identity or extra interop.
- High-performance video paths may need Direct3D/Media Foundation wrappers.

### Option B: WinUI 3 + Windows App SDK

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium-high |
| Cost | Better modern UI, slower overlay MVP |
| Performance | Good |
| OS Integration | Strong but often requires HWND interop |
| Team familiarity | Moderate |

Pros:

- Microsoft's modern native UI framework for Windows desktop apps.
- Better Fluent styling out of the box.
- Natural pairing with Windows App SDK AI APIs.

Cons:

- Transparent always-on-top overlay/pin behavior is less straightforward.
- Tray app and low-level window behavior still require Win32 interop.
- Adds framework complexity before the core product is proven.

### Option C: C++/Win32 + Direct2D/WinRT

| Dimension | Assessment |
|-----------|------------|
| Complexity | High |
| Cost | Slower development |
| Performance | Best |
| OS Integration | Best |
| Team familiarity | Requires stronger native Windows expertise |

Pros:

- Maximum control and performance.
- Cleanest Direct3D/Media Foundation integration.

Cons:

- Slower to build and iterate.
- More manual UI work.
- Higher bug risk in editor and app shell.

### Option D: Electron/Tauri

| Dimension | Assessment |
|-----------|------------|
| Complexity | Medium-high |
| Cost | Native modules required anyway |
| Performance | Risky for utility feel |
| OS Integration | Weak without native bridge |
| Team familiarity | Good for web UI |

Pros:

- Fast UI iteration.
- Cross-platform potential.

Cons:

- The hard parts are all native: capture, hotkeys, overlays, pins, clipboard, OCR, recording.
- Bigger runtime and weaker invisible-utility feel.

## Trade-Off Analysis

The killer feature is not a fancy UI. It is the feeling that a capture instantly becomes a small useful object on the Windows desktop. WPF plus Win32/WinRT interop gives the fastest credible route to that: transparent selection overlays, topmost shelf/pins, global hotkeys, clipboard/drag-drop, and a normal installer.

WinUI 3 is attractive for polish, but it does not remove the need for Win32 interop and may slow down the overlay work. C++ is ideal for the capture/recording engine but too heavy for the first pass. The pragmatic call is C# WPF MVP, with low-level capture/recording isolated behind interfaces so we can replace internals later.

## Proposed System Architecture

### Process Model

- One tray app process.
- Single hidden message window for hotkeys and protocol dispatch.
- Optional startup registration in current user run key or installer startup task.
- No always-on service in v1.

### Main Modules

- `AppShell`: lifecycle, tray icon, startup registration, single-instance lock.
- `CommandRouter`: maps hotkeys, tray menu, protocol URLs, and CLI commands to app actions.
- `Hotkeys`: Win32 `RegisterHotKey`, `UnregisterHotKey`, and `WM_HOTKEY` handling.
- `WindowInterop`: HWND helpers, monitor info, DPI conversions, window enumeration, z-order, foreground window, process/window names.
- `CaptureCoordinator`: routes capture commands to capture modes and post-capture actions.
- `SelectionOverlay`: transparent topmost per-monitor overlay for area selection, magnifier, dimensions, and freeze-screen preview.
- `WindowPicker`: enumerates and highlights candidate windows, filters invisible/tool windows, resolves HWND capture target.
- `CaptureEngine`: still capture abstraction with Windows.Graphics.Capture first and DXGI fallback.
- `Shelf`: bottom-left capture shelf, stacked items, auto-close, restore, drag/drop, context menu.
- `Annotate`: raster/vector editor with tools, layers, undo/redo, export, and project save.
- `Pins`: floating topmost image windows with opacity, resize, click-through lock, and keyboard nudging.
- `HistoryStore`: SQLite metadata plus file storage and retention cleanup.
- `OCRService`: Windows OCR abstraction with pluggable providers.
- `RecordingEngine`: Windows.Graphics.Capture frame stream plus encoder/audio pipeline.
- `ProtocolAndCli`: `octadock://` protocol handler and `octadock.exe` CLI.
- `UploadProviders`: optional BYO upload plugins in later releases.

## Recommended Windows APIs

- `Windows.Graphics.Capture` for display/window frames, snapshots, and recording streams.
- DXGI Desktop Duplication API for low-level monitor-frame fallback and high-performance capture experiments.
- `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE` for Octadock-owned shelf/overlay windows on Windows 10 version 2004+.
- Win32 `RegisterHotKey` and `WM_HOTKEY` for global shortcuts.
- WPF/Win32 transparent layered windows and topmost windows for overlays, shelf, and pins.
- Windows Clipboard and OLE drag/drop for copy and drag-to-app workflows.
- Windows App SDK AI Text Recognition or `Windows.Media.Ocr` for OCR, with Tesseract fallback if required.
- WASAPI for microphone and system audio loopback.
- Media Foundation or FFmpeg for video encoding after recording prototype.
- SQLite for history metadata.

## Data Storage

Base folder:

`%LOCALAPPDATA%\Octadock\`

Subfolders:

- `Captures\YYYY\MM\DD\`
- `Projects\`
- `Recordings\`
- `Thumbnails\`
- `TempExports\`
- `Logs\`

SQLite tables:

- `captures`: id, type, created_at, source_process, source_window, hwnd_hash, monitor_id, pixel_width, pixel_height, dpi_scale, original_path, thumbnail_path, project_path, duration_ms, deleted_at.
- `actions`: id, capture_id, action_type, created_at, destination, metadata_json.
- `pins`: id, capture_id, x, y, width, height, opacity, click_through, monitor_id, last_visible_at.
- `settings`: key, value_json, updated_at.

Project package:

`Example.octadock\`

- `manifest.json`
- `original.png`
- `preview.png`
- `objects.json`
- `assets\`

## Capture Shelf Behavior

- Anchor defaults to active monitor bottom-left with safe margins above the taskbar.
- Shelf window calls `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` where supported.
- New item enters shelf after capture and thumbnail generation.
- Shelf item contains thumbnail, file metadata, and action buttons.
- Dragging the thumbnail provides a file-drop payload and bitmap where target app supports it.
- Copy writes `CF_BITMAP`/PNG-compatible clipboard data plus optional file-drop data.
- Save prompts or saves to configured folder.
- Annotate opens editor while preserving source in history.
- Pin creates a Pin window.
- Discard soft-deletes from shelf and history until cleanup.

## DPI And Multi-Monitor Requirements

- App must be per-monitor DPI aware.
- Selection coordinates are stored in physical pixels and translated from WPF device-independent units.
- Shelf anchor is computed per monitor using work area, not full bounds, so it avoids taskbar positions.
- Multi-monitor captures store monitor id and DPI scale.
- Mixed-DPI setups are part of the manual test matrix.

## Permission And OS Risks

- Some Windows capture APIs show user-facing capture borders or require picker/consent flows.
- `WDA_EXCLUDEFROMCAPTURE` only has full exclude behavior starting Windows 10 version 2004.
- `Windows.Media.Ocr` for desktop apps requires package identity.
- Windows App SDK AI OCR may depend on NPU/hardware/model availability.
- System audio recording through WASAPI loopback is separate from microphone capture.
- Some protected/UWP/secure windows may be black or unavailable to capture.

## Performance Requirements

- App idle memory target: under 180 MB after warm startup.
- Capture-to-shelf p95 target: under 500 ms for normal area capture.
- Shelf thumbnail generation should not block UI.
- Annotation canvas should remain responsive with 8k screenshots and 100 vector objects.
- History cleanup should run opportunistically and never block capture.

## Testing Strategy

- Unit tests for command parsing, settings, history retention, project serialization, and action routing.
- Render/export tests for annotation objects.
- Integration tests for capture abstractions using fixtures and mocked capture providers.
- Manual matrix for Windows 10/11, single/multi-monitor, mixed DPI, taskbar left/right/top/bottom/auto-hide, light/dark/high-contrast, RDP, protected windows, and permission/availability failures.
- Recording tests for microphone, no microphone, system audio, long recordings, cancellation, disk-full, and crash recovery.

## Consequences

Easier:

- Fast MVP for overlays, shelf, tray, clipboard, drag/drop, pins, and settings.
- Good Windows compatibility without MSIX at first.
- Clear upgrade path for capture/recording internals.

Harder:

- Modern Fluent polish is manual or deferred.
- OCR path needs a compatibility decision.
- Recording engine may require Direct3D/Media Foundation expertise.

Revisit:

- Move settings/history UI to WinUI 3 after the core loop works.
- Package as MSIX if OCR/provider needs or Store distribution justify it.
- Replace WPF annotation rendering with SkiaSharp/Direct2D if performance demands it.

## Action Items

1. Create C#/.NET WPF solution and tray shell.
2. Add single-instance guard and hidden message window.
3. Implement `RegisterHotKey` wrapper.
4. Implement per-monitor selection overlay.
5. Implement still capture prototype with Windows.Graphics.Capture or DXGI fallback.
6. Implement Capture Shelf and clipboard/drag-drop actions.
7. Add history database and thumbnail pipeline.
8. Build annotation MVP.
9. Add pins.
10. Add OCR and automation.
11. Add scrolling capture and recording.
12. Package with installer and optional MSIX.

## Sources

- Microsoft Windows.Graphics.Capture: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture
- Microsoft Screen Capture docs: https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture
- Microsoft Desktop Duplication API: https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api
- Microsoft SetWindowDisplayAffinity: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- Microsoft RegisterHotKey: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- Microsoft Windows.Media.Ocr: https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr
- Microsoft Windows App SDK AI Text Recognition: https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition
- Microsoft WinUI 3: https://learn.microsoft.com/en-us/windows/apps/winui/winui3/

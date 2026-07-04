# Screenshot Utility Research Brief

Date: 2026-07-01

Status note 2026-07-03: this is original product research and clone-safety
context, not the current implementation state. See `../PROJECT-STATE.md` and
`../ROADMAP.md` for current reality.

Working product name: Octadock

Target platform: Windows 10/11 desktop.

This brief maps the public behavior of CleanShot X into a Windows-first, clone-safe feature inventory. CleanShot X is a Mac app, but the product idea is portable: capture something, dock it on screen, then copy, save, annotate, pin, OCR, record, or share it. We are using public feature pages, public URL scheme docs, public release notes, and official Microsoft API docs. We are not using proprietary code, assets, binaries, icons, private APIs, or exact visual design.

## Clone-Safe Boundary

Build:

- A Windows screenshot workflow with equivalent user jobs: capture, dock, copy, save, annotate, pin, OCR, record, and share.
- Original UI, interactions, naming, icons, colors, copy, settings layout, file format, and protocol names.
- Original implementation using public Windows APIs and open-source dependencies with compatible licenses.

Avoid:

- CleanShot branding, app name, icons, screenshots, marketing copy, trade dress, and cloud endpoint design.
- Decompilation, traffic interception, license bypassing, or reuse of proprietary files.
- Presenting this as CleanShot compatibility or a CleanShot replacement that copies their protected expression.

## Public Feature Surface To Recreate

### Core Capture

Feature surface:

- Area capture.
- Window capture.
- Fullscreen capture.
- Previous-area repeat capture.
- Self-timer capture.
- Scrolling capture.
- Horizontal scrolling capture.
- Crosshair, dimensions, and magnifier.
- Freeze-screen mode for hover states and moving content.
- Capture presets and aspect ratio locks.
- Multi-monitor awareness.

Windows implementation direction:

- Use `Windows.Graphics.Capture` for display/window capture where possible.
- Use DXGI Desktop Duplication as a fallback/advanced path for monitor frames.
- Use a transparent topmost selection overlay per monitor for area selection.
- Use `SetWindowDisplayAffinity(..., WDA_EXCLUDEFROMCAPTURE)` on Octadock windows so our shelf/overlays are excluded from supported Windows capture APIs.

### Bottom-Left Capture Dock

CleanShot's key workflow is a post-capture overlay: after capture, the new image/video appears as a small docked overlay in a screen corner. Public docs call this the Quick Access Overlay.

Observed behavior from public sources:

- Appears immediately after screenshots and recordings.
- Supports copy, save, annotate, upload, and pin actions.
- Supports drag and drop into other apps.
- Displays file information.
- Has adjustable position and size.
- Has configurable auto-close behavior.
- Supports multi-display behavior.
- Supports swipe gestures.
- Can restore recently closed overlay.
- Can show multiple recent items and save all.
- Has context menu actions like save as, scale high-DPI captures to 1x, flip, rotate, and copy without closing.

Octadock equivalent:

- Call this the Capture Shelf.
- Default anchor: bottom-left of the active monitor.
- Show newest capture as the active shelf item with action buttons.
- Stack older captures above or behind the newest item.
- Use Windows-style visuals, original icons, and original copy.

### Annotation Editor

Public feature surface:

- Crop with aspect ratios and edge snapping.
- Arrows, including multiple styles and curved arrows.
- Rectangle, filled rectangle, ellipse, line.
- Pixelate and blur.
- Spotlight/focus highlight.
- Counter step markers.
- Pencil with smoothing.
- Highlighter.
- Text tool with styles.
- Combine multiple images in one canvas.
- Editable project file.
- Background/padding tool.
- Resize, rotate, flip.
- Color picker and saved colors.
- Undo/redo, copy/paste objects, duplicate objects, canvas zoom.
- Smart highlighter.

Octadock equivalent:

- Use a vector annotation model on top of raster captures.
- Save editable project files as `.octadock` packages: `manifest.json`, original capture, derived previews, and vector object JSON.
- Export flattened PNG, JPEG, WebP, PDF, and optionally AVIF/HEIF if Windows codecs are installed.

### Floating/Pinned Screenshots

Feature surface:

- Pin screenshots to the screen.
- Always-on-top behavior.
- Adjust size and opacity.
- Precise keyboard nudging.
- Lock mode to interact with apps beneath the pinned image.
- Context actions such as copy, save, annotate, close all, hide all, and middle-click close.

Octadock equivalent:

- Call this Pinboards.
- Use borderless topmost WPF/Win32 windows.
- Use click-through mode with `WS_EX_TRANSPARENT` when locked.
- Each pinned capture supports opacity, scale, lock-through, close, copy, save, annotate, and keyboard nudge.

### Text Recognition

Feature surface:

- OCR from selected screen region, file, or shelf item.
- Copies extracted text to clipboard.
- Local/private by default.
- Output modes with or without line breaks.
- Future language detection and confidence review.

Windows implementation direction:

- Primary: Windows App SDK AI Text Recognition when hardware/API availability is acceptable.
- Fallback: `Windows.Media.Ocr` for MSIX/package-identity builds.
- Optional fallback: bundled Tesseract OCR for machines without supported Windows OCR availability.

### Screen Recording

Feature surface:

- Record full screen, window, or custom area.
- MP4 and GIF.
- Quality, FPS, and resolution controls.
- Microphone recording.
- System audio recording.
- Cursor visibility.
- Hide desktop clutter while recording.
- Recording timer.
- Click visualization.
- Keystroke visualization.
- Camera/webcam overlay.
- Pause/restart support.
- Crash recovery for recordings.
- Video editor: trim, quality, resolution, playback, volume, mute.

Windows implementation direction:

- Use `Windows.Graphics.Capture` frame stream for recording.
- Encode via Media Foundation, Win2D/Direct3D pipeline, or FFmpeg depending on implementation language and licensing decision.
- Capture microphone with WASAPI.
- Capture system audio with WASAPI loopback.
- Add cursor/click/keystroke overlays in composition before encoding.

### Cloud And Sharing

Feature surface:

- Upload captures and recordings.
- Get shareable links.
- Tags.
- Custom domain and branding.
- Password-protected links.
- Expiration/self-destruct.
- Team workflows.

Octadock equivalent:

- Do not clone CleanShot Cloud.
- V1 ships without hosted cloud.
- Build a provider interface for local folder, OneDrive folder, S3/R2, WebDAV, and self-hosted upload API.

### Capture History

Feature surface:

- Access recent captures.
- Restore captures.
- Delete captures.
- Filter by capture type.
- Store up to one month.
- Clear history.
- Include external files opened in the app.
- Open annotate and pin from history.

Octadock equivalent:

- Local capture library with SQLite metadata and file storage under `%LOCALAPPDATA%\Octadock`.
- Retention setting: disabled, 1 day, 7 days, 30 days, forever.
- Filters: screenshots, scrolling captures, recordings, OCR sources, pinned, annotated, uploaded.

### App Automation

CleanShot exposes command-like app control through a URL scheme. Octadock should expose both:

- `octadock://...` protocol URLs.
- `octadock.exe ...` CLI commands for PowerShell, AutoHotkey, Flow Launcher, PowerToys Run, and scripts.

Command categories:

- All-in-one capture HUD.
- Area, previous-area, fullscreen, and window capture.
- Self-timer.
- Scrolling capture.
- Pin.
- Record.
- OCR.
- Annotate.
- Add external file to shelf.
- History.
- Restore recently closed.
- Settings.

## Feature Priority

### P0: MVP That Feels Like The App

- Tray app with first-run setup.
- Global hotkeys.
- Area, window, fullscreen, previous-area capture.
- Capture Shelf docked bottom-left of active monitor.
- Shelf actions: copy, save, annotate, pin, discard, drag/drop.
- Simple annotation editor: crop, arrow, rectangle, text, blur/pixelate, highlighter, undo/redo, export.
- Floating pinned screenshots with resize, opacity, close, and lock-through.
- Local capture history with restore and delete.

### P1: Power-User Parity

- OCR from region and file.
- Scrolling capture.
- Background/padding tool.
- All-in-one capture HUD.
- Self-timer and freeze-screen capture.
- Screen recording MP4 with microphone and cursor setting.
- `octadock://` and `octadock.exe` automation.
- More annotation tools: counter, ellipse, line, pencil smoothing, spotlight, color picker.

### P2: Advanced Parity

- System audio recording.
- GIF recording.
- Click and keystroke overlays.
- Camera overlay.
- Video trim/compress.
- Horizontal scrolling capture.
- WebP/AVIF/HEIF output where supported.
- Cloud upload plugins.
- Tags, share links, expiring links, password-protected links.
- Team workflows.

## Recommended Windows Stack

- App: C#/.NET desktop app.
- UI shell: WPF for overlays, transparent windows, tray app, editor, and fast iteration.
- Optional later UI: WinUI 3 islands or separate settings app if Fluent polish becomes important.
- Capture: Windows.Graphics.Capture first, DXGI Desktop Duplication fallback for monitor frames.
- Capture exclusion: SetWindowDisplayAffinity with WDA_EXCLUDEFROMCAPTURE on Octadock-owned windows.
- Hotkeys: Win32 RegisterHotKey plus WM_HOTKEY message loop.
- Rendering/annotation: WPF DrawingVisual or SkiaSharp.
- Image pipeline: Windows Imaging Component, ImageSharp, or SkiaSharp depending on export needs.
- OCR: Windows App SDK AI Text Recognition or Windows.Media.Ocr, with Tesseract fallback if needed.
- Recording: Windows.Graphics.Capture + Media Foundation/WASAPI/FFmpeg decision after prototype.
- Storage: SQLite plus files in `%LOCALAPPDATA%\Octadock`.
- Distribution: MSIX optional, regular installer first if package identity blocks speed.

## Sources

- CleanShot X Features: https://cleanshot.com/features
- CleanShot X URL Scheme API: https://cleanshot.com/docs-api
- CleanShot X Changelog: https://cleanshot.com/changelog
- CleanShot X Pricing/FAQ: https://cleanshot.com/pricing
- Microsoft Windows.Graphics.Capture: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture
- Microsoft Screen Capture docs: https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture
- Microsoft Desktop Duplication API: https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api
- Microsoft SetWindowDisplayAffinity: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- Microsoft RegisterHotKey: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- Microsoft Windows.Media.Ocr: https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr
- Microsoft Windows App SDK AI Text Recognition: https://learn.microsoft.com/en-us/windows/ai/apis/text-recognition
- Microsoft WinUI 3: https://learn.microsoft.com/en-us/windows/apps/winui/winui3/

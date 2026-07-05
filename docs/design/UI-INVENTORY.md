# Octadock UI Inventory — Designer Handoff

Captured: 2026-07-05, from the live app after the **obsidian glass redesign**
(dark-first default theme, 2560×1440 @ 175 % scaling).
Screenshots live in [`ui/`](ui/). Every interactive surface in the product is
listed here with what it does, its entry points, and known pain points, so a
designer can propose a full redesign without reading code.

How the captures were made: the app was run with
`OCTADOCK_DISABLE_CAPTURE_EXCLUSION=1` (a dev escape hatch added for exactly
this purpose — Octadock windows normally hide themselves from screen captures)
and each window was extracted with `PrintWindow`/`BitBlt`. The tool script is
`scratchpad/ui-capture/Capture.ps1` from the 2026-07-05 session; re-run it any
time the UI changes.

## Design-system facts (current)

- **Palette ("obsidian glass")**: deep blue-black surfaces (`#0C1220` base,
  `#141E32` cards), brand teal accent (`#2DD4BF`) with a signature teal→cyan
  gradient (`Octadock.Brush.AccentGradient`) on primary actions, plus
  success/warning/danger status tokens. **Dark is the default theme** (v3
  settings migration); Light remains selectable and uses white cards on a cool
  tinted canvas with a WCAG-AA deep teal. Tokens are `Octadock.Brush.*` in
  `src/Octadock.App/Resources/Themes/{Shared,Dark,Light}.xaml` — every color a
  designer specs should map to one of these tokens.
- **Two visual families**:
  1. **Standard windows** (Settings, History, Clipboard, Text Tools, editor):
     native title bar (now theme-matched), card-based layout, pill nav.
  2. **Glass surfaces** (dock, shelf, pins, HUD, preview cards, pills,
     overlay): borderless dark translucent capsules/cards that float topmost.
- **Typography**: Segoe UI Variable; 20/16/14/13 px scale. Icons: Segoe MDL2
  glyphs (a few text-symbol leftovers remain in the annotation editor toolbar).
- Light + dark theme both exist; the screenshots show **light**. The glass
  family is always dark regardless of theme.

---

## 1. The Dock capsule (product's face)

![idle](ui/dock-idle.png) ![expanded](ui/dock-expanded-hover.png)

Always-on-screen pill, bottom-center of the active monitor, draggable, position
persists. **Idle**: breathing teal dot + wordmark. **On hover** it expands to
the full action row: teal status dot · Area capture · Window capture ·
Fullscreen · Scrolling capture · OCR · Read (aloud) · Record (red dot) ·
History (clock) · **Clip** (clipboard history, new) · File (open/preview) ·
Settings gear · Dictate (mic). Right-click is intentionally inert.

Pain points for redesign: 12 unlabeled/label-mixed actions in one row (icon +
text buttons mixed); no grouping hierarchy; no state feedback (e.g. recording
elapsed lives in a separate pill); tooltips are the only labels.

## 2. Capture Shelf (bottom-left stack)

![shelf](ui/capture-shelf.png)

Where captures land after a screenshot/recording. Card stack anchored
bottom-left: thumbnail, timestamp caption, icon actions (copy, save, annotate,
pin, discard), drag-out to Explorer/browser, context menu with more actions,
newest on top, restore-recently-closed. Auto-close and size are settings.

## 3. All-in-one HUD (`Ctrl+Shift+1`)

![hud](ui/all-in-one-hud.png)

Compact launcher card: six capture modes (Area, Window, Full, Scroll, OCR,
Record) + fixed-size / lock-aspect region controls. Draggable via its header,
Esc/× closes. Roadmap wants this to grow into a full command palette.

## 4. File preview cards (Quick Look)

![csv](ui/preview-card-csv.png) ![markdown](ui/preview-card-markdown.png)

Dark teal glass cards opened by `octadock open`, dock "File", shelf file-drop,
or Explorer "Open with". Chrome: file-type badge (MD/CSV/…, provider label
below), filename + full path, action cluster (copy content, copy path, save a
copy, pin image, open externally), close ×. Bodies: CSV → sortable/filterable
table with a stats footer; Markdown → rendered headings/lists/code/quotes
(new); JSON pretty-print, log tail, plain text, images, and a file-info
fallback. Click-away or Esc dismisses. Resizable from the corner.

## 5. Floating pins

![pin](ui/floating-pin-hover.png)

Any capture can be pinned as a topmost floating image. Hover reveals a toolbar:
copy, save, annotate, lock (click-through), opacity slider, close. Drag to
move, corner grips resize, arrow keys nudge. Pins persist across restarts.

## 6. Annotation editor

![editor](ui/annotation-editor.png)

Standard window. Top toolbar: tool buttons (select, crop, arrow, rectangle,
ellipse, line, text, highlighter, blur, pixelate, counter, freehand — currently
text-symbol glyphs, should be redesigned as a proper icon set), color swatches,
stroke width and font size sliders, undo/redo, copy/save/export. Canvas below
with the image; status bar at the bottom. Supports `.octadock` project files
with re-editable vector objects.

## 7. History window

![history](ui/history-window.png)

Search box + "show deleted" toggle + clear-history; date range pickers (the
DatePickers are still Win32-default styling — worst-themed control in the app);
filter chips (All, Screenshots, Recordings, OCR, Files, Annotated, Deleted);
thumbnail grid with type badges; action bar (Open/Annotate, Pin, Copy,
Copy Text for OCR rows, Save, Delete, Restore) and Load more.

## 8. Clipboard history window (new)

![clipboard](ui/clipboard-history-window.png)

Search box · "Watch clipboard" toggle · Clear all; filter chips (All, Text,
Images, Favorites); rows with kind icon/thumbnail, snippet, source app ·
window · relative time · seen-count; per-row copy / favorite-star / delete;
double-click restores to the clipboard; footer count + load more.

## 9. Text Tools window (new)

![text tools](ui/text-tools-window.png)

Left rail: 28 transforms grouped by category (Format, Decode, Encode, Case,
Hash, Time, Lines). Right: input pane → live result pane, with Paste input /
Clear / Use as input ↑ / Copy result. Errors show inline in red.

## 10. Settings window (12 tabs)

Pill nav rail on the left, card sections on the right, Close/Save footer.

| Tab | Screenshot | Contents |
| --- | --- | --- |
| General | ![](ui/settings-general.png) | launch at login, tray/taskbar icons, theme, dock toggle, crash reports |
| Shortcuts | ![](ui/settings-shortcuts.png) | editable hotkey rows for all 9 global chords, conflict flagging |
| Shelf | ![](ui/settings-shelf.png) | anchor, size, auto-close, restore, margin, max items |
| Capture | ![](ui/settings-capture.png) | default action, save dir, filename template, cursor, shadows, formats, timer |
| Annotate | ![](ui/settings-annotate.png) | editor defaults |
| Recording | ![](ui/settings-recording.png) | fps, quality, cursor; mic/system audio intentionally disabled (video-only build) |
| OCR | ![](ui/settings-ocr.png) | provider, output mode, language, availability report |
| Speech | ![](ui/settings-speech.png) | availability-labeled provider picker (Parakeet / Whisper / OpenAI), activation mode, live partials + auto-stop toggles, local model manager (download/delete), models, language, insertion mode, dictionary with syntax hint |
| History | ![](ui/settings-history.png) | enable, retention, clear |
| Clipboard | ![](ui/settings-clipboard.png) | watch toggle, keep images, cap (new) |
| Automation | ![](ui/settings-automation.png) | protocol/CLI toggles + example commands |
| Advanced | ![](ui/settings-advanced.png) | data locations, restore defaults |

## 11. Surfaces without screenshots (transient / interactive)

- **Tray icon + menu** (WinForms `ContextMenuStrip`): Capture Area/Window/
  Fullscreen/All Monitors/Previous, Scrolling, All-in-One, OCR, Read Aloud,
  Record + Record Area, Open a File…, Open History, Clipboard History, Text
  Tools, Restore Recently Closed, Show All Pins, Hide Dock,
  Settings, Pause Capture, About, Exit. Uses native Win32 styling — cannot be
  themed; a redesign should assume it stays native.
- **Region selection overlay**: full-screen dim + rubber-band rectangle,
  live dimension label, magnifier loupe, resize handles, Enter/Esc.
- **Window picker overlay**: per-monitor highlight rectangles over each window.
- **Recording pill** (top-center): red pulsing dot + mm:ss + pause/stop,
  draggable, follows active monitor.
- **Dictation pill** (bottom-center): pulsing teal dot + "Listening…"/"Transcribing…"
  + stop.
- **Scrolling-capture pill**: "Scroll now · N px stitched · Enter to finish".
- **Self-timer countdown pill**: 3-2-1 before delayed captures.
- **Toast notifications**: native Windows toasts ("Copied", "Video saved";
  "Video saved" is click-through to the file location).
- **First-run wizard** and **About** dialogs.

## 12. Redesign guidance (what the founder wants)

A 100 % visual overhaul is requested. Constraints that make it cheap to apply:
every color flows through the `Octadock.Brush.*` tokens, all main windows are
plain XAML with shared styles, and the glass surfaces are componentized. The
loudest specific complaints today: the mixed icon language, the Win32
DatePickers, and general lack of visual distinctiveness ("looks like default
WPF with a teal accent"). Deliverables that would slot in directly: a token
sheet (colors/spacing/type ramp), an icon set, and per-window mockups matching
the inventory above.

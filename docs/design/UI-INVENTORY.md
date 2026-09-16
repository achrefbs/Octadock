# Octadock UI Inventory — Designer Handoff

Captured: 2026-07-05, from the live app after the **obsidian glass redesign**
(dark-first default theme, 2560×1440 @ 175 % scaling).
Updated: 2026-07-23 — the all-in-one HUD, file preview cards, and floating
pins were removed from the product; the Dock, tray, Settings, and design
system sections below describe the shipped state. Screenshots live in
[`ui/`](ui/) (the 2026-07-05 set; removed surfaces are noted, not re-shot).
Every interactive surface in the product is listed here with what it does, its
entry points, and known pain points, so a designer can propose a full redesign
without reading code.

Privacy review, 2026-09-16: four screenshots containing clipboard text,
workstation details or background browser content were removed. Replacement
captures must use an isolated test profile, synthetic data and a neutral
background. Inspect the complete image before publishing, including content
visible through translucent windows.

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
- **Token and style foundation (2026-07-23)**: semantic brush aliases plus
  metric tokens — border thickness (`Octadock.Border.Thickness`), shadows
  (`Octadock.Shadow.*`), motion (`Octadock.Motion.*`, gated by the Windows
  reduced-motion preference), font sizes, and a Lucide icon ramp
  (12/14/16/20/24) — with shared styles (`Octadock.Style.Button`,
  `DangerButton.Filled`, `ListBox`/`ListBoxItem`, `StatusPill`,
  `DialogFooter`, `Text.Label`). The authoritative usage guide is the header
  comment in `src/Octadock.App/Resources/Themes/Shared.xaml`.
- **Two visual families**:
  1. **Standard windows** (Settings, History, Clipboard, Text Tools, editor):
     native title bar (theme-matched), shared ListBox styles, SectionCard
     layout, quiet empty states, danger-styled destructive actions.
  2. **Glass surfaces** (Dock, Shelf, pills, overlays): borderless dark
     translucent capsules/cards that float topmost, unified on one glass token
     set with reduced-transparency and reduced-motion gating.
- **Typography**: Segoe UI Variable; `Octadock.FontSize` ramp (Display 28 /
  Title 20 / Section 16 / Subtitle 14 / Body 13 / Caption 12 / Micro 11).
  Icons: Lucide (`PackIconLucide`) everywhere, including the annotation editor
  toolbar, which also carries automation names.
- **Destructive confirmations**: one themed `ConfirmationDialog`
  (default-Cancel) covers Shelf permanent delete, clear clipboard, clear
  history, and delete Context.
- Light + dark theme both exist; the 2026-07-05 screenshots show **light**.
  The glass family is always dark regardless of theme.

---

## 1. The Dock capsule (product's face)

![idle](ui/dock-idle.png) ![expanded](ui/dock-expanded-hover.png)

Always-on-screen pill, bottom-center of the active monitor, draggable, position
persists. **Minimal structure (2026-07-23)**: a split **Capture** button (the
primary half starts an Area capture; the chevron opens a menu with Window,
Full screen, All monitors, Previous area, Timer, Scrolling — manual vertical
(Beta), OCR, and Record), then **Dictate**, **Shelf**, **History**, and a
**More** menu (Clipboard history, Context, Settings, Account/About, Exit).
Right-click is intentionally inert.

Pain points for redesign: recording/dictation elapsed state still lives in
separate pills; tooltips are the only labels.

## 2. Capture Shelf (bottom-left stack)

![shelf](ui/capture-shelf.png)

Where captures land after a screenshot/recording — Octadock captures and
recordings only (arbitrary file drops were removed 2026-07-23). Card stack
anchored bottom-left: thumbnail, timestamp caption, icon actions (copy, save,
annotate, discard), drag-out to Explorer/browser, context menu with more
actions, newest on top, restore-recently-closed. The × **dismisses only** the
card; a separate **Delete permanently…** route asks the themed default-No
ConfirmationDialog and then deletes file + History row. Auto-close and size
are settings.

## 3. Annotation editor

![editor](ui/annotation-editor.png)

Standard window, and the only image surface: it opens Octadock capture images
and capture-derived `.octadock` projects (arbitrary-file/clipboard entries
were removed 2026-07-23; missing/corrupt sources fail visibly). Top toolbar:
tool buttons (select, crop, arrow, rectangle, ellipse, line, text,
highlighter, blur, pixelate, counter, freehand — now one Lucide icon language
with automation names), color swatches, stroke width and font size sliders,
undo/redo, copy/save/export. Canvas below with the image; status bar at the
bottom. Supports `.octadock` project files with re-editable vector objects.
Opening from Shelf/History records an honest `Annotated` action.

## 4. History window

![history](ui/history-window.png)

Search box + "show deleted" toggle + clear-history; date range pickers (the
DatePickers are still Win32-default styling — worst-themed control in the app);
filter chips (All, Screenshots, Recordings, OCR, Files, Annotated, Deleted);
thumbnail grid with type badges; action bar (Open, Annotate for image
captures, Copy, Copy Text for OCR rows, Save, Delete, Restore) and Load more.
Pin/preview actions were removed 2026-07-23.

## 5. Clipboard history window

Search box · "Watch clipboard" toggle · Clear all; filter chips (All, Text,
Images, Favorites); rows with kind icon/thumbnail, snippet, source app ·
window · relative time · seen-count; per-row copy / favorite-star / delete;
double-click restores to the clipboard; footer count + load more.

## 6. Text Tools window

![text tools](ui/text-tools-window.png)

Left rail: 28 transforms grouped by category (Format, Decode, Encode, Case,
Hash, Time, Lines). Right: input pane → live result pane, with Paste input /
Clear / Use as input ↑ / Copy result. Errors show inline in red.

## 7. Settings window (sectioned)

Pill nav rail on the left, card sections on the right, Close/Save footer.
Primary categories (Capture, Voice, Library, System) group the pages below;
`open-settings` deep links still land on the right page.

| Page | Screenshot | Contents |
| --- | --- | --- |
| General | ![](ui/settings-general.png) | launch at login (default on for new profiles, first-run opt-out), tray/taskbar icons, theme, dock toggle, crash reports |
| Shortcuts | ![](ui/settings-shortcuts.png) | editable hotkey rows; only Area `Ctrl+Shift+4`, Full screen `Ctrl+Shift+3`, Dictate `Ctrl+Shift+2` assigned by default, the rest unassigned |
| Shelf | ![](ui/settings-shelf.png) | anchor, size, auto-close, restore, margin, max items |
| Screenshots — Basic | ![](ui/settings-capture.png) | save folder, image format, cursor, selection behavior incl. freeze-screen (default on) and Precision aids (dimensions + magnifier, default on) |
| Screenshots — Advanced | ![](ui/settings-capture.png) | filename template, JPEG quality, window shadow/frame, monitor behavior, fixed size + aspect ratio, timer, manual vertical scrolling (Beta) |
| Annotate | ![](ui/settings-annotate.png) | editor defaults |
| Recording | ![](ui/settings-recording.png) | fps, quality, cursor; microphone (WASAPI) and system/app (loopback) audio opt-ins, default off (Beta) |
| OCR | ![](ui/settings-ocr.png) | provider, output mode, language, availability report |
| Speech | ![](ui/settings-speech.png) | availability-labeled provider picker (Parakeet / Whisper / OpenAI env-key), microphone picker, activation mode, live partials + auto-stop toggles, insertion mode incl. review-before-insert, model consent (provider/size/storage location), model manager with cancel/retry/delete, language, dictionary with syntax hint |
| History | ![](ui/settings-history.png) | enable, retention, clear |
| Clipboard | ![](ui/settings-clipboard.png) | watch toggle, keep images, cap |
| Automation | ![](ui/settings-automation.png) | protocol/CLI toggles + example commands |
| Advanced | ![](ui/settings-advanced.png) | data locations, restore defaults |

## 8. Surfaces without screenshots (transient / interactive)

- **Tray icon + menu** (WinForms `ContextMenuStrip`, 2026-07-23 regrouping):
  Capture submenu (Area/Window/Full screen/All monitors/Previous area/Timer/
  Scrolling/OCR/Record), Dictate, a library group (Shelf, History, Clipboard
  history, Text tools, Context), and an app group (Settings, Account/About,
  Exit). Left-click toggles the Shelf. Pause/Resume, "Open a File…", and the
  pin commands were removed. Uses native Win32 styling — cannot be themed; a
  redesign should assume it stays native.
- **Region selection overlay**: full-screen dim + rubber-band rectangle,
  live dimension label and magnifier loupe (Precision aids, default on),
  freeze-screen (default on), resize handles, Enter/Esc.
- **Window picker overlay**: per-monitor highlight rectangles over each window.
- **Recording pill** (top-center): red pulsing dot + mm:ss + pause/stop,
  draggable, follows active monitor.
- **Dictation pill** (bottom-center): distinct phases for the controller
  states (Preparing / Listening / Transcribing / Inserting / review / failed),
  live partials, discard; the review-before-insert mode shows a review panel
  with the transcript before anything is pasted.
- **ConfirmationDialog**: one themed, default-Cancel dialog for destructive
  flows (Shelf permanent delete, clear clipboard, clear history, delete
  Context).
- **Scrolling-capture pill**: "Scroll now · N px stitched · Enter to finish".
- **Self-timer countdown pill**: 3-2-1 before delayed captures.
- **Toast notifications**: native Windows toasts ("Copied", "Video saved";
  "Video saved" is click-through to the file location).
- **First-run wizard** and **About** dialogs.

## 9. Removed surfaces (2026-07-23)

Removed from the product by owner decision; listed here so older screenshots
are not mistaken for current scope:

- **All-in-one HUD** (`Ctrl+Shift+1` launcher card) — replaced by the Dock
  split Capture button + streamlined tray + shortcuts; the `all-in-one`
  command is a tombstone.
- **File preview cards** (CSV/Markdown/JSON/log/text/metadata quick-look) —
  feature and providers deleted; `open` is a tombstone.
- **Floating pins / image surface** (topmost image, quick pen, inline AI
  edit, Gather) — deleted; `pin` is a tombstone; the annotation editor is the
  only image surface.

## 10. Redesign guidance (what the founder wants)

A 100 % visual overhaul is requested. Constraints that make it cheap to apply:
every color flows through the `Octadock.Brush.*` tokens, all main windows are
plain XAML with shared styles, and the glass surfaces are componentized. The
2026-07-23 design-system pass already landed the token/style foundation (see
the `Shared.xaml` usage guide) and one Lucide icon language; the loudest
remaining complaints are the Win32 DatePickers and general lack of visual
distinctiveness. Deliverables that would slot in directly: per-window mockups
matching the inventory above and rendered mixed-DPI evidence.

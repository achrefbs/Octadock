# Proposal — Quick Look–style file previews (starting with CSV)

Status: partially implemented · updated 2026-07-03. The active state is in
`../PROJECT-STATE.md`; the active roadmap is in `../ROADMAP.md`.

Implemented so far: `octadock open --filepath <path>`, file associations routed
through that command, CSV/TSV previews, text/code/config previews, image
previews, a generic file-info fallback, CSV filtering/sorting/stats, and UNC
rejection.

Not implemented yet: drag-a-file-onto-shelf preview entry, true source-rect
Quick Look animation, protocol confirmation UI, preview export/pin actions, and
Ask AI.

Octadock is growing from a capture tool into a **vibe-coding toolkit**. A natural next
capability is opening a file into a fast, dismissible preview card — the same instinct
as macOS Quick Look (select a file, tap space, it zooms open; tap space again, it's
gone). This proposal covers the interaction, the open animation options, and how it
slots into the existing architecture. There is an interactive mockup accompanying this
doc — try the four animation styles there.

## The interaction in one line

Trigger a preview → a card **scales open from the file's on-screen position** → you
read / copy / act → `Esc` or click-away → it **scales back into the icon** and
disappears. No new window in the taskbar, no app switch, no waiting.

## Open-animation options (pick one default, keep the rest as settings)

Four candidates, all doable with pure WPF `Storyboard`s (no third-party libs). The
mockup lets you feel each one.

1. **Quick Look zoom (recommended default).** The card scales up from ~12% at the
   file's icon location to full size at center, with a quick opacity fade and a scrim
   behind it. This is the "Mac animation" you're picturing. It reads as *this card came
   from that file*, which is exactly the mental model we want. Easing:
   `CubicEase EaseOut`, ~340 ms.
2. **Genie.** Like Quick Look but the transform origin is the bottom edge and there's a
   subtle vertical stretch, so it appears to get "sucked" out of the file. Flashier;
   nice for a hero moment but can feel heavy if you open files all day.
3. **Card slide-up.** The card fades in and rises ~46 px into center. Calm, fast,
   direction-agnostic — a safe choice that never looks wrong regardless of where the
   file icon is. Good fallback when we can't determine the source rect (e.g. opened
   from the CLI).
4. **Spotlight.** Scrim dims, card cross-fades and scales 96%→100% in place. Minimal
   and focus-pulling. This is also the automatic **reduced-motion** fallback
   (respect `SystemParameters` / prefers-reduced-motion).

Recommendation: ship **Quick Look zoom** as default, fall back to **slide-up** when
there's no source rect, and use **spotlight** under reduced-motion. Expose the choice in
Settings → Appearance.

### How the zoom-from-icon works in WPF

The card is a borderless `ToolWindowBase` (or an in-shelf overlay). On open, capture the
source element's screen rect (the shelf card, the file-picker row, or the cursor point
for a hotkey/CLI open). Set the card's `RenderTransform` to a `TransformGroup`
(`ScaleTransform` + `TranslateTransform`) whose initial values place it over the source
rect, then animate scale→1 and translate→0 with a `Storyboard`. `RenderTransformOrigin`
= center for Quick Look, `0.5,1` for Genie. Close plays the same storyboard reversed
(`AutoReverse` or a second board) then hides the window.

## Entry points (how a file gets opened)

Reuse the automation surface Octadock already has, so previews are triggerable the same
ways captures are:

- **Drag a file onto the shelf** — planned; current shelf mostly supports
  dragging items out, while `add-shelf-item` can add external files through
  automation.
- **`octadock://open?filepath=…`** protocol + **`octadock open --filepath <path>`**
  CLI — implemented, parsed by the existing `CommandParser`, dispatched by
  `CommandDispatcher`.
- **Global hotkey → file picker** — e.g. a "Quick open" chord that shows an `OpenFileDialog`
  (or, later, a fuzzy launcher) and previews the result.
- **Windows file association / "Open with Octadock"** — implemented as per-user
  `Octadock.Preview` registration refreshed at startup.
- **Clipboard path** — if the clipboard holds a file path, offer "Preview in Octadock".

## The CSV card specifically

The CSV preview is the first provider and the richest. Contents:

- **Header + typed table.** Parse with a small, dependency-light reader (handle quoted
  fields, embedded commas/newlines, CRLF, and a configurable delimiter — auto-detect
  `,` `;` `\t`). Render as a virtualized `DataGrid`/`ItemsControl` so a 100k-row file
  stays smooth. Row numbers, sticky header, zebra rows, monospace figures.
- **Column tools.** Click a header to sort; a filter box to narrow rows; right-align
  numeric columns automatically.
- **A stats bar.** For the focused/selected numeric column show count, sum, mean,
  min/max — the thing you actually opened the CSV to find.
- **Actions in the card chrome.** Copy (TSV to clipboard, reusing `IClipboardService`),
  Export → `.xlsx` (hand off to the spreadsheet path), Pin (reuse `PinService` to keep
  the card floating), and **"Explain this file"** which sends the header + a sample to a
  local/hosted model for a natural-language summary or lets you ask a question over the
  data. Pinning + ask-over-data are the features that make this feel like a *toolkit*,
  not a viewer.
- **Big-file guardrails.** Stream the first N rows immediately, load the rest in the
  background, and surface "showing 1–500 of 248,000".

## Architecture fit

Introduce one abstraction and a provider per type:

```
IFilePreviewProvider
  bool CanPreview(string extension)
  Task<FilePreviewResult> LoadAsync(string path, CancellationToken ct)

FilePreviewService  → picks a provider by extension, opens the card window
  CsvPreviewProvider     (this proposal)
  JsonPreviewProvider    (formatted + collapsible)
  MarkdownPreviewProvider(rendered)
  ImagePreviewProvider   (reuse existing imaging; also feeds OCR + annotate)
  Text/LogPreviewProvider(monospace + tail/follow)
  CodePreviewProvider    (syntax highlight)
```

The card window is a themed `ToolWindowBase`, so it inherits capture-exclusion, the DWM
dark-titlebar work (once added), theming, and the pin/close plumbing. Because previews,
pins, and the shelf already share infrastructure, "pin this preview" and "annotate this
image preview" come almost for free.

## Suggested phasing

1. `FilePreviewService` + `CsvPreviewProvider` + the Quick Look card and animation, wired
   to drag-onto-shelf and `octadock open`.
2. Sorting/filter/stats + Export to xlsx + Pin.
3. JSON, Markdown, image, text/log providers.
4. "Ask over this file" (ties into the STT/LLM roadmap) and a fuzzy Quick-open launcher.

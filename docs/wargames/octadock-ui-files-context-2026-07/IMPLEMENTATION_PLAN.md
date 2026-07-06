# Implementation Plan to Wargame

## Product Rule

Capture Shelf is for things the user just captured. Context Shelf is for things
the user is intentionally preparing to use, export, share, or hand to another
tool.

## Phase 1: UI Foundation and Capture Shelf Redesign

- Create shared UI tokens for dark surfaces, borders, teal accent, typography,
  row heights, thumbnails, icon buttons, action rails, and inspector panels.
- Replace current shelf cards with compact rows inspired by
  `reference-images/04-shelf-row-panel.png`.
- Add stable row layouts for screenshot, window capture, OCR text, recording,
  external image, and future external file.
- Preserve existing actions: copy, save, annotate, pin, discard, drag-out,
  restore.
- Add actions: add to Context Shelf, OCR/read status, richer more-menu.
- Refresh pinned/media viewer using `reference-images/01-pinned-media-viewer.png`.

Acceptance:

- Tiny screenshots no longer create visually awkward oversized cards.
- Long filenames do not overlap actions.
- Keyboard focus and hover states are visible.
- Shelf remains fast and lightweight.

## Phase 2: Universal File Open Shell

- Introduce a universal file router that can open any local file path.
- For supported rich types, route to a rich provider.
- For unsupported types, show a safe metadata/fallback preview.
- Keep provider loading async and cancellable.
- Add preview tabs/panels: Preview, Metadata, Text/OCR, Annotations, Context.
- Expand provider model beyond current `FilePreviewKind` as needed.

Priority rich preview order:

1. PDF.
2. Office documents, spreadsheets, and presentations.
3. Archives, starting with zip.
4. SVG and other easy design-ish formats.
5. Video/audio later.
6. PSD/AI/Figma/Sketch only after investigation.

Acceptance:

- Any file opened through CLI, Explorer, drag/drop, or app UI produces either a
  rich preview or a useful fallback card.
- Unknown files are never treated as errors merely because they lack a provider.
- Macro/script execution is never required for preview.

## Phase 3: Explorer Integration

- Add Settings > Files.
- Add toggles for:
  - Show Octadock in Open With for supported file types.
  - Show Octadock in Open With for all files.
  - Add Explorer verb: Open in Octadock.
  - Add Explorer verb: Add to Context Shelf.
  - Add Explorer verb: Add image to Capture Shelf.
- Keep all registration per-user under HKCU.
- Do not automatically seize default app associations.
- Add unregister/repair controls.

Acceptance:

- User can enable/disable all Explorer integration.
- Existing registration is repaired after app path moves.
- Context verbs work on arbitrary file selections.

## Phase 4: Context Shelf

- Add persistent domain model: ContextShelf, ContextShelfItem, ContextPackage.
- Supported item types: captures, OCR text, clipboard text, files, file excerpts,
  image annotations, notes/prompts, archive entries later.
- Add "Add to Context Shelf" from Capture Shelf, History, File Preview,
  Annotation Editor, Clipboard/OCR results, and Explorer right-click.
- Add separate Context Shelf panel/window with ordered rows, source badges,
  thumbnails, include/exclude toggles, notes, rename, remove, and reorder.
- Export context packages as:
  - `context.md`
  - `manifest.json`
  - `attachments/`
  - `thumbnails/`
  - `ocr/`
  - `annotations/`

Acceptance:

- User can build a context bundle from mixed captures and files.
- User can copy as Markdown or export a folder package.
- Context Shelf persists across app restarts.
- AI discovery/sessions are not reintroduced.

## Phase 5: Direct Editing and Annotation

- Keep existing image annotation and redesign its toolbar.
- Directly save safe edits to original files when supported.
- Create automatic backup/revision before overwrite.
- Add clear file state labels: Editing original, Editing copy, Annotation layer
  only, Read-only.
- Add PDF annotation/writeback after provider investigation.
- Add Office annotation/comment/highlight/writeback after provider investigation.
- For unsupported or risky formats, offer annotated copy/export or sidecar layer.

Acceptance:

- Users understand whether they are editing an original or a copy.
- No writeback happens without a recoverable backup.
- Failed saves never corrupt the original.

## Phase 6: Archives and Packages

- Add zip preview with tree/list view, metadata, extract selected, extract all.
- Add Context Shelf package export as folder and zip.
- Later: edit files inside archives with backup.

Acceptance:

- Zip files can be opened, browsed, and extracted.
- Context packages can be zipped for sharing.

## Phase 7: History, Command Deck, and HUD Polish

- Redesign History around the library/inspector concept in
  `reference-images/03-history-library-inspector.png`.
- Redesign Command Deck using the structure in
  `reference-images/02-command-deck-shelf-files.png`, but replace AI Sessions
  with Capture Shelf, Context Shelf, Files, OCR, and Recent Activity.
- Redesign Capture HUD around `reference-images/05-capture-hud.png`.

Acceptance:

- The app feels like one coherent product.
- No abandoned AI Session panels remain in primary UI.


# Project Context

## Product

Octadock is a Windows desktop app centered on fast capture, local history,
annotation, file preview, pinning, OCR, speech/read tooling, and future context
packaging.

The current redesign effort focuses on:

- Full UI polish, especially the Capture Shelf card/list experience.
- A new separate Context Shelf.
- Opening all file types with safe fallback previews.
- Rich previews for PDF, Office, and archives.
- File annotation and direct writeback where safe.
- Explorer integration for Open With and right-click verbs.

## Current Code Status

Current baseline: `origin/main` at commit `034fe69`.

Verified from code:

- Capture Shelf exists.
  - Main files: `src/Octadock.App/CaptureUx/ShelfService.cs`,
    `ShelfViewModel.cs`, `ShelfItemViewModel.cs`, `ShelfItemView.xaml`,
    `ShelfWindow.xaml.cs`.
  - It is a live stack of capture cards with copy, save, annotate, pin, discard,
    drag/drop, and restore behavior.
  - External add-to-shelf currently accepts supported raster images only. Other
    files trigger the warning: "Only image files can be added to the dock right
    now."

- Context Shelf is not implemented.
  - The phrase exists in roadmap/spec docs only.
  - There are no `ContextShelf` classes or tests in `src`/`tests`.

- File preview is partial.
  - Registered providers: CSV/TSV, JSON, log, Markdown, text-like files, raster
    images.
  - Unsupported types use a `FilePreviewKind.FileInfo` fallback with metadata and
    open/reveal/copy style actions.
  - Main files: `src/Octadock.App/Preview/FilePreviewService.cs`,
    `PreviewCardWindow.cs`, `ImagePreviewProvider.cs`,
    `src/Octadock.Core/Abstractions/FilePreviewAbstractions.cs`, and provider
    classes under `src/Octadock.Core/Services/`.

- File associations are partially built and currently always-on at startup.
  - Main file: `src/Octadock.Platform.Windows/System/FileAssociationRegistration.cs`.
  - App startup calls registration in `src/Octadock.App/App.xaml.cs`.
  - It advertises Octadock in Open With for previewable text/code/data/image
    extensions.
  - It adds "Add to Octadock dock" only for image extensions.
  - There is no user-facing settings toggle yet.
  - It does not yet provide universal all-file verbs such as "Add to Context
    Shelf".

- Image annotation exists.
  - Main files: `src/Octadock.App/Editing/AnnotationEditorWindow.xaml`,
    `EditorViewModel.cs`, `EditorTool.cs`, `EditorCanvas.cs`,
    `AnnotationRenderer.cs`, `EditorExporter.cs`.
  - Current tools include select, crop, arrow, rectangle, ellipse, line, text,
    highlighter, blur, pixelate, counter, and freehand.
  - Editable projects are saved as `.octadock` packages.
  - Annotation is currently image/raster oriented, not full PDF/Office/file
    annotation.

- Pinned media viewer exists and overlaps with the desired visual direction.
  - It already supports core actions like copy, save, annotate, lock, opacity,
    and close.
  - It needs visual and metadata polish to match the reference image.

- AI session discovery has been dropped.
  - AI Sessions code has been removed from the latest main.
  - Related documentation was moved to `docs/archive/`.
  - Do not include AI discovery or AI Sessions in the new plan.

## User Decisions Already Made

- Capture Shelf and Context Shelf must be separate features.
- Context Shelf should be persistent, not merely temporary clipboard history.
- Unknown file types can use fallback preview while rich previews phase in.
- Explorer integration should include right-click verbs.
- Octadock should not silently become the default app for every file type.
- Direct editing/writeback is desired when safe.
- Automatic backups/revisions before overwriting originals are acceptable.
- Highest priority rich previews: PDF, Office, archives.
- Video/audio are later priority.
- Design files are exploratory; SVG first, PSD/AI/Figma/Sketch carefully later.
- Context package format should be a folder with `context.md`,
  `manifest.json`, attachments, thumbnails, OCR, and annotations.

## Design Direction From Reference Images

- Dark glass utility UI with teal focus/accent.
- Dense but organized layouts; no marketing-style hero pages.
- Row-first Capture Shelf, not large awkward cards.
- Strong thumbnail + metadata + action rail pattern.
- Inspector panels for history/files/context.
- Media-forward pin/viewer surface with top toolbar and bottom metadata strip.
- Capture HUD with large mode tiles and clear readiness states.
- Command Deck can use the structural idea from the reference, but not AI
  Sessions.


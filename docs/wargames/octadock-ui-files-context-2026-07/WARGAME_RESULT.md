# Wargame Result — Octadock UI, Files, and Context Shelf

Run: 2026-07-06 · Baseline: `origin/main` @ `034fe69` · Method: `WARGAME_PLAN.md` roles/scenarios · Evidence: direct code inspection of the baseline (citations inline)

## Executive Summary

- **Overall verdict:** Pass with changes. The phase sequencing and product rules are sound; no scenario invalidates the product direction. Five P0 revisions are required before engineering starts — four are plan gaps, one (Explorer multi-select acceptance) is a platform impossibility as written.
- **Biggest risk:** Data loss via writeback. The plan promises "no writeback without a recoverable backup," but today **no user-file save path in the codebase is atomic or backed up** — every save is a direct overwrite (`ShelfItemViewModel.cs:199`, `HistoryViewModel.cs:472`, `PinWindow.xaml.cs:400`, `FilePreviewService.cs:267`, `CaptureCoordinator.cs:558,734`). The safety layer is scheduled last (Phase 5) while save actions ship from Phase 1, and the plan never specifies mechanism, storage, retention, or a restore UI.
- **Biggest UX ambiguity:** The Capture Shelf / Context Shelf boundary. Two similarly-styled "shelves," **no reference image depicts the Context Shelf at all**, and the plan never defines what "add to Context Shelf" actually does to the bytes (copy vs reference) — which is simultaneously the biggest unresolved domain-model decision.
- **Biggest engineering trap:** Explorer verbs on multi-select. Static HKCU verbs invoke the command **once per selected file**, and Windows suppresses static verbs above the 15-item `MultipleInvokePromptMinimum` default — so "Context verbs work on arbitrary file selections" cannot be met with the planned registration approach. Runner-up: the PDF/Office render+writeback dependency decision (no good free option; AGPL trap), currently hidden inside Phases 2/5 as "after investigation."
- **Recommended v1 scope change:** Writeback = raster images only. PDF/Office = preview + annotate-on-copy/sidecar (signed/encrypted/protected always forced to copy). Explorer bulk-add = drag-drop; right-click verbs coalesce ≤15 files. Command Deck redesign moves to v2; HUD/History refresh trail as should-ship.
- **What already holds up (verified in code):** single-instance IPC exists with per-user pipe ACL (`SingleInstanceGuard.cs:142-147,195-200` uses `CurrentUserOnly`); a versioned migration runner with corruption quarantine + salvage exists (`SchemaMigrations.cs:14-28`, `OctadockDatabase.cs:135-235`); preview loading is already async + cancellable with row caps (`FilePreviewService.cs:112-117`); settings-synced registration patterns already exist to model Phase 3 on (`App.xaml.cs:104-105`); scrolling capture exists (`ScrollingCaptureEngine.cs`), so the HUD reference's Scrolling tile is honest.

## Top Plan Changes

| Priority | Change | Why | Owner | Confidence |
| --- | --- | --- | --- | --- |
| P0 | Define Context Shelf item **ownership semantics**: snapshot items into managed storage by default (the pattern external shelf-adds already use, `CaptureCoordinator.cs:558`); reference-with-verify for files above a size threshold; per-item "materialize now"; bundle items must survive capture discard/retention | Undefined copy-vs-reference blocks the Phase 4 schema and silently breaks bundles when sources move or captures are discarded | Product/Engineering | High |
| P0 | Build a shared **atomic-save + revision layer in Phase 1–2**, not Phase 5: temp-in-same-dir + `File.Replace` (backup) → central revision store → visible restore UI → retention policy; route every user-file write through it | Plan promises recoverable backups but all current saves are direct overwrites; the temp+rename idiom exists only for internal files (`OctadockProjectSerializer.cs:102`); a backup no user can find is not a backup | Engineering | High |
| P0 | Rewrite the Explorer multi-select design + acceptance: v1 = static verbs, app-side **coalescing window** for per-file invocations, acceptance capped at ≤15 items, drag-drop positioned as the bulk path; spike `IExplorerCommand` COM handler for v2 | Static verbs fire once per file and are suppressed >15 selected items — the current acceptance criterion is unachievable on the platform | Engineering | High |
| P0 | Lock the v1 **writeback matrix**: images only; PDF/Office annotate → copy or sidecar; signed, encrypted, permission-restricted PDFs and macro-enabled Office files never writeback; PDF render library + license budget decided before Phase 2 starts | Prevents signature/form destruction, trust damage, and an unbounded mid-phase dependency spike | Product/Engineering | High |
| P0 | **Executable guard** in fallback preview and the "Open in Octadock" verb: no one-click Open/ShellExecute for executable/script types (.exe .bat .cmd .ps1 .vbs .js .msi .scr .lnk …); Reveal-in-Explorer becomes primary; explicit confirm to launch | The universal router plus an all-files verb otherwise turns "preview" into a launcher for hostile binaries | Engineering/Design | High |
| P1 | Add an **export preview step** to Phase 4: manifest tree with per-item *and per-derivative* (OCR / thumbnail / annotation) include toggles, size estimate, relative paths by default | Scenario 12's redaction workflow doesn't exist in the plan; OCR text is the stealth leak — excluding an image must exclude its OCR everywhere | Design/Engineering | High |
| P1 | **Migration hardening**: file-copy backup of `octadock.db` after WAL checkpoint and before applying v7+ migrations; explicit downgrade guard when `user_version` > known; first-run adoption of existing association registrations into the new toggles (don't silently unregister, don't present as user-chosen) | Migration runner exists (`SchemaMigrations.cs`, currently v6) but has no pre-upgrade backup; this machine has documented WAL-corruption history; Context bundles are irreplaceable curated data | Engineering | High |
| P1 | Produce a **Context Shelf design mock before Phase 4** and resolve naming; give it a visibly distinct identity (header, accent, badge) from Capture Shelf | The plan mandates following the reference images, but no reference depicts the Context Shelf, its toggles, badges, or export flow | Design | High |
| P1 | **Action-rail policy**: max 4–5 visible icons + overflow menu; extension-preserving middle truncation for filenames; keyboard (roving focus) and screen-reader names for icon-only rails | Ref 04 already shows 5 icons and truncates "Product-Roadmap.p..." (extension lost); the plan adds 2+ more actions per row | Design | High |
| P1 | **Zip safety spec** in Phase 6: canonicalize extraction paths against the target root (zip-slip), entry-count/uncompressed-size caps before extract-all, encrypted-entry detection (`System.IO.Compression` cannot decrypt — show locked state), long-path (`\\?\`) handling | Phase 6 lists features with no guardrails; extraction traversal is an arbitrary-file-write primitive | Engineering | High |
| P1 | **Cloud-placeholder policy**: never hydrate OneDrive/Files-On-Demand placeholders for preview, metadata, hashing, or thumbnails (attribute check + no-recall open flags); explicit "download to include" decision at ingest/export | The universal router and batch ingest will otherwise trigger surprise multi-GB downloads | Engineering | Medium |
| P2 | **Pinned viewer compact mode**: ref 01's toolbar (7 controls) needs ~640 px; below that, collapse to icon-only + overflow; keep existing nudge/resize/middle-click-close behaviors (`PinWindow.xaml.cs:204-266`) | Tiny captures are Scenario 1's core case and the reference only shows the 1920×1080 happy path | Design | High |
| P2 | **Style-token fallbacks**: solid-surface variants for transparency-off/battery-saver/RDP, high-contrast mode support, metadata-text contrast ≥ 4.5:1, per-monitor-DPI (PMv2) audit for shelf/pin/HUD windows | Dark-glass styling in the references degrades exactly where utility apps live: remote sessions, low power, accessibility modes | Design/Engineering | Medium |

## Scenario Results

### 1. Tiny Screenshot Capture

- **Verdict:** Pass with changes
- **User expectation:** A 200×80 snip lands as a readable compact row; copy/save/annotate/pin are one click; pinning it gives a usable floating window.
- **Planned behavior:** Phase 1 rows per ref 04 (fixed-height row, contained thumbnail box) directly fix the oversized-card acceptance.
- **Failure modes:** [Designer] extreme aspect ratios (3840×60 ticker snip) render as an illegible sliver in a ~96×64 thumb box. [Power User] plan adds add-to-context + OCR status + more-menu to an already 5-icon rail → overflow or shrinking targets. [Casual] hover-only rails (as in ref 04) hide actions until discovered. [QA] pinning a tiny capture: ref 01's toolbar cannot fit at small widths. [Designer] truncation in ref 04 eats extensions ("Product-Roadmap.p...").
- **Required changes:** Rail policy + truncation rule (Top Changes #9); letterbox + hover-zoom for extreme aspects; pinned compact mode (#12); actions visible on row focus, not hover-only.
- **Severity:** P1 (rail/truncation), P2 (rest) — **Confidence:** High

### 2. Capture to Context

- **Verdict:** Pass with changes
- **User expectation:** Add two of three captures to Context Shelf, annotate with notes, export a folder that contains exactly what was shown.
- **Planned behavior:** Phase 4 add-from-shelf, notes, reorder, folder export with `context.md`/`manifest.json`/attachments/thumbnails/ocr/annotations.
- **Failure modes:** [Product Owner] what "add" does to bytes is undefined — if it references the capture record and the user later discards/retention-purges the capture, the bundle silently loses content (soft-delete exists: `captures.deleted_at`, `SchemaMigrations.cs:33-49`). [Casual] two shelf-named surfaces with the same row styling; no reference image to even design against. [Privacy] no preview of what `context.md` will contain before export. [QA] duplicate adds of the same capture; ordering semantics in `context.md` unstated. [Power User] "copy as Markdown" clipboard semantics (paths? embedded?) undefined pre-export.
- **Required changes:** Ownership semantics (#1); export preview (#6); Context Shelf mock + distinct identity (#8); "in context" badge on capture rows; define copy-as-Markdown as links relative to the exported/updated package, plain text otherwise.
- **Severity:** P0 (ownership), P1 (preview, design) — **Confidence:** High

### 3. Open Unknown File

- **Verdict:** Pass with changes
- **User expectation:** Right-click → Open in Octadock on an unknown binary shows something useful, safe, and honest.
- **Planned behavior:** Router falls back to the metadata card (exists today: `FilePreviewService.cs:321-346` — name/size/created/modified/path + actions).
- **Failure modes:** [Security] the same card for an `.exe`/`.lnk`/`.ps1` with a prominent "Open" action *launches it* — one misread click. [Windows Shell] probing OneDrive placeholders for size/hash/thumbnail hydrates them. [QA] multi-GB file + eager hashing/thumbnailing = hang; locked files and >260-char paths need graceful messages. [File Format] third-party `IThumbnailProvider` COM handlers for unknown types run foreign code in-process — restrict to icon + metadata for unknowns.
- **Required changes:** Executable guard (#5); no-hydrate policy (#11); lazy, size-capped hashing; icon-only thumbnails for unknown types. Optional P3: safe hex/text peek of first 4 KB.
- **Severity:** P0 (executable guard), P1 (hydration) — **Confidence:** High

### 4. Add Many Files to Context

- **Verdict:** **Fail** (as specified) — passes with changes
- **User expectation:** Select 20 mixed files → one right-click → one batch lands in Context Shelf with progress and a summary.
- **Planned behavior:** Phase 3 static verb `Add to Context Shelf`; each invocation forwards args over the named pipe to the primary instance (`Program.cs:61-83`, `SingleInstanceGuard.cs:123-183`).
- **Failure modes:** [Windows Shell] static verbs invoke **once per selected file** → 20 process spawns; above 15 items Explorer suppresses static verbs entirely (`MultipleInvokePromptMinimum` default 15) → the verb doesn't appear at 20. The acceptance criterion is unachievable as written. [QA] cold-start burst: 19 secondaries race a **5-second pipe-connect timeout** (`SingleInstanceGuard.cs:146`) while the primary initializes DB/quick_check — timeouts throw and those files are silently dropped. [Power User] 20 unbatched adds = 20 toasts/rows popping. [Data-Loss] copying 20×4 GB into managed storage explodes disk; referencing breaks later (see #1). [Maintainer] arg forwarding sends one request per process — but `IpcRequest.Arguments[]` is already an array (`IpcProtocol.cs:48-57`), so in-app coalescing is cheap.
- **Required changes:** #3 (coalescing window + ≤15 acceptance + drag-drop bulk path + `IExplorerCommand` spike for v2); retry/backoff on pipe connect + visible "N added, M failed" summary; dedup by path (then hash); size-threshold copy-vs-reference (#1); ingest progress with cancel.
- **Severity:** P0 — **Confidence:** High (platform constraint + code)

### 5. Annotate and Overwrite Image

- **Verdict:** Pass with changes (plan intent is right; mechanism unspecified and contradicted by every existing write path)
- **User expectation:** Save-over-original is safe: automatic backup, no corruption on failure, obvious recovery.
- **Planned behavior:** Phase 5 promises automatic backup/revision, atomic saves, and state labels — but specifies none of it, and ships it *after* Phases 1–4 have already shipped save actions.
- **Failure modes:** [Data-Loss] current saves are `File.Copy(..., overwrite: true)` / `WriteAllBytesAsync` — a crash mid-write destroys the destination; nothing creates backups (verified across shelf, history, pins, preview, editor: `ShelfItemViewModel.cs:199`, `HistoryViewModel.cs:472`, `PinWindow.xaml.cs:400`, `FilePreviewService.cs:267`, `AnnotationEditorWindow.xaml.cs:499,542`). [File Format] PNG re-encode via `PngBitmapEncoder` (`EditorExporter.cs:44-52`) silently drops ICC profiles, text chunks, DPI metadata; JPEG re-encode degrades + EXIF orientation risks double-rotation. [Casual] no restore surface planned — recovery is invisible. [QA] read-only attribute, file locked by another app, cloud-synced folder replace conflicts.
- **Required changes:** #2 (SafeFileWriter in Phase 1–2 + revision store + restore UI + retention); metadata-preservation or explicit warning on re-encode; state labels exactly as planned; link revisions to the `.octadock` project so recovery can be re-editable.
- **Severity:** P0 — **Confidence:** High

### 6. Annotate PDF

- **Verdict:** Pass with changes (only because the plan already gates writeback behind investigation — the gates need teeth)
- **User expectation:** Highlights and comments like a PDF app; save keeps text selectable, pages intact, existing annotations preserved.
- **Planned behavior:** Phase 2 PDF preview first; Phase 5 "PDF annotation/writeback after provider investigation."
- **Failure modes:** [File Format] the current annotation stack is raster-only — "annotate PDF" without a real PDF annotation model means flatten-to-image, which destroys selectable text and is not what users expect from "save." [Data-Loss] any rewrite of a signed PDF invalidates the signature; encrypted PDFs and permission bits (no-annotate flag) must be honored; AcroForm-heavy files break under naive rewrite. [Maintainer] library reality: PDFium renders but doesn't write; iText is AGPL (license trap); Apryse/Syncfusion cost money; PdfSharp is partial. This decision is load-bearing for two phases and currently unowned.
- **Required changes:** #4 — v1 = render preview (PDFium-class) + annotate producing **sidecar layer (default) or flattened annotated copy (explicit, labeled)**; detect signatures/encryption/permissions and hard-block the writeback path; true incremental-update writeback only in v2 after the library+license decision.
- **Severity:** P0 (scope honesty), P1 (detection gates) — **Confidence:** High (signed/encrypted behavior), Medium (library specifics)

### 7. Open Office Document

- **Verdict:** Pass with changes
- **User expectation:** Open `.docx`, see the document, add a comment/highlight, save without wrecking formatting or tracked changes.
- **Planned behavior:** Phase 2 rich preview priority #2; Phase 5 comments/highlights "after provider investigation."
- **Failure modes:** [Maintainer] there is no free high-fidelity .docx renderer: Word COM interop requires installed Office and is fragile; LibreOffice headless is a ~400 MB dependency; commercial SDKs cost. "Office preview" as rendered pages is a trap. [File Format] OpenXML SDK gives safe *extraction* (text, images, sheet grids, metadata) and even comment/highlight injection — but round-tripping protected or track-changes documents is where corruption happens. [Security] macros: OpenXML reading never executes them (safe by construction — say this explicitly and badge `.docm`/`.xlsm`); legacy binary `.doc`/`.xls` are a separate risky parser family. [Product Owner] scope creep risk from "preview Office" → "edit Office."
- **Required changes:** v1 Office preview = **extracted content** (text, embedded images, spreadsheet grid via OpenXML — reuse the CSV grid UX from ref 02), explicitly not page-faithful rendering; writeback (comments/highlight only) = v2 behind validation (OpenXML validator + backup + reopen-verify); macro-enabled and legacy binary formats read-only in v1.
- **Severity:** P1 — **Confidence:** High

### 8. Open Large Zip

- **Verdict:** Pass with changes
- **User expectation:** Multi-GB zip opens instantly to a browsable tree; extract selected/all works; nothing scary happens.
- **Planned behavior:** Phase 6 zip tree/list, metadata, extract selected/all; package-zip export.
- **Failure modes:** [Security] zip-slip (`..\`, absolute paths, drive-relative names) on extract = arbitrary file write; zip bombs (entry-count / expansion-ratio) on extract-all. [File Format] AES-encrypted entries unsupported by `System.IO.Compression` — must show a locked state, not crash; nested archives shouldn't auto-recurse; reserved device names (`CON`, `aux`) and >260-char entry paths break extraction. [QA] free-disk check before extract-all; cancel + progress; zip64 (OK in `System.IO.Compression`).
- **Required changes:** #10 (zip safety spec). Central-directory-only browsing is already the platform default — codify "no full extraction for preview" as acceptance.
- **Severity:** P1 (P0 if extraction ships without traversal guard) — **Confidence:** High

### 9. Explorer Integration Toggle

- **Verdict:** Pass with changes
- **User expectation:** Toggles do what they say; moving the app doesn't strand menu entries; disabling removes everything.
- **Planned behavior:** Phase 3 Settings > Files, five toggles, HKCU-only, repair + unregister controls.
- **Failure modes:** [Windows Shell] unregister must enumerate *everything*: ProgID `Octadock.Preview`, per-extension `OpenWithProgids` values, `SystemFileAssociations\<ext>\shell\Octadock.AddToDock` verbs (`FileAssociationRegistration.cs:23-136`), plus the new all-file verbs — and fire `SHChangeNotify(SHCNE_ASSOCCHANGED)` or Explorer shows stale entries. [Maintainer] startup currently registers unconditionally (`App.xaml.cs:106`; comment admits "Always-on for now (no setting yet)") — repair-on-startup must become conditional on the toggle, following the existing settings-synced patterns (`SyncStartupRegistration`/`SyncProtocolRegistration`, `App.xaml.cs:104-105`). [QA] upgrade/move while the old instance is running: the new exe forwards args to the **old** binary and exits (documented on this machine), so "repair" doesn't run until a genuinely fresh launch — needs a version handshake in `IpcRequest` + "restart to finish update" prompt. [Casual] "Open With for all files" pollutes every file's menu — default off with explanation. [Product Owner] default-app seizure is already impossible on Win10/11 (UserChoice hash) — keep the non-goal but reframe the real risk: Open With pollution and orphaned entries after manual deletion (no installer exists in the repo).
- **Required changes:** Full-cleanup enumeration + `SHChangeNotify` + "Remove all Explorer integration" button; toggle-conditional startup repair; registered-path vs current-path status readout; IPC version handshake; first-run adoption of existing registration state into toggles (#7).
- **Severity:** P1 — **Confidence:** High

### 10. Upgrade From Current Build

- **Verdict:** Pass with changes
- **User expectation:** Captures, history, pins, settings survive; new Context tables appear; nothing about AI Sessions resurfaces; a failed upgrade doesn't eat the library.
- **Planned behavior:** Plan is silent on migration mechanics; Phase 4 just says "add persistent domain model."
- **Failure modes:** [Data-Loss] migrations run in transactions with `user_version` stamping and quick_check + quarantine + salvage exist (`OctadockDatabase.cs:135-235`) — good — but there is **no pre-migration file backup**, and this machine's history includes WAL-checkpoint bugs that made the main DB file a 4 KB shell and corruption that turned writes into silent no-ops. A backup copy before the v7 migration is cheap insurance for the exact data (curated context) users can't recreate. [QA] downgrade: behavior of an older binary opening `user_version` = 7 is unverified — needs an explicit refuse-with-message guard. [Maintainer] Context tables must be additive so a Phase 4 failure can't take captures down. [Product Owner] association state: upgrading users have always-on registration; new toggles must adopt that state as ON (detected), not silently unregister or misrepresent it as a user choice.
- **Required changes:** #7 (checkpoint → file-copy backup → migrate → verify; downgrade guard; association adoption). Add "restore from backup" to the existing quarantine/salvage story.
- **Severity:** P1 (P0 if shipped with no pre-migration backup) — **Confidence:** High

### 11. UI Density and Accessibility

- **Verdict:** Pass with changes
- **User expectation:** Rows, rails, and inspectors stay readable and clickable on a 1366×768 laptop at 125 %, a 4K at 200 %, and an ultrawide; keyboard and screen reader work.
- **Planned behavior:** Phase 1 shared tokens; reference-driven density.
- **Failure modes:** [Designer] the references' gray-on-near-black metadata text likely fails 4.5:1; dark glass loses all hierarchy when Windows transparency is off (battery saver, RDP, high-contrast) — tokens need solid fallbacks. [QA] WPF per-monitor-DPI (PMv2) across mixed-DPI monitors is a classic bug source for the floating shelf/pin/HUD windows; pins restored onto a disconnected monitor must recover on-screen; Windows text-scale factor doesn't auto-apply in WPF. [Casual] the HUD (ref 05, six large tiles) + footer must fit 1366×768 @ 125 % or scroll. [Accessibility] icon-only rails need `AutomationProperties.Name` and roving keyboard focus; ref 04's teal focus border is a good visible-focus baseline. [Power User] ultrawide: cap History grid column growth.
- **Required changes:** #13 (token fallbacks, contrast, PMv2 audit); #9 (keyboard/SR for rails); HUD compact layout; off-screen window recovery.
- **Severity:** P1 (contrast/keyboard), P2 (rest) — **Confidence:** High for contrast/fallback risks, Medium for specific DPI bugs

### 12. Privacy-Sensitive Context Package

- **Verdict:** Pass with changes
- **User expectation:** See exactly what leaves the machine; exclude anything; no hidden text or paths in the package.
- **Planned behavior:** Phase 4 include/exclude toggles on items; fixed package layout.
- **Failure modes:** [Privacy] OCR is the stealth leak: OCR text lives in the DB (`actions.metadata_json`) and would flow into `ocr/` and inline `context.md` — excluding a screenshot must exclude **all its derivatives** (OCR, thumbnail, annotations), or redaction is an illusion. [Privacy] `manifest.json` with absolute paths leaks `C:\Users\<name>\...` — relative paths by default, absolute source paths opt-in. [Casual] blurred annotation ≠ redacted original: if the original file is attached, the secret ships anyway — label "original attached." [QA] "copy as Markdown" must respect the same exclusions; zip export must mirror the folder exactly; staging temp cleaned up.
- **Required changes:** #6 (export preview with per-derivative toggles, relative paths); derivative-exclusion rule as a hard invariant; opt-in "strip image metadata (EXIF/GPS)" toggle (P2); secrets-pattern warning is P3, not v1.
- **Severity:** P1 (trust) — **Confidence:** High

## Risk Register

| Risk | Area | Severity | Likelihood | Mitigation | Verification |
| --- | --- | --- | --- | --- | --- |
| Failed/interrupted save destroys user original (all writes non-atomic today) | Editing / Data | P0 | High | Shared SafeFileWriter: temp-in-dir + `File.Replace` w/ backup; route all writes | Kill process mid-save ×100; original always intact or restorable |
| Context bundle items dangle after source discard/move/retention | Context / Data | P0 | High | Snapshot-by-default ownership; verify-at-export; materialize option | Discard capture in bundle → export still complete |
| Explorer verb invisible >15 items; per-file invocation storm | Shell | P0 | High | Coalescing window; ≤15 acceptance; drag-drop bulk; `IExplorerCommand` v2 | 1/15/16/20/100-file selections, warm start |
| Cold-start burst drops forwarded files (5 s pipe timeout, `SingleInstanceGuard.cs:146`) | Shell | P1 | Medium | Connect retry/backoff; "N added, M failed" summary toast | Verb on 20 files with app not running; count ingested |
| PDF writeback invalidates signatures / breaks forms | Editing | P0 | High | Detect signature/encryption/permissions → force copy/sidecar | Signed, encrypted, AcroForm corpus |
| Office round-trip corrupts tracked/protected docs | Editing | P1 | Medium | v2 gate; OpenXML validate + backup + reopen-verify | Track-changes corpus round-trip diff |
| v7 migration fails on WAL-fragile store with no backup | Data | P1 | Medium | Checkpoint → file backup → migrate → verify; downgrade guard | Upgrade/downgrade matrix incl. stale WAL/SHM sidecars |
| Zip extraction path traversal writes outside target | Files / Security | P0 | Medium | Canonicalize entry paths; reject escapes/absolute/reserved names | Malicious zip corpus |
| Fallback "Open" launches hostile executable | Files / Security | P0 | Medium | Executable-type guard + explicit confirm; Reveal primary | Verb + fallback card on .exe/.lnk/.ps1 |
| Package leaks OCR text / local paths / username | Context / Privacy | P1 | High | Derivative-exclusion invariant; relative paths; export preview | Export with exclusions; grep package for `C:\Users`, OCR strings |
| Image re-encode silently drops ICC/EXIF/DPI, degrades JPEG | Editing | P1 | High | Preserve metadata chunks or warn; JPEG quality policy | Round-trip PNG w/ ICC; JPEG w/ EXIF orientation |
| OneDrive placeholder hydration on preview/ingest/hash | Files | P1 | Medium | Attribute check; no-recall opens; explicit download step | Placeholder folder: open, ingest, export — zero hydration |
| Glass UI unreadable with transparency off / high contrast | UI | P2 | High | Solid fallback tokens; high-contrast palette | RDP + battery-saver + high-contrast pass |
| Upgrade-while-running forwards args to old binary (memory-documented) | Shell / Data | P2 | High | IPC version handshake → restart prompt | Replace exe while running; invoke verb |
| Huge-file preview hangs (eager hash/thumbnail) | Files | P1 | Medium | Lazy, size-capped derivatives; cancellable loads (exists: `FilePreviewService.cs:112-117`) | 20 GB file: open, fallback card, ingest |
| Open With pollution / orphaned HKCU entries after manual delete (no installer in repo) | Shell | P2 | Medium | "Remove all integration" button; `SHChangeNotify`; uninstall docs | Delete exe → inspect Open With + context menu |

## Revised Scope

### V1 Must Ship

- Phase 1 row-based Capture Shelf + shared tokens, including rail-overflow policy, extension-preserving truncation, keyboard/SR access.
- SafeFileWriter + revision store + restore UI (moved from Phase 5 into Phase 1–2 as shared infrastructure).
- Universal file router with fallback card, executable guard, lazy derivatives, cancellation.
- PDF render preview (render-only library, e.g. PDFium-class; license cleared).
- Settings > Files with the five toggles, conditional startup repair, full unregister + `SHChangeNotify`, status readout.
- Context Shelf: persistent model with snapshot ownership, add from shelf/history/preview/editor/Explorer, panel with reorder/include/notes, folder export **with export preview**, persistence across restarts.
- Image annotation writeback through the revision layer; state labels (Editing original / copy / annotation layer / read-only).
- Zip preview (browse, metadata, extract selected/all) with the zip safety spec.
- Migration hardening (pre-migration backup, downgrade guard, association-state adoption).

### V1 Should Ship

- Office preview as extracted content (docx text/images, xlsx grid) via OpenXML — explicitly not page rendering.
- Coalesced multi-add ingest UX (progress, dedup, failure summary) + drag-drop bulk path.
- Pinned viewer refresh per ref 01 + compact mode.
- Capture HUD refresh per ref 05 (all six modes exist in code, including scrolling).
- Cloud-placeholder no-hydrate policy.
- Copy-as-Markdown; context package zip export.
- SVG preview via a script-inert static rasterizer (never a browser control).

### V2 / Later

- PDF true annotation writeback (post library/license decision); Office comment/highlight writeback (post validation spike).
- `IExplorerCommand` COM handler for real multi-select batching.
- In-archive editing; non-zip archive formats (7z/rar need native deps/licensing).
- Command Deck redesign per ref 02 (minus AI panels — largest new surface, defer); full History library/inspector redesign per ref 03 (or trail v1 as capacity allows).
- Video/audio previews; PSD/AI/Figma/Sketch investigation.
- EXIF/GPS strip-on-export toggle; secrets-pattern warnings.

### Explicit Non-Goals

- AI discovery / AI Sessions in any surface (including ref 02's "AI Analyze" button and "AI" dock tile — do not carry these over).
- Becoming or seizing default app for any type (Windows already prevents silent seizure; we also don't prompt for it).
- Full text/Office/PDF *editing* (beyond annotation scope defined above); macro execution ever, in any format.
- Cloud sync/sharing of context packages (folder/zip handoff only).

## File-Type Policy

| Type | Preview | Edit Original | Backup Required | Fallback |
| --- | --- | --- | --- | --- |
| Images | Rich (exists) | **Yes — v1**, via revision layer only | Yes, automatic + restorable | FileInfo card for unsupported raster variants |
| Text/Markdown/CSV/JSON | Rich (exists, row-capped) | No in v1 (viewer; no text editing promised) | n/a | FileInfo card |
| PDF | Rich render, v1 | No in v1 — sidecar (default) or flattened annotated copy (explicit); v2 writeback post-investigation; signed/encrypted/restricted: never | Yes when v2 writeback lands | FileInfo card |
| Office | Extracted content v1 (OpenXML); no page rendering | No in v1; v2 = comments/highlights only, validated + backed up; macro-enabled + legacy binary: read-only | Yes (v2) | FileInfo card (.doc/.xls/.ppt legacy always) |
| Zip | Tree/browse v1, guarded extract | In-archive editing v2+, backup mandatory | Yes (v2) | FileInfo card (encrypted: locked state) |
| Unknown Binary | FileInfo card (+ optional hex peek P3) | Never | n/a | Is the fallback; executables get launch guard |
| Design Files | SVG v1 via script-inert rasterizer; PSD/AI/Figma/Sketch v2 investigation | Never in v1 | n/a | FileInfo card |

## Explorer Integration Policy

- **Open With:** supported-types registration ON by default for upgrades (adopt existing state) and fresh installs — it's today's shipped behavior — now user-controllable; "all files" OFF by default with a plain-language warning.
- **Right-click verbs:** Open in Octadock (all files), Add to Context Shelf (all files), Add image to Capture Shelf (images only — current `Octadock.AddToDock` pattern). All HKCU. Executable types: verbs still shown, but in-app launch guard applies.
- **Multi-file behavior:** v1 = per-file invocations coalesced in-app into one batch (time-window), acceptance ≤15 items; bulk path = drag-drop onto shelf/Context panel (single COM drop delivers all paths); v2 = `IExplorerCommand`.
- **Disable/unregister behavior:** toggles remove exactly their keys; "Remove all Explorer integration" removes ProgID, all `OpenWithProgids` values, all verbs; every change fires `SHChangeNotify(SHCNE_ASSOCCHANGED)`; startup repair runs only when the corresponding toggle is enabled; settings show registered-path vs current-path status.
- **Default-app behavior:** never write `UserChoice`/defaults, never prompt to become default; non-goal stands.

## Context Package Policy

- **Canonical structure:** `context.md`, `manifest.json` (with `schemaVersion` + generator version + item order), `attachments/`, `thumbnails/`, `ocr/`, `annotations/`.
- **Local path handling:** all intra-package references relative; absolute source paths excluded from manifest by default, opt-in toggle ("include source paths"); no username-bearing strings in the default output.
- **OCR handling:** per-item OCR files in `ocr/` + inline in `context.md` only for included items; OCR inclusion toggleable per item at export (sourced from DB action records).
- **Annotation handling:** flattened PNG in `annotations/` by default; optional `.octadock` editable project inclusion (off by default — it may contain undo history of redacted content).
- **Redaction/exclusion:** hard invariant — excluding an item excludes every derivative (attachment, thumbnail, OCR, annotation, `context.md` mention); "blurred annotation + original attached" states are labeled.
- **Zip/export behavior:** zip is a byte-for-byte mirror of the folder export; staging happens under `%LOCALAPPDATA%` and is cleaned on success and failure; export preview (tree + size estimate) precedes both.

## UI Corrections

Concrete changes required to match the reference images:

- **Capture Shelf:** adopt ref 04 exactly — header "Shelf" + count pill + Clear all + gear; ~96×64 thumbnail box; name / dims·source-app / relative-time stack; teal active border + left status dot. Corrections beyond the reference: middle-truncate preserving extension; cap rail at 4–5 icons + overflow (plan adds context/OCR/more actions); rails appear on focus as well as hover; letterbox extreme aspect ratios; explicit empty state.
- **Context Shelf:** no reference exists — produce a mock first. Reuse the row grammar but differentiate: distinct header/icon, second accent treatment, source badges (capture/file/OCR/note), include checkboxes, drag-reorder handles, per-item notes, footer with item count + estimated size + Export.
- **File Preview:** tabbed Preview / Metadata / Text-OCR / Annotations / Context per plan; adopt ref 02's CSV panel (grid, column types, stats footer, Copy/Open/Save As) **minus "AI Analyze"**; provider badge; safety banner for executables/unknown binaries.
- **Pinned Viewer:** ref 01's top toolbar (Copy, Save, Annotate, Lock, Opacity slider, Always on top, Close) + bottom strip (dims · format · size · Pinned badge · "Pinned just now"); add compact/overflow mode below ~640 px; retain existing drag-move, corner-resize, arrow-nudge, middle-click-close, Ctrl+Alt unlock behaviors.
- **History/Library:** ref 03's filter rail (type counts, application search-list, date buckets, has-OCR/has-tags), card grid, right inspector (preview, Open/Copy/Restore, editable filename, metadata rows, OCR preview with char count, copyable file path, tags, notes). Corrections: drop "Share" unless sharing is actually scoped; storage meter + "Manage Library" only if retention management UI is in scope this cycle.
- **Capture HUD:** ref 05's six tiles map 1:1 to existing modes (Area, Window, Full Screen, Scrolling, OCR, Record — all present in code) with Ready/Standby states; footer quick-settings (Lock Aspect, Fixed Size, Delay, Settings); shortcut chip in header. Correction: must fit 1366×768 @ 125 % (compact tile size or scroll).
- **Command Deck:** keep ref 02's structure — left nav (capture history counts, by-type, by-app, storage meter), center (Recent Activity + Captures table), right shelf panel, bottom feature dock — **replacing AI Sessions with Capture Shelf / Context Shelf / Files / OCR / Recent Activity, and deleting the "AI" dock tile and "AI Analyze" buttons**. Keep Dictate/Read tiles (features exist). Defer the whole surface to v2.

## Test Matrix

| Test Area | Required Tests | Manual Verification |
| --- | --- | --- |
| Capture Shelf | Row layout per item type incl. 3840×60 and 60×3840 captures; rail overflow + roving keyboard focus; restore stack; MaxItems eviction; truncation preserves extension | Hover/focus visuals at 100/125/200 % DPI; hover-vs-focus rail reveal |
| Context Shelf | Persistence across restart + force-kill (stale WAL sidecars); add from every surface; reorder/include/notes round-trip; source-discarded item still exports; dedup on double-add | Fresh-user walkthrough: can they say which shelf they're in and why |
| File Preview | Provider routing matrix; unknown-type fallback; cancellation mid-load; size caps; executable guard blocks one-click open; locked/long-path/missing files | OneDrive placeholder: open/preview causes zero hydration |
| File Writeback | Kill-process-mid-save loop (original intact); backup created + restore round-trip; ICC/EXIF preservation; read-only + locked + cloud-synced failures; revision retention | Restore flow discoverable without docs |
| Explorer Integration | Register/unregister/repair idempotence; registry state after each toggle; 1/15/16/20-file verb behavior; cold-start 20-file burst (zero drops); `SHChangeNotify` fired | Win11 context menu placement; Open With list before/after remove-all |
| Context Export | Package completeness vs toggles; derivative-exclusion invariant; relative-path audit; zip mirrors folder; staging cleanup on cancel/failure | Grep exported package for `C:\Users`, usernames, excluded OCR strings |
| Migration | v6→v7 applies with pre-backup created; downgrade (old binary on v7) refuses cleanly; quarantine/salvage still works; association-state adoption; Context tables additive (failure doesn't touch captures) | Upgrade while old instance running → verify handshake/restart prompt |

## Open Questions

| Question | Owner | Blocking? | Suggested Default |
| --- | --- | --- | --- |
| Naming: is "Context Shelf" the user-facing name? Two "shelves" test poorly by inspection | Product/Design | Yes (Phase 4 UI) | Capture surface stays "Shelf"; context surface named "Context" (no "shelf") |
| Packaging/installer story — repo has none; who cleans HKCU on uninstall? | Product/Eng | No | Portable exe + prominent "Remove all Explorer integration"; revisit if an installer lands |
| PDF library + license budget (render-only free vs writeback AGPL/commercial) | Product | Yes (orders Phase 2/5) | PDFium-class render-only for v1; writeback decision deferred to v2 spike |
| Snapshot-vs-reference size threshold for Context items | Product | No | Copy ≤ 25 MB; reference above with per-item "materialize" |
| Revision retention defaults | Product | No | 10 revisions per file, 500 MB store cap, configurable |
| Does the current migration runner refuse `user_version` greater than it knows? | Engineering | Yes (Phase 4) | Verify; add explicit guard + friendly message |
| Legacy binary Office (.doc/.xls/.ppt) support | Product | No | FileInfo fallback only in v1 |
| Archive formats beyond zip (7z/rar: native deps + licensing) | Product | No | Zip only in v1; 7z read-only investigation v2 |
| Ref 03 shows a "Share" action in History — is sharing scoped? | Product | No | Cut; Copy/Export only |
| Ref 03 shows storage meter + "Manage Library" — is retention UI in scope? | Product | No | Meter read-only, linking to existing retention settings |
| Should `open --filepath` CLI accept multiple paths per invocation (IPC `Arguments[]` already supports arrays)? | Engineering | No | Yes — enables clean coalescing and scripted bulk adds |

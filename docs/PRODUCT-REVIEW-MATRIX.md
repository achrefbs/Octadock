# Octadock Product Review Matrix

Updated: 2026-07-20

This is the founder's editable keep/remove/change inventory for the current
Octadock product, its user-facing surfaces, and its proposed roadmap. It is not
a roadmap approval by itself.

Sources: [Capabilities](CAPABILITIES.md), [Current product state](PROJECT-STATE.md),
[Roadmap](ROADMAP.md), [Plan](PLAN.md), and
[UI inventory](design/UI-INVENTORY.md). Code and executed behavior outrank this
table if they disagree.

## How to review

Replace `Unreviewed` in the **Decision** column with one of:

- `Keep`
- `Remove`
- `Change`
- `Strengthen`
- `Update`
- `Merge`
- `Hide / Defer`
- `Build` (for missing capabilities)
- `Reject` (for roadmap ideas)
- `Keep removed` or `Restore` (for previously removed ideas)

Use **Priority** values `Critical`, `High`, `Medium`, `Low`, or `None`. Put the
desired behavior, new name, merge target, or reason in the final column.

You can edit this file directly, or reply in chat with compact decisions such
as `F-024 Remove`, `P-023 Change — simplify to one-step send`, or
`M-031 Build — High`; the table can then be updated from those IDs.

The **State / weakness** column is factual. It does not recommend that a feature
be kept.

State labels mean:

| State | Meaning |
|---|---|
| Built | A user-facing path exists now; a listed acceptance gap can still remain |
| Partial | The path is usable, but an important part, dependency, or acceptance check is incomplete |
| Beta | Intentionally shipped with a narrow, explicitly stated boundary |
| Prepared | Internal support exists, but there is no complete end-user feature |
| Not started / Deferred / Blocked | It is not a current product feature |

## Inventory at a glance

| Review area | Rows | What the rows cover |
|---|---:|---|
| Product-level questions | 10 | Decisions that determine what Octadock is and which pillars deserve investment |
| Current features | 151 | Desktop, capture, library, media, voice, Context, AI, automation, licensing, and distribution behavior |
| Pages and surfaces | 76 | 75 reachable surfaces plus 1 dormant compatibility window, across Windows, overlays, menus, dialogs, Settings, CLI, website, and service UI |
| Missing or deferred capabilities | 56 | Phase 2 ideas, known gaps, demand-led ideas, and release blockers |
| Previously removed or rejected | 8 | Ideas that are deliberately not current behavior |
| Claims and naming issues | 10 | Product language that needs an explicit founder decision |

## Product-level decisions

| ID | Question | Current direction | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|
| Q-001 | What is Octadock fundamentally? | Local-first Windows capture-to-context workspace becoming a memory layer | Unreviewed | — | — |
| Q-002 | What should be the daily core loop? | Capture or dictate, inspect, keep in Shelf or Context, then copy/export/send | Unreviewed | — | — |
| Q-003 | Should reviewed AI handoff remain a primary pillar? | Yes in current strategy; founder has questioned its value and wording | Unreviewed | — | — |
| Q-004 | Should Projects and Thoughts become the next major product slice? | Yes, after recovery gates | Unreviewed | — | — |
| Q-005 | Should file preview remain deliberately limited? | Current policy says glance-only and frozen, but later docs conflict | Unreviewed | — | — |
| Q-006 | Should recording remain in the product? | Beta, MP4 video only | Unreviewed | — | — |
| Q-007 | Should scrolling capture remain in the product? | Beta, manual vertical only | Unreviewed | — | — |
| Q-008 | Should Octadock ever add cloud sharing, sync, or teams? | Deferred until proven demand | Unreviewed | — | — |
| Q-009 | Which utilities deserve Dock-level visibility? | Capture, OCR, dictation, recording, Shelf, History, Clipboard, Context, reviewed handoff, Settings | Unreviewed | — | — |
| Q-010 | Which capabilities justify payment? | Capture, dictation, Context, and reviewed AI handoff | Unreviewed | — | — |

## Current feature decisions

### App shell, trust, and local platform

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-001 | Tray utility and native menu | Persistent-when-enabled tray icon with capture, library, voice, Context, AI, account, settings, pause, and exit commands | Built; users can hide it, and the native menu cannot match custom WPF styling | Change | — | Streamline to essential commands; collapse individual capture commands under one Capture submenu; remove Pause/Resume; account for the removed HUD |
| F-002 | Floating Dock capsule | Persistent-when-enabled capsule for Area, Window, Full screen, OCR, Dictate, Record, Shelf, History, Clipboard, Context, reviewed handoff, Settings, and trial state | Built; users can hide it; File, Read aloud, and Scrolling are not current Dock buttons; dense action list and mixed-DPI/rendered QA remain | Change | — | Minimal Dock: split Capture button, Dictate, Shelf, History, and More; main Capture click starts Area and its arrow exposes Window, Full screen, Scroll, OCR, and Record; remove reviewed handoff |
| F-003 | All-in-one capture HUD | Compact launcher for capture modes, OCR, recording, sizing, timer, settings, and AI review | Built; unclear whether it should stay a capture HUD or become a command palette | Remove | — | Remove the standalone HUD; use the Dock split Capture button plus streamlined tray and shortcuts instead |
| F-004 | Single-instance app behavior | One tray process; later launches forward commands through a per-user pipe | Built | Keep | — | Retain one running instance to prevent duplicate tray icons, hotkeys, and database writers |
| F-005 | Launch at login | Per-user Windows startup registration | Built | Change | — | Enable by default; provide a clear first-run opt-out and persistent Settings toggle; do not auto-enable clipboard monitoring or acquisition |
| F-006 | Global keyboard shortcuts | Configurable shortcuts for capture, HUD, OCR, recording, dictation, clipboard, and read aloud | Built; real conflict and accessibility QA remain | Change | — | Assign only Area capture, Full screen, and Dictate by default; leave all other supported shortcuts unassigned and editable; remove the HUD binding |
| F-007 | Pause and resume capture | Temporarily blocks acquisition commands from the tray | Built | Remove | — | Remove the feature and its tray state because capture already requires an explicit user command |
| F-008 | Dark, light, and high-contrast themes | Semantic WPF tokens, themed chrome, rounded corners, reduced-transparency fallback | Built; live theme, composed contrast, assistive technology, and rendered QA remain | Keep | — | Retain all theme, system-theme, high-contrast, and reduced-transparency support |
| F-009 | Octadock-window capture exclusion | Attempts to keep Octadock surfaces out of screenshots and recordings | Built where Windows supports it | Keep | — | Retain automatic exclusion of Octadock surfaces wherever Windows supports it |
| F-010 | Local SQLite persistence | Stores settings, captures, actions, pins, clipboard, Context, and retention state locally | Built; product data model is broad and needs long-run soak evidence | Keep | — | Retain local-first durable storage with no automatic sync |
| F-011 | Opt-in local crash reports | Writes redacted JSON crash reports locally when enabled | Built; no uploader or telemetry transport | Keep | — | Retain opt-in, redacted, local-only crash reports with no automatic upload |

### Capture and recording

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-012 | Area capture | Select a rectangular region across per-monitor overlays | Built; mixed-DPI and multi-monitor rendered QA remain | Keep | — | Retain as a core capture mode |
| F-013 | Window capture | Pick a visible window from per-monitor overlays | Built; uses WGC with GDI/PrintWindow fallbacks | Keep | — | Retain as a core capture mode |
| F-014 | Active or selected-monitor fullscreen capture | Captures one monitor without requiring a region drag | Built | Keep | — | Retain as a core capture mode and default shortcut |
| F-015 | All-monitor capture | Captures the entire multi-monitor desktop | Built; GDI path and unusual monitor layouts need live QA | Keep | — | Retain inside the Capture menu rather than as a main Dock action |
| F-016 | Previous-area capture | Reuses the last selected region and falls back to selection if none exists | Built | Keep | — | Retain as a secondary Capture-menu action with no default shortcut |
| F-017 | Self-timer capture | Delays a capture and shows a countdown pill | Built | Keep | — | Retain and expose through the Capture menu after removing the HUD |
| F-018 | Fixed-size and locked-aspect selection | HUD can constrain the selected capture area | Built; usability and DPI QA remain | Change | — | Retain for advanced users under Capture > Size and ratio; hide it from the main Dock; support an optional user-assigned shortcut with no default binding |
| F-019 | Selection dimensions and magnifier | Shows live pixel dimensions and a loupe during region selection | Built | Change | — | Combine dimensions and magnifier into an optional Precision aids setting; enable it by default and let users hide it |
| F-020 | Frozen-screen selection | Can freeze the desktop image while the user selects an area | Built; multi-monitor fidelity needs live QA | Change | — | Expose one clear Settings toggle: Freeze screen while selecting; enable it by default and let users turn it off |
| F-021 | WGC/GDI capture fallbacks | Chooses Windows Graphics Capture where possible and falls back when required | Built; hardware/app compatibility matrix remains | Keep | — | Retain automatic capture-engine fallback with no ordinary user-facing controls |
| F-022 | Configurable post-capture action | Route new captures to Shelf, copy, save, annotate, pin, or discard | Built | Keep | — | Retain Shelf, copy, save, annotate, pin, and discard as configurable default actions |
| F-023 | Capture output settings | Save directory, filename template, PNG/JPEG, JPEG quality, cursor, shadow, and monitor mode | Built | Change | — | Show save folder, image format, and cursor as Basic; move filename template, JPEG quality, shadow/frame, and monitor behavior under Advanced |
| F-024 | Manual vertical scrolling capture | User scrolls manually while Octadock stitches a long image | Beta; no auto-scroll or horizontal mode; adversarial app testing remains | Change | — | Retain as an Advanced capture option, clearly labeled Beta and manual vertical only; do not place it on the main Dock |
| F-025 | Active-monitor screen recording | Records MP4 video from the active monitor | Beta; video only, with no microphone or system audio | Strengthen | High | Complete the recorder with reliable long-running video, finalization/recovery, microphone audio, and system/app audio |
| F-026 | Selected-region screen recording | Records MP4 video from a user-selected area | Beta; video only and needs finalization/recovery soak | Strengthen | High | Keep and complete with the same reliable finalization/recovery, microphone audio, and system/app audio as full-monitor recording |
| F-027 | Recording cleanup and recovery | Cleans incomplete output and handles failed frame-pump cases | Partial; crash, shutdown, disk-full, and long-run proof remain | Strengthen | High | Make crash, shutdown, disk-full, corrupted-output, finalization, and long-recording recovery part of completing the recorder |

### Shelf and immediate recovery

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-028 | Capture Shelf | Floating stack where screenshots and recordings land | Built; core visual and mixed-DPI acceptance remain | Change | High | Keep the Shelf but redesign it: smaller uniform cards, cleaner/smoother presentation, dismiss-without-delete, a fixed edge tab, and automatic active-monitor following |
| F-029 | Image Shelf cards | Thumbnail-first cards with timestamp and contextual actions | Built | Change | High | Make every Shelf card the same smaller size with a cleaner, smoother visual treatment |
| F-030 | Video Shelf cards | Cards for completed Beta recordings | Built; videos cannot be pinned or annotated | Change | High | Keep video cards and match the compact uniform Shelf size; replace the transparent, easy-to-miss preview with a clearly visible poster frame/background and video affordance |
| F-031 | Shelf action toolbar | Open, copy, save, annotate, pin, add to Context, more actions, and discard | Built; action density and error feedback need review | Unreviewed | — | — |
| F-032 | Drag captures out | Drag capture files into Explorer, apps, or a browser | Built; temporary-export cleanup needs audit | Unreviewed | — | — |
| F-033 | Discard and restore recently closed | Durable soft discard with a restore path | Built; restart and rendered recovery QA remain | Change | High | Make the Shelf × dismiss the card only while retaining its capture in History and on disk; put permanent Delete in a separate clearly labeled action with confirmation |
| F-034 | Shelf placement and behavior | Anchor, size, maximum items, edge margin, auto-close, and eye-button behavior | Built; many options may be unnecessary | Change | High | Follow the current active monitor like the Dock pill; replace the moving eye with a small edge tab that stays at the Shelf anchor and expands/collapses in place |
| F-035 | File drop into Shelf | Accept local files and route them to preview or image handling | Built; unsupported formats fall back to metadata; live Release testing confirmed external video is rejected even though automation copy advertises video support | Unreviewed | — | — |
| F-036 | Add Shelf capture to Context | Adds the exact capture into the active Context package | Built; current-package clarity needs rendered QA | Unreviewed | — | — |

### Capture History and Clipboard History

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-037 | Local capture History | Persistent searchable record of captures, recordings, OCR, files, and annotations | Built | Unreviewed | — | — |
| F-038 | History search and filters | Search, type chips, date range, deleted state, and pagination | Built; tokenized DatePicker styles exist, but rendered, keyboard, and accessibility acceptance remains | Unreviewed | — | — |
| F-039 | History soft delete and restore | Delete without immediately destroying the artifact, then restore | Built | Unreviewed | — | — |
| F-040 | History retention and clear | Configurable retention plus manual clearing | Built; deletion behavior should remain explicit | Unreviewed | — | — |
| F-041 | Region OCR recovery in History | Stores OCR source capture and extracted text as recoverable actions | Built for region OCR only | Unreviewed | — | — |
| F-042 | First-run Clipboard consent | Explicitly asks whether clipboard collection should start | Built; fresh installs default off | Unreviewed | — | — |
| F-043 | Clipboard monitoring | Watches `WM_CLIPBOARDUPDATE` only after enablement | Built; live Win32 verification remains | Unreviewed | — | — |
| F-044 | Local text and image clip retention | Stores clipboard text and optionally images locally | Built | Unreviewed | — | — |
| F-045 | Clipboard privacy and deduplication | Password-manager exclusion formats, own-write suppression, dedupe, seen counts, and provenance | Built; exclusion coverage must remain conservative | Unreviewed | — | — |
| F-046 | Clipboard search, filters, and favorites | Search clips and filter by text, images, or favorites | Built | Unreviewed | — | — |
| F-047 | Clipboard copy, restore, delete, and clear | Restore a clip to the clipboard or remove retained material | Built; clearer failure feedback is needed | Unreviewed | — | — |
| F-048 | Clipboard cap and trimming | Limits the number of retained clips | Built | Unreviewed | — | — |

### Images, pins, annotation, and image mockups

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-049 | Image surface | Default image opener for captures and local image files | Built | Unreviewed | — | — |
| F-050 | Persistent floating pins | Keep images topmost and restore them across restarts | Built; topmost/lock/multi-monitor edge cases need visual regression | Unreviewed | — | — |
| F-051 | Pin positioning controls | Move, resize, nudge, change opacity, lock position, and toggle topmost | Built | Unreviewed | — | — |
| F-052 | Quick pen on image surface | Draw directly on a pinned image and clear the ink | Built; intentionally simpler than the editor | Unreviewed | — | — |
| F-053 | Image copy, save, reveal, and open source | Common image follow-through without leaving the surface | Built | Unreviewed | — | — |
| F-054 | Advanced annotation editor | Full non-destructive image-markup window | Built; icon language and rendered accessibility need review | Unreviewed | — | — |
| F-055 | Crop and object selection | Crop the image and select or move editable objects | Built | Unreviewed | — | — |
| F-056 | Shape and arrow tools | Arrow, rectangle, ellipse, and line objects | Built | Unreviewed | — | — |
| F-057 | Text and counter tools | Add text labels and numbered counters | Built | Unreviewed | — | — |
| F-058 | Highlighter and freehand tools | Highlight or draw freeform strokes | Built | Unreviewed | — | — |
| F-059 | Blur and pixelate tools | Obscure selected image regions | Built; distinct from outbound AI pixel redaction | Unreviewed | — | — |
| F-060 | Annotation undo and redo | Reverses editor operations | Built | Unreviewed | — | — |
| F-061 | Annotation export, copy, save, and drag | Produce a flattened image or drag it elsewhere | Built | Unreviewed | — | — |
| F-062 | `.octadock` editable image projects | Save and reopen image plus vector objects | Built; name can be confused with future product Projects | Unreviewed | — | — |
| F-063 | Bounded selected-region AI image mockup | Sends a selected crop, surrounding context, and mask to signed-in Codex; reviews and composites the result only inside the selection | Built in the annotation editor; explicit image egress, provider dependency, and product fit need review | Unreviewed | — | — |
| F-064 | Image save-behavior prompt | Choose overwrite, save copy, or future default behavior after edits | Built | Unreviewed | — | — |
| F-065 | Gather, show, hide, and close pins | Manage multiple floating image surfaces | Built | Unreviewed | — | — |

### OCR and file preview

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-066 | Region OCR | Select pixels and recognize text locally through Windows OCR | Built; live installed-language and application acceptance remains | Unreviewed | — | — |
| F-067 | File OCR | Recognize text from a chosen local image file | Partial; result is not stored as a History row | Unreviewed | — | — |
| F-068 | OCR output modes | Compact, lines, and layout-oriented output | Built | Unreviewed | — | — |
| F-069 | OCR provider and language settings | Choose engine/output/language and view availability | Partial; Windows AI and Tesseract are placeholders | Unreviewed | — | — |
| F-070 | Image preview routing | Images open in the image surface | Built | Unreviewed | — | — |
| F-071 | CSV and TSV preview | Tabular preview with sorting/filtering and statistics | Built within its intentional bounded-sampling scope; rendered QA remains | Unreviewed | — | — |
| F-072 | JSON preview | Pretty structured JSON preview | Built within its intentional read-only glance scope | Unreviewed | — | — |
| F-073 | Log preview | Bounded log-tail preview | Built within its intentional bounded/tail-oriented glance scope | Unreviewed | — | — |
| F-074 | Markdown preview | Render headings, lists, code, quotes, and text | Partial; theme refresh can reset reading position/selection | Unreviewed | — | — |
| F-075 | Text, code, and config preview | Bounded plain-text preview for a broad safe extension set | Built within its intentional read-only scope; no syntax highlighting | Unreviewed | — | — |
| F-076 | Unsupported-file metadata card | Shows safe file facts when rich preview is unavailable | Built fallback | Unreviewed | — | — |
| F-077 | Risky external-open warning | Rejects UNC paths and warns before shell-active/executable-like files open externally | Built; security-sensitive extension list | Unreviewed | — | — |
| F-078 | Explorer “Open with Octadock” | Registers supported files under the current Windows user | Built; no Settings toggle | Unreviewed | — | — |
| F-079 | Explorer “Add to Octadock dock” | Adds supported images to the Shelf from Explorer | Built | Unreviewed | — | — |

### Context packages

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-080 | Persistent named Context packages | Create and navigate durable bundles of local evidence | Partial; core works, rendered workflow acceptance remains | Unreviewed | — | — |
| F-081 | Add files, captures, and images to Context | Collect multiple artifact types into one package | Built | Unreviewed | — | — |
| F-082 | Snapshot/reference ownership | Snapshot small files and hash-reference larger files | Built; ownership model may be hard for users to understand | Unreviewed | — | — |
| F-083 | Per-item include/exclude review | Choose exactly which Context items export or enter AI review | Built | Unreviewed | — | — |
| F-084 | Context naming, notes, and navigation | Rename packages, keep notes, and move between packages | Built | Unreviewed | — | — |
| F-085 | Context reorder, open, and remove | Reorder evidence, open it, or remove it from the package | Built | Unreviewed | — | — |
| F-086 | Context folder export | Export the reviewed item set into a safe folder | Built | Unreviewed | — | — |
| F-087 | Context ZIP export | Export the reviewed item set into a ZIP | Built | Unreviewed | — | — |
| F-088 | Context manifest and safe relative paths | Records package contents without leaking absolute paths | Built | Unreviewed | — | — |
| F-089 | Changed/missing-reference fail closed | Refuses export or handoff if referenced evidence changed, vanished, or was not reviewed | Built | Unreviewed | — | — |
| F-090 | Context-to-AI review | Opens the included Context set in the reviewed AI workspace | Built; rendered owner/focus acceptance remains, and the founder has questioned the value of the overall feature | Unreviewed | — | — |

### Dictation, read aloud, and text tools

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-091 | Local Parakeet dictation | Default local speech-to-text engine after disclosed model download | Partial; real microphone, accent, and language evidence remains | Unreviewed | — | — |
| F-092 | Local Whisper fallback | Alternative local STT engine | Partial; model quality/performance varies by machine | Unreviewed | — | — |
| F-093 | Opt-in OpenAI transcription | Uses an environment-provided key only when explicitly selected | Partial; named cloud boundary and network behavior need live QA | Unreviewed | — | — |
| F-094 | Speech model manager | Consent, download, delete, and availability UI for local models | Partial; real download, cancellation, retry, and large-download UX need review | Unreviewed | — | — |
| F-095 | Live dictation partials and VAD | Shows stable/volatile transcript text while listening | Partial; hardware validation remains | Unreviewed | — | — |
| F-096 | Auto-stop, discard, cancel, and partial recovery | Ends on silence, allows discard, cleans microphone state, and preserves recoverable partials | Partial; real privacy/device failure matrix remains | Unreviewed | — | — |
| F-097 | Toggle, hold, or both activation modes | Choose how the dictation shortcut behaves | Partial; low-level hook behavior needs live validation and may be more configuration than needed | Unreviewed | — | — |
| F-098 | Dictation language and code-term dictionary | Language routing plus replacement rules for technical terms | Partial; live accuracy evidence remains | Unreviewed | — | — |
| F-099 | Paste-at-cursor and clipboard restoration | Inserts the transcript and restores the prior clipboard where possible | Built; Windows app compatibility needs live QA | Unreviewed | — | — |
| F-100 | Windows read aloud | Speaks literal text, clipboard text, bounded local text files, OCR regions, or local image OCR through built-in Windows voices | Built; binary files are rejected and text files are capped | Unreviewed | — | — |
| F-101 | Opt-in ElevenLabs read aloud | Optional environment-key cloud voice provider | Built; external provider dependency | Unreviewed | — | — |
| F-102 | Read-aloud controls | Sentence-chunked playback with an elapsed pill and pause, resume, and stop; voice and rate live in Settings | Built | Unreviewed | — | — |
| F-103 | Read AI result aloud | Speaks ephemeral reviewed-AI output | Built; usefulness depends on retaining AI review | Unreviewed | — | — |
| F-104 | JSON text transforms | Format and minify JSON | Built | Unreviewed | — | — |
| F-105 | Encode/decode transforms | Base64, URL, and HTML encode/decode, plus payload-only unsigned JWT inspection/decode | Built; JWT verification or signing is not performed | Unreviewed | — | — |
| F-106 | Identifier casing transforms | Convert text among common identifier styles | Built | Unreviewed | — | — |
| F-107 | Hash and timestamp transforms | MD5/SHA hashing and Unix timestamp conversion | Built | Unreviewed | — | — |
| F-108 | Line transforms and chaining | Sort, dedupe, trim, count, and feed one result into the next transform | Built; diff-two-clips is missing | Unreviewed | — | — |

### Reviewed AI and agent handoff

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-109 | Source-bound reviewed AI workspace | Opens from Shelf, pin, Context, History, Clipboard, Dock, tray, HUD, or automation | Built; rendered owner/focus/mixed-DPI acceptance remains, and the founder has explicitly said the feature currently feels useless | Unreviewed | — | — |
| F-110 | “Build from this” workflow | Seeds an implementation-oriented goal and acceptance criteria | Built inside the AI workspace | Unreviewed | — | — |
| F-111 | “Investigate an issue” workflow | Seeds a defect/root-cause investigation task | Built inside the AI workspace | Unreviewed | — | — |
| F-112 | “Verify a result” workflow | Seeds before/after comparison and pass/fail review | Built inside the AI workspace | Unreviewed | — | — |
| F-113 | “Extract usable data” workflow | Seeds structured extraction from screenshots or copied content | Built inside the AI workspace | Unreviewed | — | — |
| F-114 | “Prepare a handoff” workflow | Packages context, decisions, unknowns, and next action for someone else | Built; label and value proposition were explicitly rejected by the founder | Unreviewed | — | — |
| F-115 | Editable task intent | Edit title, goal, project, missing context, definition of done, and criteria | Built; complex form may feel like work rather than acceleration | Unreviewed | — | — |
| F-116 | Evidence selection | Add/remove recent captures, Context, local files, text, images, and OCR | Built; UI density and exact-source clarity need rendered QA | Unreviewed | — | — |
| F-117 | Deterministic Agent Packet | Produces `TASK.md`, `manifest.json`, and checksums | Built; may be over-engineered if the product direction changes | Unreviewed | — | — |
| F-118 | Default-on text-secret redaction | Detects and replaces many token/config secret forms in text | Built; screenshot pixels are not changed | Unreviewed | — | — |
| F-119 | Pixel-boundary warning | Explicitly warns that attached screenshot pixels are unredacted | Built warning; no pixel-redaction capability | Unreviewed | — | — |
| F-120 | Evidence integrity and fail-closed review | Hashes assets and refuses changed evidence after review | Built | Unreviewed | — | — |
| F-121 | Before/after visual comparison | Builds heat map, changed-pixel ratio, deltas, and bounds | Built; needs real visual acceptance | Unreviewed | — | — |
| F-122 | Read-only Claude CLI runner | Launches the user's Claude CLI with packet-relative Read/Glob only | Built; requires an installed, signed-in external CLI and still launches an external agent process | Unreviewed | — | — |
| F-123 | Ephemeral Codex CLI runner | Launches Codex with shell/exec disabled and explicit image attachments | Built; requires an installed, signed-in external CLI and still launches an external agent process | Unreviewed | — | — |
| F-124 | Destination-named confirmation | Shows the chosen CLI and exact packet before invocation | Built | Unreviewed | — | — |
| F-125 | Packet export and ephemeral results | Export a reviewed bundle; copy or read results; temporary packets are cleaned | Built; results are not saved unless copied | Unreviewed | — | — |

### Automation, licensing, updates, and distribution

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-126 | `octadock.exe` CLI | Sends commands to the running app and exposes truthful exit/JSON behavior | Built; power-user feature; `add-shelf-item` advertises image/video but returns success after rejecting a video | Unreviewed | — | — |
| F-127 | `octadock://` protocol | Routes supported deep-link commands to the app | Built; blocks microphone-starting/local-only commands | Unreviewed | — | — |
| F-128 | Durable capture IDs in automation | Successful capture replies identify the exact database/file artifact | Built | Unreviewed | — | — |
| F-129 | Fourteen-day local trial | Local no-account trial with monotonic high-water clock | Partial; deletion/replay can reset state under current policy | Unreviewed | — | — |
| F-130 | Signed offline entitlement | Verifies Ed25519-signed license state outside the main database | Partial; production key delivery is not ready | Unreviewed | — | — |
| F-131 | License activation UI, CLI, and deep link | Activate a key from Settings, automation, or protocol | Partial; production host/commercial lifecycle is missing | Unreviewed | — | — |
| F-132 | Paid-feature gating | Blocks new creation after expiry while existing artifacts remain viewable/exportable | Built policy seam; exact paid/free boundary needs product review | Unreviewed | — | — |
| F-133 | Entitlement replacement confirmation | Shows a default-No identity comparison before replacing a different license | Built | Unreviewed | — | — |
| F-134 | License service and admin health | Webhook, activation, device limit, signing, revocation, and minimal authenticated health surface | Partial backend; production auth, KMS, email, Stripe, and support remain | Unreviewed | — | — |
| F-135 | Signed update-manifest verification | Core can compare versions and verify an update manifest | Prepared only; no host, UI, updater, or installer | Unreviewed | — | — |
| F-136 | Self-contained ZIP packaging | Builds single-file App/CLI outputs with manifest and checksums | Partial distribution; unsigned ZIP, not an installer or public release | Unreviewed | — | — |
| F-137 | Static paid-beta website shell | Landing, pricing, privacy, refund, EULA, and terms pages | Partial; download/checkout/waitlist links are placeholders | Unreviewed | — | — |

### Additional current behavior found in the source audit

| ID | Feature | What exists now | State / weakness | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| F-138 | First-run onboarding and consent | One scrolling welcome page explains capture, dictation, Context, privacy, clipboard opt-in, startup, and the trial/license route | Built; the page pitches many concepts at once and clean-profile acceptance remains | Unreviewed | — | — |
| F-139 | Tray feedback notifications | Native tray balloons report short success, warning, and error messages and may open the saved output | Built; presentation and Windows-profile behavior are native and inconsistent with WPF surfaces | Unreviewed | — | — |
| F-140 | Recording session controls | Countdown and elapsed-time surface supports pause, resume, and stop-and-save | Beta; shutdown, crash, disk-pressure, and finalization soak remain | Unreviewed | — | — |
| F-141 | Shelf image transforms | Flip horizontally or vertically, rotate 90 degrees, or scale a high-DPI image to 1× | Built; image-only and hidden in the card menu | Unreviewed | — | — |
| F-142 | History item actions | Open, copy, pin, save, reveal details, and recover OCR text from selected retained items | Built; missing backing files can only fail and report | Unreviewed | — | — |
| F-143 | Approved mockup linkage in History | A source capture can expose its explicitly approved AI mockup, local spec, and copy/view actions | Built only after a mockup is approved; value depends on retaining the mockup workflow | Unreviewed | — | — |
| F-144 | Inline Pin AI image edit and undo | A Pin can send its pixels plus an explicit instruction to signed-in Codex, replace the displayed result, and undo once | Built; distinct from the annotation editor's bounded mockup flow; no silent fallback or result history | Unreviewed | — | — |
| F-145 | Preview common actions | Copy source/info/path, save a copy, reveal, open externally, fit images, toggle inspector, and close | Built; capability varies intentionally by file type, and external open can require warnings | Unreviewed | — | — |
| F-146 | Preview-to-Context and Shelf routing | Add the current local file to active Context; route supported images to Shelf/image handling | Built for the stated scope; non-image Shelf addition is not a general workflow | Unreviewed | — | — |
| F-147 | Preview cancellation and recovery | Latest request wins; loading can be cancelled, failures retried, and moved files located | Built; rendered recovery QA remains, while unsupported rich formats stay outside the frozen scope | Unreviewed | — | — |
| F-148 | AI command compatibility aliases | `ai`, `agent`, `handoff`, and historical aliases all open the one canonical reviewed workspace | Built; retained aliases add conceptual surface area even though there is only one live workspace | Unreviewed | — | — |
| F-149 | Automation opt-in and safety gates | Separate Settings toggles gate CLI/protocol use; restricted deep-link commands fail closed | Built; the protocol deliberately blocks microphone, quit, and AI/TTS-triggering local-only commands | Unreviewed | — | — |
| F-150 | Desktop presence controls | Settings can show or hide the tray icon, taskbar presence, and Dock | Built; some changes depend on Windows shell behavior and can reduce discoverability | Unreviewed | — | — |
| F-151 | Local storage disclosure and restore defaults | Advanced Settings shows local storage locations and can restore all settings defaults | Built; restoring settings does not erase retained user artifacts | Unreviewed | — | — |

## Page and surface decisions

Reviewing a surface is separate from reviewing its underlying feature. For
example, the decision can be `Keep` for capture but `Remove` for the HUD.

### Desktop surfaces

| ID | Page / surface | Type | Purpose and entry | Current concern | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|---|
| P-001 | Tray icon and menu | Native menu | Persistent-when-enabled access to almost every command | Users can hide it; the menu is very broad and native styling cannot match the app | Unreviewed | — | — |
| P-002 | Dock capsule | Floating surface | Persistent-when-enabled launcher for Area, Window, Full screen, OCR, Dictate, Record, Shelf, History, Clipboard, Context, reviewed handoff, and Settings | Users can hide it; too many actions; File, Read aloud, and Scrolling are not current Dock buttons; reviewed-handoff label is unclear; DPI QA pending | Unreviewed | — | — |
| P-003 | All-in-one capture HUD | Floating window | Tray, hotkey, or CLI capture launcher with mode, fixed-size/aspect, delay, reviewed-handoff, and Settings controls | Overlaps Dock; AI entry feels unrelated to capture | Unreviewed | — | — |
| P-004 | Capture Shelf | Floating window | Landing zone for new captures and recordings | Change: smaller uniform cards, cleaner motion/visuals, safer removal, active-monitor following, and a fixed edge tab that expands/collapses in place | Change | High | Redesign according to F-028/F-034 decisions |
| P-005 | Shelf item card | Repeated component | Thumbnail plus immediate capture actions | Change: cards should share one smaller size and feel cleaner/smoother; video cards need a visible poster/background; × dismisses only, while permanent Delete is separate and confirmed | Change | High | Redesign according to F-029/F-030/F-033 decisions |
| P-006 | Region selection overlay | Full-screen overlay | Select an area for capture, OCR, read-region-aloud, recording, or scrolling capture | Mixed-DPI, magnifier, contrast, and keyboard acceptance remain | Unreviewed | — | — |
| P-007 | Window picker overlay | Full-screen overlay | Choose a window to capture | Per-monitor opacity and mixed-DPI behavior need rendered QA | Unreviewed | — | — |
| P-008 | Self-timer countdown pill | Transient pill | Show delayed-capture countdown | Small utility surface; positioning QA remains | Unreviewed | — | — |
| P-009 | Scrolling-capture pill | Transient pill | Explain manual scrolling state and finish/cancel controls | Beta workflow may be confusing or not worth retaining | Unreviewed | — | — |
| P-010 | Recording pill | Transient pill | Elapsed time and recording controls | Separate status surface; recovery/device states need review | Unreviewed | — | — |
| P-011 | Dictation pill | Transient pill | Preparing/listening/transcribing state and live partials | Must remain calm and reliable during device/model failure | Unreviewed | — | — |
| P-012 | Read-aloud pill | Transient pill | Playback status, pause/resume, and stop | Secondary feature with another floating surface | Unreviewed | — | — |
| P-013 | Tray balloon notifications | Native notification | Report copy/save/errors and link to files | These are native tray balloons, not Windows toasts; presentation and error consistency need review | Unreviewed | — | — |
| P-014 | File preview card | Floating window | Quick-look local files from tray Open File, Explorer, CLI/protocol, and file flows | Large complex surface; preview scope and theme/scroll behavior need review | Unreviewed | — | — |
| P-015 | Image surface / pin window | Floating window | Open, pin, edit, save, and route an image | Toolbar density plus embedded AI prompt may be too broad | Unreviewed | — | — |
| P-016 | Annotation editor | Standard window | Full image markup and project editing | Mixed icon language and visual/accessibility QA remain | Unreviewed | — | — |
| P-017 | AI image mockup dialog | Modal dialog | Confirm provider and describe an image edit | AI-specific surface may not fit the desired product | Unreviewed | — | — |
| P-018 | Image save-choice dialog | Modal dialog | Choose overwrite/copy/default behavior | Extra decision point after image editing | Unreviewed | — | — |
| P-019 | Capture History window | Standard window | Search, recover, open, export, delete, and restore captures | Tokenized DatePicker styles exist; rendered controls, overall visual grammar, keyboard, and accessibility still need review | Unreviewed | — | — |
| P-020 | Clipboard History window | Standard window | Review and manage retained clips | Explicit consent is good; live behavior and value need review | Unreviewed | — | — |
| P-021 | Text Tools window | Standard window | Browse and run local text transforms | Standalone utility window may be overbuilt for a secondary feature | Unreviewed | — | — |
| P-022 | Context window | Borderless fixed-size topmost tool window | Create, curate, review, export, and delete Context packages | Concept, naming, package navigation, and AI action need usability review | Unreviewed | — | — |
| P-023 | Reviewed AI workspace | Standard window | Choose outcome, intent, evidence, packet, provider, and result | Founder considers current feature useless; form is complex and jargon-heavy | Unreviewed | — | — |
| P-024 | Dormant legacy AI Actions window | Code-defined compatibility window | Old AI action layout retained in dependency registration | No production open path; all aliases open the canonical workspace, so this is a dead-code retention/removal decision rather than a live page | Unreviewed | — | — |
| P-025 | Settings shell | Standard window | Five primary categories with nested subpages | Large information architecture; some controls expose unfinished features | Unreviewed | — | — |
| P-026 | Settings: Screenshots | Settings page | Output, file, monitor, timer, cursor, shadow, freeze, and exclusion settings | High option count | Unreviewed | — | — |
| P-027 | Settings: Recording | Settings page | FPS, quality, cursor, and unavailable audio options | Shows planned controls for features that do not exist | Unreviewed | — | — |
| P-028 | Settings: Pins | Informational Settings page | Explains hover controls, Pen, inline AI edit, and the More menu; contains no settings controls | Decide whether an information-only page belongs in Settings | Unreviewed | — | — |
| P-029 | Settings: Dictation | Settings page | Privacy, provider readiness, language, partials, models, and insertion | Dense and technically worded | Unreviewed | — | — |
| P-030 | Settings: Read aloud | Settings page | Local/cloud privacy, provider, voice, and rate | Dedicated page for a secondary feature | Unreviewed | — | — |
| P-031 | Settings: Advanced voice | Settings page | Provider models, activation mode, and developer dictionary | Likely too technical for most users | Unreviewed | — | — |
| P-032 | Settings: Shelf | Settings page | Frame, anchor, size, margin, maximum, peek, auto-close, restore | Many behavioral options may signal unresolved design | Unreviewed | — | — |
| P-033 | Settings: History | Settings page | Enablement, retention, and clear | Straightforward; deletion copy needs care | Unreviewed | — | — |
| P-034 | Settings: Clipboard | Settings page | Monitoring, image retention, and cap | Clear privacy boundary; may merge with History | Unreviewed | — | — |
| P-035 | Settings: OCR | Settings page | Engine, output mode, and language | Exposes placeholder engines | Unreviewed | — | — |
| P-036 | Settings: General | Settings page | Startup, tray/taskbar, Dock, crash reports, and theme | Mixes shell, privacy, and appearance | Unreviewed | — | — |
| P-037 | Settings: Shortcuts | Settings page | Edit global shortcut rows and conflicts | Needs strong conflict and keyboard UX | Unreviewed | — | — |
| P-038 | Settings: Automation | Settings page | Protocol and CLI toggles plus examples | Power-user content may belong outside primary Settings | Unreviewed | — | — |
| P-039 | Settings: Advanced system | Settings page | Data locations and restore defaults | Catch-all page; review content and naming | Unreviewed | — | — |
| P-040 | Settings: License | Settings page | Trial/license status, key entry, and activation | Commercial UI exists before production commercial path | Unreviewed | — | — |
| P-041 | First-run experience | One scrolling onboarding window | Explains capture, dictation, Context, inline AI image edit, privacy, clipboard consent, startup, and trial/license route | Broad one-page pitch, not a multi-step wizard; positioning should follow product decisions | Unreviewed | — | — |
| P-042 | About window | Standard modeless window | Version/product information, model attribution/licensing, and AI-edit disclosures | Basic utility surface | Unreviewed | — | — |
| P-043 | Explorer context-menu integration | Native shell surface | “Open with Octadock” and image “Add to dock” | Always registered; no opt-out UI | Unreviewed | — | — |
| P-044 | License replacement confirmation | System/modal confirmation | Default-No comparison before replacing another entitlement | Security-critical but visually disconnected | Unreviewed | — | — |

### Website and service pages

| ID | Page / surface | Type | Purpose and entry | Current concern | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|---|
| P-045 | Website landing page | Web page | Main product story and download/checkout journey | Current Aquarium direction; product claims must match actual scope | Unreviewed | — | — |
| P-046 | Pricing page | Web page | Explain paid-beta offer and purchase | Checkout is a placeholder; pricing/value depends on product decisions | Unreviewed | — | — |
| P-047 | Privacy page | Web page | Explain local storage and named network exceptions | Requires legal review and exact feature alignment | Unreviewed | — | — |
| P-048 | Refund policy page | Web page | State refund rules | Requires legal/commercial approval | Unreviewed | — | — |
| P-049 | EULA page | Web page | End-user license agreement | Requires legal review | Unreviewed | — | — |
| P-050 | Terms of service page | Web page | Commercial/service terms | Requires legal review | Unreviewed | — | — |
| P-051 | License-service admin health page | Minimal operations page | Authenticated launch-health metrics | Not a real support console; production network auth is external | Unreviewed | — | — |

### Internal views, menus, dialogs, and native pickers

These are listed separately because a feature can be worth keeping while one of
its steps, menus, or confirmation surfaces still needs to change.

| ID | Page / surface | Type | Purpose | Current weakness / question | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|---|
| P-052 | AI outcome chooser | Reviewed-workspace view | Choose Build, Investigate, Verify, Extract, or Handoff and seed the task | Five abstract modes may create work before value is clear | Unreviewed | — | — |
| P-053 | AI ready/evidence view | Reviewed-workspace view | Review evidence status and add missing context | Density, empty/error states, focus order, and terminology need review | Unreviewed | — | — |
| P-054 | Advanced Agent Packet view | Reviewed-workspace view | Inspect and copy the exact outbound Markdown packet | Technical implementation detail may be too prominent for most users | Unreviewed | — | — |
| P-055 | Agent review result view | Reviewed-workspace view | Show the ephemeral read-only CLI result with copy/read-aloud actions | Closing loses uncopied output; value depends on retaining the parent feature | Unreviewed | — | — |
| P-056 | Before/after verification view | Reviewed-workspace view | Compare two images and deterministic difference metrics | Pixel deltas cannot establish semantic correctness; viewport/DPI QA remains | Unreviewed | — | — |
| P-057 | Shelf frame menu | Context menu | Clear the visible Shelf or open Shelf Settings | Destructive-action wording and popup placement need review | Unreviewed | — | — |
| P-058 | Shelf item action menu | Context/menu surface | Open, annotate, copy/save, Context, AI, pin, reveal, transform, or discard one item | Dense and duplicates the hover rail | Unreviewed | — | — |
| P-059 | Pin More menu | Context/menu surface | Copy/save, opacity, clear pen, Context, AI, reveal, and source actions | Hover discoverability and no-activate keyboard behavior need review | Unreviewed | — | — |
| P-060 | Preview context menu | Dynamic context menu | Copy/format, Context, fit, Shelf, retry/locate/cancel, save/reveal/open, and close | Very broad state-dependent menu; focus/theme/DPI QA remains | Unreviewed | — | — |
| P-061 | Annotation canvas menu | Context menu | Edit/delete, undo/redo, copy/export, and AI mockup actions | Selection-state clarity and keyboard/accessibility QA remain | Unreviewed | — | — |
| P-062 | Analyze with Claude/Codex confirmation | Default-No modal | Name the destination and confirm the exact reviewed payload before launch | Critical trust step; owner, focus, and warning readability need live QA | Unreviewed | — | — |
| P-063 | Unsaved annotations confirmation | Native modal | Save, discard, or cancel when closing a dirty editor | Native MessageBox is visually disconnected; close/recovery behavior needs review | Unreviewed | — | — |
| P-064 | Clear Clipboard History confirmation | Default-No native modal | Permanently delete all retained clips, including favorites | Destructive scope must remain unmistakable | Unreviewed | — | — |
| P-065 | Delete Context confirmation | Native modal | Delete a package and managed snapshots without deleting original files | Ownership wording and owner/focus behavior need review | Unreviewed | — | — |
| P-066 | Clear Capture History confirmation | Native modal | Soft-delete listed or all retained captures | Exposed from History and Settings; wording should be consistent | Unreviewed | — | — |
| P-067 | Open file externally confirmation | Default-No native modal | Warn that control is leaving Octadock | Can be followed by a second, stronger risky-file prompt | Unreviewed | — | — |
| P-068 | Open executable-like file confirmation | Default-No native modal | Warn before shell-opening scripts, packages, links, or executable-like files | Security-critical wording, ownership, and focus need live QA | Unreviewed | — | — |
| P-069 | Download speech model consent | Default-No native modal | Disclose provider and download size before the first local-model download | Real cancel/retry/failure behavior remains to be exercised | Unreviewed | — | — |
| P-070 | Startup failure dialog | Fatal native dialog | Point to local logs and exit after an unrecoverable startup exception | Clean-machine failure path is not proven | Unreviewed | — | — |
| P-071 | Native open-file pickers | Windows picker | Select files for preview, Context, AI evidence, verification, and moved-file recovery | Filters, initial location, ownership, and focus vary by route | Unreviewed | — | — |
| P-072 | Native folder pickers | Windows picker | Select Context/Agent Packet export or capture output folders | Path-boundary, initial location, ownership, and focus need consistency | Unreviewed | — | — |
| P-073 | Native save pickers | Windows picker | Choose output for captures, previews, Context ZIP, annotations, mockups, History, and Pins | Initial name/folder, overwrite, ownership, and filters need consistency | Unreviewed | — | — |
| P-074 | Native annotation color picker | WinForms picker | Select custom stroke or fill color | Intentionally unthemed and visually inconsistent with the WPF editor | Unreviewed | — | — |
| P-075 | `octadock.exe` help and result interface | Terminal interface | Show global/per-command help plus human-readable or JSON success/error output | Help advertises `read --explain` while the dispatcher explicitly rejects it | Unreviewed | — | — |
| P-076 | Preview Details / Inspector panel | Expandable preview subview | Show scrollable metadata for the currently previewed local file | Toggle discoverability, scrolling, live theme, and keyboard behavior need rendered review | Unreviewed | — | — |

## Missing, deferred, blocked, and removed capability decisions

`Planned` below does not mean approved. Several items are only demand-led ideas.

### Memory spine and next-product ideas

| ID | Capability | Current status | Intended value / dependency | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| M-001 | Audio-first Thought capture | Not started | Insert, prompt, or save a Thought; retain playable audio if transcription fails | Unreviewed | — | — |
| M-002 | Explicit project registry | Not started | Register project roots without scanning the whole disk | Unreviewed | — | — |
| M-003 | Project resolver | Not started | Resolve spoken/project names with visible confidence and correction | Unreviewed | — | — |
| M-004 | Unplaced queue | Not started | Keep low-confidence Thoughts findable instead of silently guessing | Unreviewed | — | — |
| M-005 | Thoughts in Shelf/Library | Not started | Search, retain, and manage Thoughts beside captures | Unreviewed | — | — |
| M-006 | Projects screen and Git lens | Not started | Show branch, dirty count, last commit, and last touch on explicit open | Unreviewed | — | — |
| M-007 | Resume project actions | Not started | Open configured terminal/editor from a project | Unreviewed | — | — |
| M-008 | Read-on-open agent session lens | Blocked | Summarize local Claude/Codex session files after explicit consent | Unreviewed | — | — |
| M-009 | Catch-up and thought clustering | Not started | Opt-in synthesis through the existing reviewed packet path | Unreviewed | — | — |
| M-010 | Idea-to-brief synthesis | Not started | Turn retained ideas into an actionable brief | Unreviewed | — | — |
| M-011 | Read-only Tidy report | Not started | Report stale, dirty/no-remote, and large artifacts inside registered roots | Unreviewed | — | — |

### Known product gaps and demand-led ideas

| ID | Capability | Current status | Intended value / dependency | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| M-012 | Annotate or pin video captures | Planned | Bring video cards closer to image parity | Unreviewed | — | — |
| M-013 | File OCR saved in History | Planned | Make file-based OCR recoverable/searchable | Unreviewed | — | — |
| M-014 | Windows AI or Tesseract OCR engines | Placeholder only | Broader recognition choices | Unreviewed | — | — |
| M-015 | Microphone audio in recordings | Deferred | Complete narrated screen recording | Build | High | Required for the retained, completed recorder |
| M-016 | System audio in recordings | Deferred | Capture app/system sound | Build | High | Required for the retained, completed recorder, including system/app audio |
| M-017 | Camera, click, and keystroke overlays | Demand-led | Tutorial/demo recording | Unreviewed | — | — |
| M-018 | GIF, trim, compress, and recorder editing | Demand-led | Post-process recordings | Unreviewed | — | — |
| M-019 | Strong recording crash/disk-full recovery | Planned hardening | Avoid incomplete or false-success recordings | Build | High | Required for the retained, completed recorder; cover crash, shutdown, disk pressure, corruption, and long sessions |
| M-020 | Windows Speech fallback | Planned | Dictation fallback without downloaded models | Unreviewed | — | — |
| M-021 | Rich PDF preview | Deferred / policy conflict | Inspect PDFs in place | Unreviewed | — | — |
| M-022 | Office document preview | Deferred / policy conflict | Inspect Office files in place | Unreviewed | — | — |
| M-023 | Archive and design-file preview | Deferred / policy conflict | Inspect more artifact types | Unreviewed | — | — |
| M-024 | Non-image annotation and safe writeback | Deferred | Mark up other document formats | Unreviewed | — | — |
| M-025 | Syntax-highlighted code preview | Demand-led | Improve code readability | Unreviewed | — | — |
| M-026 | Code screenshot beautifier | Demand-led | Create polished code images | Unreviewed | — | — |
| M-027 | File-association Settings toggle | Planned | Let users opt out of Explorer integration | Unreviewed | — | — |
| M-028 | Visible clipboard failure states | Planned hardening | Make copy failures truthful and recoverable | Unreviewed | — | — |
| M-029 | Temporary drag/export cleanup hardening | Planned hardening | Avoid leaked temporary files | Unreviewed | — | — |
| M-030 | Diff two clips | Planned | Compare captured/copied text | Unreviewed | — | — |
| M-031 | MCP server | Deferred | Explicitly expose reviewed local context to external agents | Unreviewed | — | — |
| M-032 | Local-model destinations | Demand-led | Keep AI inference local | Unreviewed | — | — |
| M-033 | Write-enabled agent destinations | Gated / not started | Allow scoped actions rather than analysis only | Unreviewed | — | — |
| M-034 | Pixel-level secret redaction | Not started | Remove visible secrets before image handoff | Unreviewed | — | — |
| M-035 | Reusable task/profile library | Demand-led | Reuse common reviewed-agent jobs | Unreviewed | — | — |
| M-036 | Snippet and prompt library | Demand-led | Reuse text with paste-at-cursor and Context | Unreviewed | — | — |
| M-037 | Upload providers and share links | Deferred | Share artifacts outside the machine | Unreviewed | — | — |
| M-038 | Password-protected share links | Deferred | Safer external sharing | Unreviewed | — | — |
| M-039 | Hosted sync, teams, and accounts | Demand-led | Multi-device/team workflows | Unreviewed | — | — |
| M-040 | Self-service device management | Deferred | Manage licensed devices | Unreviewed | — | — |
| M-041 | Command palette and fuzzy launcher | Demand-led | Faster navigation than Dock/tray/HUD sprawl | Unreviewed | — | — |
| M-042 | Command dashboard | Rejected for now | Permanent mission-control style workspace | Unreviewed | — | — |
| M-043 | Color picker | Demand-led | Capture-adjacent utility | Unreviewed | — | — |
| M-044 | Scratchpad notes | Demand-led | Lightweight temporary notes | Unreviewed | — | — |
| M-045 | Future AI/MCP/upload/key-manager Settings | Deferred | Configure integrations if those features are approved | Unreviewed | — | — |
| M-046 | Crash-report uploader or product telemetry | No commitment | Production diagnostics; requires explicit consent | Unreviewed | — | — |

### Release and commercial gaps

| ID | Capability | Current status | Intended value / dependency | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| M-047 | Offline-trial reset/replay policy | Founder-blocked | Choose server-authoritative enforcement or explicitly accept an offline honor system | Unreviewed | — | — |
| M-048 | Installer/signing/update architecture | Not started | Normal install, upgrade, rollback, uninstall, and trusted protocol associations | Unreviewed | — | — |
| M-049 | Signed release candidate | Not started | Reduce SmartScreen friction and prove artifact identity | Unreviewed | — | — |
| M-050 | Clean Windows 10/11 VM matrix | Blocked | Verify install, launch, activate, upgrade, and uninstall | Unreviewed | — | — |
| M-051 | Real download and checksum delivery | Blocked | Replace website placeholders with a real artifact | Unreviewed | — | — |
| M-052 | Live checkout and waitlist | Blocked | Real purchase/interest path | Unreviewed | — | — |
| M-053 | Purchase-to-entitlement lifecycle | Not started | Webhook, email, activation, device limit/reset, refund, and revocation | Unreviewed | — | — |
| M-054 | Legal, support, DNS, and observability readiness | Not started | Operable paid beta | Unreviewed | — | — |
| M-055 | Paid-beta onboarding validation | Not started | Prove a new user reaches capture and dictation value quickly | Unreviewed | — | — |
| M-056 | In-app updater and update host | Planned | Deliver signed updates safely | Unreviewed | — | — |

### Previously removed or explicitly rejected

| ID | Capability | Current status | Why it was removed/rejected | Decision | Priority | Requested change / notes |
|---|---|---|---|---|---|---|
| R-001 | Background AI/session discovery | Removed | Violates explicit-action/local-first trust boundary | Unreviewed | — | — |
| R-002 | Ambient screen discovery | Removed | Would watch the screen without an explicit capture | Unreviewed | — | — |
| R-003 | `run`/`watch`/hook tracking and passive overlays | Removed | Continuous monitoring model was deliberately deleted | Unreviewed | — | — |
| R-004 | Hidden AI sends or provider fallback | Rejected | Destination and payload must be explicit | Unreviewed | — | — |
| R-005 | Stored AI prompt/session history | Rejected for current product | Conflicts with ephemeral reviewed-handoff model | Unreviewed | — | — |
| R-006 | Automatic or horizontal scrolling capture | Rejected for Beta | Current product promises manual vertical only | Unreviewed | — | — |
| R-007 | Autonomous/background agent workspaces | Removed | Conflicts with the explicit-action, reviewed, local-first trust model | Unreviewed | — | — |
| R-008 | `read --explain` agent explanation | Rejected in the current command dispatcher | Read aloud currently promises exact literal speech; the trusted-summary engine remains an unexposed internal prototype | Unreviewed | — | — |

## Claims and naming that need an explicit decision

| ID | Claim / naming issue | Current problem | Decision | Priority | Requested wording / notes |
|---|---|---|---|---|---|
| C-001 | “Local memory and handoff layer” | Memory-defining Projects and Thoughts are not built | Unreviewed | — | — |
| C-002 | “Octadock never runs the agent” | Octadock does launch Claude/Codex CLI after confirmation | Unreviewed | — | Suggested truth: never autonomously runs or grants write-enabled control |
| C-003 | “Library” | Docs say Library is built, but there is no standalone Library surface; Settings groups Shelf/History/Clipboard/OCR under that name | Unreviewed | — | — |
| C-004 | “Projects” | `.octadock` annotation-project storage can be confused with the unbuilt project registry/product screen | Unreviewed | — | — |
| C-005 | “File preview is frozen forever” | Later roadmap still lists richer safe previews | Unreviewed | — | — |
| C-006 | “Phase 1” | Plan says Gates A–C plus starting D, while definition of done effectively requires all commercial Gate D work | Unreviewed | — | — |
| C-007 | “Planned” | Many items are only directional/demand-led, not committed | Unreviewed | — | Use Committed, Sequenced, Directional, or Rejected |
| C-008 | “Release packaging” | Current artifact is an unsigned unpublished ZIP, not an installed public release | Unreviewed | — | — |
| C-009 | “Read-only AI” | Describes permissions, but can sound like no external process runs | Unreviewed | — | — |
| C-010 | “Built” | Many built features still lack rendered, DPI, hardware, accessibility, performance, or soak acceptance | Unreviewed | — | Consider “Built — acceptance pending” |

## Review outcome summary

Complete this after reviewing the tables.

| Outcome | Count / list | Notes |
|---|---|---|
| Features to keep | — | — |
| Features to remove | — | — |
| Features to change or strengthen | — | — |
| Surfaces to remove or merge | — | — |
| Missing capabilities to build next | — | — |
| Roadmap ideas to reject | — | — |
| Claims/names to rewrite | — | — |
| Three highest-priority product changes | — | — |

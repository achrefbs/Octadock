# Post-Expiry Verb Matrix — RATIFIED (WS5, R16/R17)

Status: **RATIFIED 2026-07-06 — founder chose the documented safe defaults** (§17 item 14).
The 4 ⚑ rows are resolved: **BLOCK** Read-aloud, file-preview-of-new-files, and Text Tools;
**ALLOW** Clear History. This now drives the WS5 gate (§6 order 8). The gate is enforced at
the service seams, never at scattered UI call sites, so every entry point (dock,
hotkey, `octadock://`, CLI, Explorer association) obeys the same rule.

Governing principles (Locked Decisions):
- The gate **never blocks viewing or exporting data the user already has.**
- It blocks **creating new content** and **running compute/cloud actions.**
- **Settings are always reachable** (so the user can enter a license key / buy).
- Every refusal routes to **one non-modal, UIA-announced expiry pill** (not a silent no-op).
- Restore of existing pins/windows on startup stays exempt.

Legend: **ALLOW** · **BLOCK** (→ pill) · **PAUSE** (background).

## Automation verbs (`CommandType`)

| Verb | Post-expiry | Enforcing seam | Rationale |
| --- | --- | --- | --- |
| `AllInOne` | **BLOCK** | `CaptureCoordinator` | Starts a new capture. |
| `CaptureArea` | **BLOCK** | `CaptureCoordinator` | New capture. |
| `CapturePreviousArea` | **BLOCK** | `CaptureCoordinator` | New capture. |
| `CaptureFullscreen` | **BLOCK** | `CaptureCoordinator` | New capture. |
| `CaptureWindow` | **BLOCK** | `CaptureCoordinator` | New capture. |
| `SelfTimer` | **BLOCK** | `CaptureCoordinator` | New capture. |
| `ScrollingCapture` | **BLOCK** | `CaptureCoordinator` | New capture. |
| `Pin` (new) | **BLOCK** | `PinService` | New pin creation; **restoring existing pins is ALLOWED** (startup restore exempt). |
| `RecordScreen` | **BLOCK** | `RecordingController` | New recording. |
| `CaptureText` (OCR) | **BLOCK** | `OcrService` | New OCR compute. |
| `ReadAloud` | **BLOCK** ⚑ | `ReadAloudService` | New TTS/compute. ⚑ founder: allow local-voice read of *existing* selection? |
| `AiActions` (execute) | **BLOCK** | `CliAiTextActionService` | New AI/cloud compute. The review window may open, but execution is gated before the selected CLI starts. |
| `Dictation` | **BLOCK** | `DictationController` | New STT compute (+ model). |
| `OpenAnnotate` | **BLOCK** | annotation service | Starts a new annotation job (per §9). Viewing an existing `.octadock` project is ALLOWED. |
| `OpenFromClipboard` | **BLOCK** | `PinService` | New pin from clipboard. |
| `AddShelfItem` | **BLOCK** | `CaptureCoordinator` | New shelf item (also UNC-guarded, R32). |
| `Open` (file preview) | **BLOCK** ⚑ | `FilePreviewService` | Preview of a *new* external file runs OCR/thumbnail compute (per §9). ⚑ founder: this is the most view-like of the blocks — confirm. |
| `OpenHistory` | **ALLOW** | — | View/export existing library. |
| `OpenClipboardHistory` | **ALLOW** (view) | — | View existing clips; the **monitor PAUSES** (below). |
| `OpenTextTools` | **BLOCK** ⚑ | text-tools seam | New text transforms are a paid feature. ⚑ founder: text tools are local + trivial — confirm block vs allow. |
| `RestoreRecentlyClosed` | **ALLOW** | — | Restores existing pins/windows (restore-exempt). |
| `ClearHistory` | **ALLOW** ⚑ | — | User managing their own data. ⚑ founder: confirm (destructive but user-owned). |
| `OpenSettings` | **ALLOW** | — | Must reach key entry / Account & Billing. |
| `Quit` | **ALLOW** | — | Always. |

## Background / non-verb surfaces

| Surface | Post-expiry | Enforcing point | Rationale |
| --- | --- | --- | --- |
| Clipboard monitor | **PAUSE** | `ClipboardMonitor`/`ClipboardHistoryService` | Stop recording new clips at expiry with an inline "Paused — trial ended" banner (WS10, R17). Existing clips stay viewable. |
| Annotate-existing (save/writeback) | **BLOCK** new jobs | annotation editor | New annotation edits blocked; existing projects viewable. |
| Text transforms | **BLOCK** ⚑ | `TextTools` | See `OpenTextTools`. |
| Recording | **BLOCK** | `RecordingController` | See `RecordScreen`. |
| Explorer "Open With" / association | inherits seam | `CaptureCoordinator`/`PinService` | Routes through the same seams, so obeys the same rules. |

⚑ (now resolved) = the four rows the founder ratified to their safe defaults on 2026-07-06:
**BLOCK** `ReadAloud`, `Open`, `OpenTextTools` (and Text transforms); **ALLOW** `ClearHistory`
(matches "block new activity, never block managing existing data").

## Ratification — DONE

Ratified by the founder 2026-07-06 (safe defaults). This feeds the WS5 gate implementation
(§6 order 8) and the WS11 trial/gate test matrix (every row verified by observed behavior,
incl. Narrator + keyboard completing expiry → buy → activate).

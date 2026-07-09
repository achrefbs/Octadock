# Octadock Capabilities Matrix

Last audited: 2026-07-09

Status legend:

- **Built** - implemented and wired in the app.
- **Partial** - user-visible or code-backed, but missing promised behavior.
- **Prepared** - code shape exists, but no complete user-facing flow.
- **Planned** - roadmap only.

| Capability | Entry points | Main code areas | Status | Limitations / next work |
| --- | --- | --- | --- | --- |
| Tray utility | Startup, tray icon, settings | `App.xaml.cs`, `TrayIconController`, settings | Built | Native Windows Forms tray menu path is confirmed working. |
| Permanent Dock | On-screen Dock capsule | `DockPill`, `HudService`, `HudWindow` | Built | Has capture/voice/library/settings actions and Context entry; visual system still needs final design enforcement. |
| Area capture | Hotkey, Dock, tray, CLI/protocol | `CaptureCoordinator`, `RegionSelectionService`, `CaptureEngine` | Built | Keep testing mixed-DPI overlays. |
| Previous-area capture | Hotkey, CLI/protocol | `CaptureCoordinator`, command parser | Built | Falls back to selection when no previous area exists. |
| Fullscreen capture | Hotkey, Dock, CLI/protocol | `CaptureCoordinator`, `CaptureEngine` | Built | Single monitor prefers WGC; all-monitor uses GDI. |
| Window capture | Hotkey, Dock, CLI/protocol | `WindowPickerOverlay`, `WindowPicker`, `CaptureEngine` | Built | Picker overlays are per-monitor; WGC falls back to GDI/PrintWindow when needed. |
| Self-timer capture | CLI/protocol, command path | `CaptureCoordinator`, command parser, `CaptureCountdownPill` | Built | Shows a transient countdown pill after region selection and hides it before capture. |
| Capture Shelf | Shelf window/cards | `ShelfService`, `ShelfViewModel`, `ShelfItemViewModel`, `ShelfItemView` | Built | Current image cards are image-first with hover actions and click-to-open image surface. Drag/drop and design polish still need visual QA. |
| Video shelf cards | Recording stop flow | `RecordingController`, `ShelfItemViewModel`, `ShelfItemView` | Built | Videos cannot be annotated or pinned yet. |
| Clipboard copy | Shelf/history/editor/pins | `WpfClipboardService` | Built | Clipboard failures need stronger surfaced error states. |
| Drag-out | Shelf/editor/pins | Shelf/editor/pin view models | Built | Temp export cleanup needs audit. |
| Local history | History window, retention timer | `HistoryViewModel`, repositories, `RetentionService` | Built | Includes type/search/deleted/date filters; the visual grammar still trails the command-center reference. |
| Annotation editor | Shelf/history/file/clipboard | `AnnotationEditorWindow`, `EditorViewModel`, renderer | Built | Image annotation is mature; embedded image object/non-image writeback are not complete. |
| Image surface / pins | Shelf/history/file/clipboard, `pin`, image `open` | `PinService`, `PinWindow`, `PinViewModel`, `PinRepository` | Built | Default image opener with quick pen, save-choice prompt, advanced annotate, add-to-Context, source open/reveal, pin/unpin topmost, opacity and resize. Needs visual regression on stack thumbnail refresh and lock/topmost edge cases. |
| OCR | Hotkey, Dock/HUD, CLI/protocol, file | `OcrService`, `OcrHistoryRecorder`, `WindowsMediaOcrProvider` | Partial | Region OCR and file OCR work; region grabs create `OcrSource` history rows with recoverable text. File OCR does not create history rows yet. Windows AI/Tesseract remain placeholders. |
| Scrolling capture | Dock/HUD, CLI/protocol | `ScrollingCaptureEngine`, `ScrollingSessionPill` | Partial | Manual vertical only; auto-scroll and horizontal are rejected. Needs adversarial live testing. |
| Screen recording | Hotkey, tray, Dock/HUD, CLI/protocol | `RecordingController`, `MediaFoundationRecordingEngine`, `RecordingPill` | Partial | Active-monitor and selected-region MP4 video-only paths exist. Mic audio, system audio, camera, click/keystroke overlays, GIF, trim/compress, and crash/disk-full recovery are planned. |
| File preview | Dock, shelf drop, `octadock open --filepath`, file associations | `FilePreviewService`, preview providers, `PreviewCardWindow`, `MarkdownRendering`, `ShelfWindow` | Partial | Images route to the image surface; CSV/TSV, JSON, log, Markdown, broad text/code/config, and fallback metadata cards exist. PDF, Office, archives, design files, syntax highlighting, universal writeback, source animation, and Ask AI remain planned. |
| File associations | Explorer Open With, image "Add to dock" verb | `FileAssociationRegistration`, `App.xaml.cs` | Built | Per-user HKCU registration. No settings toggle and not every file type has a rich preview. |
| Automation CLI/protocol | `octadock.exe`, `octadock://` | `CommandParser`, `CommandDispatcher`, IPC | Built | Settings toggles gate CLI/protocol dispatch before parsing. Protocol blocks microphone-starting/local-only commands such as dictation and quit. |
| Context Stack | Dock/tray/CLI, shelf/pin add-to-context, Context window | `ContextService`, `ContextRepository`, `ContextViewModel`, `ContextWindow`, `ContextExport` | Partial | Persistent packages, snapshot/reference ownership, package navigation, open item, delete, folder export, zip export, and manifest rules are built. Missing per-item UI toggles, notes/reorder polish, redaction, source integrations, export preview, AI, and MCP. |
| Dictation / STT | Dock mic action, global shortcut, CLI | `DictationController`, `DictationPill`, audio/STT providers, shortcuts, command parser | Partial | Parakeet TDT v3 default with model consent/manager, Whisper fallback, explicit `OCTADOCK_OPENAI_API_KEY` cloud opt-in, live partials, auto-stop, discard, and toggle/hold/both activation exist. Needs live device/language QA and Windows Speech fallback. |
| Read aloud | Tray, Dock, `Ctrl+Shift+0`, `octadock read` | `ReadAloudService`, `WindowsTtsProvider`, `ElevenLabsTtsProvider`, `SentenceChunker`, `ReadingPill` | Built | Verbatim by default with Windows voices; `--explain` shells out to the user's Codex/Claude CLI before TTS. Screen-discovery overlay remains planned. |
| Settings | Settings window | `SettingsWindow`, `SettingsViewModel`, `SettingsSections` | Partial | Capture, voice, library, system, Account & Billing, shelf size, local crash reports, clipboard, dictation, and read-aloud settings exist. AI/MCP/upload/association/key-manager settings are still missing. |
| Licensing/trial gate | First-run, Settings, tray, Dock, CLI/protocol activation | `LicenseStateService`, `LicenseGate`, `ActivationService`, settings/tray UI | Built | 14-day trial, signed entitlement outside DB, activation UI/deep link, and service-seam gates are wired. Production host/KMS/Stripe/legal are external gates. |
| License service / admin health | HTTP service under `services/license-service` | `Octadock.LicenseService`, repositories, Stripe/signing services | Partial | Webhook, activation, trust anchor, admin health, alerts, and optional Stripe reconciliation source are built. Full admin dashboard, production auth, live keys, support email, and legal/commercial operations are not complete. |
| Update check | Core update service | `ReleaseVersion`, `UpdateManifest`, `UpdateCheckService` | Prepared | Signed-manifest verification exists, but the manifest host, signing key, UI flow, and auto-update installer are not built. |
| Crash reporting | Settings, exception handlers | `CrashReportService`, `App.xaml.cs` | Built | Saves local redacted JSON reports only when enabled. No upload/telemetry transport exists. |
| Clipboard history | Hotkey, tray, Dock, CLI/protocol, Settings | `ClipboardMonitor`, `ClipboardHistoryService`, `ClipboardHistoryWindow`, repository | Built | Local text/image clips with provenance, dedupe, favorites, search/filter, copy/delete/clear, and trimming. Shelf cards for clips remain planned. |
| Text transforms | Tray, `octadock open-text-tools` | `TextTransforms`, `TextToolsWindow`, `TextToolsViewModel` | Built | Local transform toolbox is wired; diff-two-clips remains open. |
| Upload/share | Command/action model | `PostCaptureAction.Upload`, docs | Planned | No upload providers yet. |
| Command palette | Roadmap | HUD/command parser foundation | Planned | Extend all-in-one HUD into a fuzzy launcher. |
| Code screenshot beautifier | Roadmap | Annotation/export foundation | Planned | Needs syntax highlighting and frame/padding presets. |
| Snippet/prompt library | Roadmap | n/a | Planned | Should integrate with paste-at-cursor and Context. |
| Ask AI / Send to AI | Roadmap | n/a | Planned | Requires provider settings, redaction, context model, cloud consent, and cost/session boundaries. |
| MCP server | Roadmap | command/service foundation | Planned | Expose local Octadock context after permission/redaction design. |
| Advanced recorder | Roadmap | recording foundation | Planned | System audio, GIF, camera overlay, click/keystroke display, trim/compress. |
| Distribution | Release script, website placeholders | `build/release.ps1`, `web/`, version files | Partial | Self-contained single-file win-x64 zip packaging exists. Installer/MSIX, code signing, SmartScreen, auto-update host, live DNS/download URL, and clean-VM release matrix remain open. |

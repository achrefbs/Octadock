# Octadock Capabilities Matrix

Last audited: 2026-07-03

Status legend:

- **Built** - implemented and wired in the app.
- **Partial** - user-visible or code-backed, but missing promised behavior.
- **Prepared** - code shape exists, but no complete user-facing flow.
- **Planned** - roadmap only.

| Capability | Entry points | Main code areas | Status | Limitations / next work |
| --- | --- | --- | --- | --- |
| Tray utility | Startup, tray icon, settings | `App.xaml.cs`, `TrayIconController`, settings | Built | Native Windows Forms tray menu path is confirmed working by the user on 2026-07-03. |
| Permanent Dock | On-screen Dock capsule | `DockPill`, `HudService`, `HudWindow` | Built | Capture-to-AI routing and dock/shelf merge are not complete. |
| Area capture | Hotkey, Dock, tray, CLI/protocol | `CaptureCoordinator`, `RegionSelectionService`, `CaptureEngine` | Built | Keep testing mixed-DPI overlays. |
| Previous-area capture | Hotkey, CLI/protocol | `CaptureCoordinator`, command parser | Built | Falls back to selection when no previous area exists. |
| Fullscreen capture | Hotkey, Dock, CLI/protocol | `CaptureCoordinator`, `CaptureEngine` | Built | Single monitor prefers WGC; all-monitor uses GDI. |
| Window capture | Hotkey, Dock, CLI/protocol | `WindowPickerOverlay`, `WindowPicker`, `CaptureEngine` | Built | Picker overlays are per-monitor for mixed-DPI placement; WGC falls back to GDI/PrintWindow when needed. |
| Self-timer capture | CLI/protocol, command path | `CaptureCoordinator`, command parser, `CaptureCountdownPill` | Built | Shows a transient countdown pill after region selection and hides it before capture. |
| Capture Shelf | Shelf window/cards | `ShelfService`, `ShelfViewModel`, `ShelfItemViewModel` | Built | Shelf accepts generated/external items through commands; drop-in UX still needs polish. |
| Video shelf cards | Recording stop flow | `RecordingController`, `ShelfItemViewModel`, `ShelfItemView` | Built | Videos cannot be annotated or pinned yet. |
| Clipboard copy | Shelf/history/editor/pins | `WpfClipboardService` | Built | Clipboard failures need stronger surfaced error states. |
| Drag-out | Shelf/editor/pins | Shelf/editor/pin view models | Built | Temp export cleanup needs audit. |
| Local history | History window, retention timer | `HistoryViewModel`, repositories, `RetentionService` | Built | Includes type/search/deleted/date filters; retention edge cases still need polish. |
| Annotation editor | Shelf/history/file/clipboard | `AnnotationEditorWindow`, `EditorViewModel`, renderer | Built | Embedded image annotation object is modeled but not a complete tool. |
| Floating pins | Shelf/history/file/clipboard | `PinService`, `PinWindow`, `PinRepository` | Built | Restored pins are clamped/gatherable after monitor layout changes; close races need continued regression checks. |
| OCR | Hotkey, Dock/HUD, CLI/protocol, file | `OcrService`, `OcrHistoryRecorder`, `WindowsMediaOcrProvider` | Partial | Interactive region OCR, fixed-coordinate OCR, and file OCR work; region grabs now create `OcrSource` history rows with recoverable text ("Copy Text" in History). File OCR does not create history rows yet. |
| Scrolling capture | Dock/HUD, CLI/protocol | `ScrollingCaptureEngine`, `ScrollingSessionPill` | Partial | Manual vertical only; auto-scroll and horizontal are rejected. |
| Screen recording | Hotkey, tray, Dock/HUD, CLI/protocol | `RecordingController`, `MediaFoundationRecordingEngine`, `RecordingPill` | Partial | Active-monitor, command-selected/fixed-region, tray selected-area, and HUD selected-area video-only paths exist; start/stop failures clean incomplete MP4 outputs before notifying; mic audio, system audio, camera, crash/shutdown recovery, and trim/compress are planned. Default output uses managed storage unless a save directory is configured. |
| File preview | Dock, shelf drop, `octadock open --filepath`, file associations | `FilePreviewService`, preview providers, `PreviewCardWindow`, `MarkdownRendering`, `ShelfWindow` | Partial | CSV/text/image/fallback plus JSON pretty-print, log tail, and rendered Markdown previews; brand-teal glass chrome, custom preview scrollbars/context menu, provider-aware badges, confirmation, copy/open/export actions, shelf file-drop entry, image add-to-shelf, and image pinning exist; Ask AI and syntax-highlighted code remain planned. |
| File associations | Explorer Open With | `FileAssociationRegistration`, `App.xaml.cs` | Built | Always-on for previewable file types; no settings toggle yet. |
| Automation CLI/protocol | `octadock.exe`, `octadock://` | `CommandParser`, `CommandDispatcher`, IPC | Built | Settings toggles gate CLI/protocol dispatch before parsing; help/docs must stay aligned with real dispatch behavior. |
| Dictation / STT | Dock mic action, global toggle hotkey, CLI | `DictationController`, `DictationPill`, `AudioCaptureService`, speech providers, shortcuts, command parser | Partial | Local Parakeet TDT v3 is the default engine (sherpa-onnx int8, ~0.06 RTF, punctuation/casing, 25 EU languages, resumable SHA-256-verified download, whisper-for-now stopgap while it fetches); local Whisper (`small` default) covers the 99-language tail with per-utterance routing for out-of-coverage languages; opt-in OpenAI transcription is selectable with an API key; configurable toggle hotkey defaults to `Ctrl+Shift+2`; `octadock dictation` toggles locally while `octadock://dictation` is blocked; live partials via embedded Silero VAD (stable/volatile transcript on the pill, discard button, optional auto-stop on silence); activation modes toggle/hold/both (`speech.activationMode`) with hold-to-talk via an opt-in LL keyboard hook. |
| Settings | Settings window | `SettingsWindow`, `SettingsViewModel`, `SettingsSections` | Partial | Speech settings, shelf size, local crash-report settings, and the Clipboard history tab are wired; Ask AI/TTS/MCP/upload settings still missing. |
| Crash reporting | Settings window, global exception handlers | `CrashReportService`, `GeneralSettings.CrashReportingEnabled`, `App.xaml.cs` | Built | Saves local redacted JSON reports under `CrashReports` only when enabled; no upload/telemetry transport exists. |
| Upload/share | Command/action model | `PostCaptureAction.Upload`, docs | Planned | No upload providers yet. |
| Clipboard history | Hotkey (`Ctrl+Shift+9`), tray, Dock "Clip", `octadock open-clipboard-history`, Settings Clipboard tab | `ClipboardMonitor`, `WpfClipboardSnapshotSource`, `ClipboardHistoryService`, `ClipboardHistoryWindow`, `ClipboardClipRepository` | Built | Local-only WM_CLIPBOARDUPDATE monitor with password-manager format exclusion and own-write suppression; text + image clips with provenance, dedupe/seen counts, favorites, search/filters, copy/delete/clear, and cap-based trimming. Shelf cards remain planned. |
| Command palette | Roadmap | HUD/command parser foundation | Planned | Extend all-in-one HUD into fuzzy launcher. |
| Text transforms | Tray "Text Tools", `octadock open-text-tools` | `TextTransforms` (Core), `TextToolsWindow`, `TextToolsViewModel` | Built | 28 local transforms: JSON format/minify, Base64/URL/HTML, JWT decode, identifier casing, MD5/SHA hashes, Unix timestamps, and line utilities with live output and chaining. |
| Code screenshot beautifier | Roadmap | Annotation/export foundation | Planned | Needs syntax highlighting and frame/padding presets. |
| Snippet/prompt library | Roadmap | n/a | Planned | Should integrate with paste-at-cursor and AI context. |
| Context Shelf | Roadmap | Shelf/history/OCR foundation | Planned | Bundle captures, OCR, files, clips, prompts, and logs. |
| Ask AI / Send to AI | Dock disabled slot, roadmap | n/a | Planned | Requires provider settings, redaction, context model, and opt-in cloud rules. |
| MCP server | Roadmap | command/service foundation | Planned | Expose local Octadock context to Cursor/Claude Code/Codex after permission/redaction design. |
| Advanced recorder | Roadmap | recording foundation | Planned | System audio, GIF, camera overlay, click/keystroke display, trim/compress. |
| Distribution | Release script | `build/release.ps1`, version files | Partial | Framework-dependent zip packaging exists; installer, signing, auto-update, public beta docs, and opt-in telemetry/upload remain planned. |

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
| Permanent Dock | On-screen Dock capsule | `DockPill`, `HudService`, `HudWindow` | Built | Dock AI action now opens Active AI Sessions; capture-to-AI routing and dock/shelf merge are not complete. |
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
| OCR | Hotkey, Dock/HUD, CLI/protocol, file | `OcrService`, `WindowsMediaOcrProvider` | Partial | Interactive region OCR, fixed-coordinate OCR, and file OCR work; OCR history rows are not produced yet. |
| Scrolling capture | Dock/HUD, CLI/protocol | `ScrollingCaptureEngine`, `ScrollingSessionPill` | Partial | Manual vertical only; auto-scroll and horizontal are rejected. |
| Screen recording | Hotkey, tray, Dock/HUD, CLI/protocol | `RecordingController`, `MediaFoundationRecordingEngine`, `RecordingPill` | Partial | Active-monitor, command-selected/fixed-region, tray selected-area, and HUD selected-area video-only paths exist; start/stop failures clean incomplete MP4 outputs before notifying; mic audio, system audio, camera, crash/shutdown recovery, and trim/compress are planned. Default output uses managed storage unless a save directory is configured. |
| File preview | Dock, shelf drop, `octadock open --filepath`, file associations | `FilePreviewService`, preview providers, `PreviewCardWindow`, `ShelfWindow` | Partial | CSV/text/image/fallback, draggable Windows glass chrome, custom preview scrollbars/context menu, provider-aware badges, confirmation, copy/open/export actions, shelf file-drop entry, image add-to-shelf, and image pinning exist; Ask AI is planned. |
| File associations | Explorer Open With | `FileAssociationRegistration`, `App.xaml.cs` | Built | Always-on for previewable file types; no settings toggle yet. |
| Automation CLI/protocol | `octadock.exe`, `octadock://` | `CommandParser`, `CommandDispatcher`, IPC | Built | Settings toggles gate CLI/protocol dispatch before parsing; help/docs must stay aligned with real dispatch behavior. |
| Dictation / STT | Dock mic action, global toggle hotkey, CLI | `DictationController`, `DictationPill`, `AudioCaptureService`, speech providers, shortcuts, command parser | Partial | Local Whisper now defaults old tiny/base settings to `small`; opt-in OpenAI transcription is selectable with an API key; configurable toggle hotkey defaults to `Ctrl+Shift+2`; `octadock dictation` toggles locally while `octadock://dictation` is blocked; no hold-to-talk, live partials, or Windows speech fallback yet. |
| Settings | Settings window | `SettingsWindow`, `SettingsViewModel`, `SettingsSections` | Partial | Speech settings, shelf size, local crash-report settings, and Active AI Sessions overlay controls are wired; Ask AI/TTS/MCP/upload/clipboard-history settings still missing. |
| Crash reporting | Settings window, global exception handlers | `CrashReportService`, `GeneralSettings.CrashReportingEnabled`, `App.xaml.cs` | Built | Saves local redacted JSON reports under `CrashReports` only when enabled; no upload/telemetry transport exists. |
| Upload/share | Command/action model | `PostCaptureAction.Upload`, docs | Planned | No upload providers yet. |
| Clipboard history | Persistence foundation | `ClipboardClipRecord`, `ClipboardClipRepository`, SQLite migration | Prepared | Core/Data can store text clips and image metadata paths; no clipboard monitor, UI/search restore flow, hotkeys, shelf cards, or private clipboard format exclusion yet. |
| Command palette | Roadmap | HUD/command parser foundation | Planned | Extend all-in-one HUD into fuzzy launcher. |
| Text transforms | Roadmap | n/a | Planned | JSON/Base64/JWT/case/hash/timestamp mini-tools. |
| Code screenshot beautifier | Roadmap | Annotation/export foundation | Planned | Needs syntax highlighting and frame/padding presets. |
| Snippet/prompt library | Roadmap | n/a | Planned | Should integrate with paste-at-cursor and AI context. |
| Context Shelf | Roadmap | Shelf/history/OCR foundation | Planned | Bundle captures, OCR, files, clips, prompts, and logs. |
| Ask AI / Send to AI | Dock disabled slot, roadmap | n/a | Planned | Requires provider settings, redaction, context model, and opt-in cloud rules. |
| Active AI Sessions | CLI run/watch/open/event, tray, Dock AI button, bottom-right overlay, process and Codex-state discovery, persistence foundation | `AiSessionCommandService`, `AiSessionDiscoveryService`, `AiSessionOverlayService`, `AiSessionRepository`, `AiSessionsWindow`, command parser | Partial | Generic `run`/`watch --pid` commands persist process status and exit, with toast notifications on completion/failure unless `--notify silent` is used; waiting/completion/failure notifications open the AI Sessions window when clicked; `run` captures bounded stdout/stderr timeline events plus full stdout/stderr log artifacts under `AiSessionLogs` and marks sessions as waiting when common output prompts appear; `ai-session-event` lets local hooks add events or status changes to existing sessions; startup/window/overlay refresh auto-discovers already-running Codex runtime sessions and Claude Code workers from Windows process metadata, plus active Codex Desktop threads/subagents from Codex's local state database and rollout logs, while ignoring desktop shells, helper/app-server processes, native-host bridges, stale open subagents, completed Codex rollout threads, stale active rollout heartbeats, and kept-alive Codex runtime workers whose latest workspace thread is complete, stale, or unknown; the tray/Dock/`open-ai-sessions` window shows recent sessions, details, timeline events, log-open actions, copy actions, and working-folder reveal; the passive overlay shows up to eight live/recent sessions plus completions it observed running, and Settings can disable the overlay or hide recent completions. No dock cards, Claude desktop state adapter, provider-specific hook adapters/waiting/log enrichment, or remote provider APIs yet. Protocol URLs for run/watch/event ingestion are blocked. |
| MCP server | Roadmap | command/service foundation | Planned | Expose local Octadock context to Cursor/Claude Code/Codex after permission/redaction design. |
| Advanced recorder | Roadmap | recording foundation | Planned | System audio, GIF, camera overlay, click/keystroke display, trim/compress. |
| Distribution | Release script | `build/release.ps1`, version files | Partial | Framework-dependent zip packaging exists; installer, signing, auto-update, public beta docs, and opt-in telemetry/upload remain planned. |

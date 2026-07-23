# Octadock codebase context (for agents)

Updated 2026-07-19 from a full code survey. If this file disagrees with code or
`docs/PROJECT-STATE.md`, they win — update this file.

## Solution layout

| Project | Role |
| --- | --- |
| `src/Octadock.App` | WPF shell: windows, tray, dock, view models, DI (`DependencyInjection/AppServiceCollectionExtensions.cs`) |
| `src/Octadock.Core` | Platform-free domain: command parser, preview providers, secret redaction, licensing, Context models, settings |
| `src/Octadock.Data` | SQLite (WAL) repositories; migrations in `Sqlite/SchemaMigrations.cs` — 9 versions; `ai_sessions` tables were added in v4 and dropped in v6 (removed feature — do not resurrect) |
| `src/Octadock.Platform.Windows` | Win32/WinRT: hotkeys, capture, Windows Graphics Capture, OCR, WASAPI audio, STT/TTS providers |
| `src/Octadock.Cli` | `octadock.exe`; same Core parser; forwards to the tray instance over a per-user named pipe |
| `services/license-service` | Isolated ASP.NET Core: Stripe webhooks, activation, Ed25519 entitlement signing; own solution and tests |
| `tools/internal/Octadock.WorkflowIntelligence.Internal` | Internal-only research assembly (agent-transcript corpus work); excluded from public artifacts by gate; governed by `docs/strategy/WORKFLOW-INTELLIGENCE-INTERNAL-ADDENDUM.md` |
| `tests/` | Core, App, Data, Platform.Windows, CLI (1,219 tests green on the current Phase 1 candidate) |

## User-facing surfaces and where they live

- **Dock capsule** `src/Octadock.App/CaptureUx/DockPill.cs` (permanent pill) ·
  **tray** `Tray/TrayIconController.cs` · **HUD** `CaptureUx/HudWindow.xaml` ·
  global hotkeys `Ctrl+Shift+1…0` (`Platform.Windows/Hotkeys/HotkeyService.cs`,
  defaults in Core `SettingsSections.cs`).
- **Capture pipeline** `Services/CaptureCoordinator.cs` + selection/window-picker
  overlays → **Shelf** (`CaptureUx/ShelfWindow.xaml`, `ShelfService.cs`) — the
  landing zone for every capture.
- **Pins and generic file preview were REMOVED** (2026-07-23, Phase 1 product
  decision, overriding the earlier freeze). The only image surface is the
  annotation editor, reachable for registered captures from Shelf/History.
  Removed CLI/protocol verbs (`pin`, `open`, `open-annotate`,
  `open-from-clipboard`, `add-shelf-item`) keep parse tombstones and fail
  truthfully; `pins` DB rows stay inert (no destructive migration); legacy
  per-user Explorer associations are unregistered at every startup.
- **Context packages** `Services/ContextService.cs`, `Context/` — curated file
  bundles: small files snapshotted, large ones SHA-256-referenced; folder/zip
  export with manifests; changed references fail closed.
- **Use with AI** `Ai/AgentWorkspaceViewModel.cs` + `AgentPacketBuilder` +
  `AgentCliRunner.cs` — builds a deterministic packet (TASK.md + manifest.json +
  SHA256SUMS) from Shelf/Pins/Context/History/Clipboard/OCR evidence, runs
  redaction (`Core/Ai/TextSecretDetector.cs`, **default ON**), shows the exact
  payload, then shells out to the user's own `claude` (read-only tools,
  no-session-persistence) or `codex` (ephemeral, sandboxed, no shell) CLI.
  There is no cloud API client and no stored key. ROADMAP C-01 removes the
  standalone-window positioning in favor of source-bound review;
  `AiActionsWindow` and the `ShowAiActions` seam are compatibility adapters.
- **Dictation** — local Parakeet default, local Whisper fallback; OpenAI STT
  only via `OCTADOCK_OPENAI_API_KEY`. **Read aloud** — Windows voices; optional
  ElevenLabs via env key. **OCR** — `Windows.Media.Ocr`, region/file.
- **History / Clipboard windows**, **Text Tools** (28 local transforms),
  **annotation editor** (`.octadock` project files), **image mockup**
  (`Ai/ImageMockupService.cs`, routed through the Codex CLI).

## Persistence

Everything under `%LOCALAPPDATA%\Octadock\`: `octadock.db` (SQLite WAL,
checkpoint on retention/exit) plus `Captures\yyyy\MM\dd\`, `Thumbnails\`,
`Projects\`, `Mockups\`, `Recordings\`, `Clipboard\`, `Context\<itemId>\`,
`TempExports\`, `Logs\`. Media paths are stored relative with forward slashes.
Settings are JSON blobs in the `settings` table (`Core/Services/Json/OctadockJson.cs`).

## Gotchas

- Floating Octadock windows exclude themselves from screen captures by default.
  Settings can include them in screen captures/desktop recordings, and the dev
  escape hatch `OCTADOCK_DISABLE_CAPTURE_EXCLUSION=1` forces inclusion.
- Mixed-DPI / multi-monitor is the #1 regression class for anything positioned
  on screen (dock, shelf, overlays, pills).
- Paid features gate through `Core/Licensing/LicenseGate.cs`; offline-trial
  hardening is pending founder decision B-04 — do not redesign trial state.
- The copy-honesty gate scans user-facing strings; Beta labels ("video only",
  "manual vertical") are enforced wording.
- The license service is a separate solution — desktop test runs do not cover it.

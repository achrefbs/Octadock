# Implementation examples — file preview (Quick Look) + STT

Status note 2026-07-03: these examples are historical scaffolding. Parts have
since been adapted into production code (`FilePreviewService`, preview
providers, `DictationController`, `AudioCaptureService`, `WhisperSttProvider`),
while other parts remain planned (source animation, Ask AI, hold-to-talk, live
partials, and Windows speech fallback).

Companion code for [file-preview-quicklook.md](../file-preview-quicklook.md)
and the STT section of [vibe-coding-toolkit-roadmap.md](../vibe-coding-toolkit-roadmap.md),
reviewed in [REVIEW-2026-07-02.md](../REVIEW-2026-07-02.md). These files live
under `docs/` so they are **not compiled**; each header says where it belongs
under `src/` and which NuGet packages it needs. They reference real codebase
types (`ToolWindowBase`, `IClipboardService`, `IStoragePaths`, notification and
settings services) so they drop in with minimal adaptation.

## File preview

| File | Target project | What it shows |
|---|---|---|
| `FilePreview/IFilePreviewProvider.cs` | Core | Provider contract, options, typed CSV model |
| `FilePreview/CsvPreviewProvider.cs` | Core | Streaming RFC-4180 parser, `;`-aware delimiter detection, es-ES-safe type inference, non-locking file share |
| `FilePreview/FilePreviewService.cs` | App | Provider selection, protocol-open confirmation + UNC block, reduced-motion/no-source-rect animation policy |
| `FilePreview/PreviewCardWindow.xaml.cs` | App | The Quick Look zoom-from-icon storyboard (scale+translate to identity), Genie/slide-up/spotlight variants, Esc/click-away dismissal |

Wiring: register providers + service in `AppServiceCollectionExtensions`
(`AddSingleton<IFilePreviewProvider, CsvPreviewProvider>()`, …); add an `open`
verb to `CommandTokens`/`CommandType` with a `filepath` parameter (parser
validation mirrors `add-shelf-item`); branch the shelf's existing drop handler
on extension. No new packages for phase 1.

## STT

| File | Target project | What it shows |
|---|---|---|
| `Stt/ISpeechToTextProvider.cs` | Core | Engine contract (+ optional streaming), canonical 16 kHz mono float `AudioBuffer`, dictation-dictionary options |
| `Stt/AudioCaptureService.cs` | Platform.Windows | WASAPI capture → resample, mic-privacy-denied detection, device-loss handling, HUD peak meter (**NAudio**) |
| `Stt/WhisperSttProvider.cs` | Platform.Windows | Model download with atomic install, resident `WhisperFactory`, per-utterance processors, dictionary post-pass (**Whisper.net + Whisper.net.Runtime**) |
| `Stt/DictationController.cs` | App | Toggle-mode flow, waveform HUD lifecycle, paste-at-cursor with clipboard save/restore and UIPI fallback to clipboard mode |

Wiring: add `HotkeyAction.Dictate` + a `dictate` command verb; register the
controller, capture service, and provider (behind a factory like OCR's) in DI;
`ms-settings:privacy-microphone` deep link is handled in the controller.
Packages (Central Package Management → `Directory.Packages.props`): `NAudio`,
`Whisper.net`, `Whisper.net.Runtime`.

Deliberate phase-1 choices, argued in the review: **toggle mode before
hold-to-talk** (WM_HOTKEY has no key-up; a low-level hook is phase 2), **CPU
runtime only** (GPU runtimes are large native deps), and **no live partials**
(Whisper is not natively streaming; the hybrid Windows-recognizer approach is
sketched via `IStreamingSpeechToTextProvider`).

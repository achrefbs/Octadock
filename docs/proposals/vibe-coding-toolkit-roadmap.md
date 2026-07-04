# Roadmap — Octadock as a vibe-coding toolkit

Status: historical proposal updated with implementation notes · 2026-07-03.
For the active plan, see `../ROADMAP.md`; for external research, see
`../research/VIBE-DEVELOPER-RESEARCH-2026-07-03.md`.

The goal: keep the capture core, and grow Octadock into the utility belt you reach for
while coding with an AI — capture, dictate, preview, transform, and feed things to a
model, all behind global hotkeys and a command HUD, all local-first. This doc covers
speech-to-text, text-to-speech, and a prioritized menu of other tools. It pairs with the
[file-preview proposal](file-preview-quicklook.md).

Everything here fits the existing spine: global hotkeys → a command (`octadock://` +
CLI) → a coordinator → a themed tool window / shelf card, with clipboard and pin reuse.
New capability = a new command verb + a service, not a new app.

## Speech-to-text (STT)

The headline feature. Flow: press a chord → a small HUD shows a live waveform → speak →
release → text lands at the cursor (or on the clipboard / shelf). Think "push-to-talk
for your editor and your AI prompt box."

Implementation note 2026-07-03: the first slice is now partially implemented as
dock or global-hotkey toggle dictation with WASAPI capture, local Whisper/OpenAI
provider selection, model download/cache, a dictation pill, code-term hints,
paste-at-cursor, local-only CLI toggle command, and speech settings for
model/language/provider/dictionary. Missing: hold-to-talk mode, live partials,
and Windows speech fallback.

### Engine choice

| Option | Accuracy | Privacy | Setup | Notes |
|---|---|---|---|---|
| **Local Whisper (recommended)** | High | Fully offline | One model download | `whisper.cpp` via the `Whisper.net` NuGet bindings; GPU/CPU; `base`/`small` are plenty for dictation |
| Windows built-in speech | Medium | Offline | Zero | `System.Speech` / WinRT `SpeechRecognizer`; weaker punctuation and code terms |
| Cloud (OpenAI/Deepgram/…) | Highest | Sends audio | API key | Best for long-form; optional, off by default |

Recommendation: **local Whisper** as the default to match Octadock's privacy-first
stance, with a cloud engine as an opt-in for users who want maximum accuracy. Keep the
engine behind an `ISpeechToTextProvider` so it's swappable.

```
ISpeechToTextProvider
  Task<SttResult> TranscribeAsync(AudioBuffer audio, SttOptions opts, CancellationToken ct)
WhisperSttProvider   (default, Whisper.net + a bundled/downloaded ggml model)
WindowsSttProvider   (zero-dependency fallback)
CloudSttProvider     (opt-in)
```

Capture audio with WASAPI (`NAudio` or WinRT `AudioGraph`). Add an `AudioCaptureService`
alongside the screen-capture engine.

### UX details that make or break it

- **Push-to-talk and toggle** modes. A hold-to-talk chord for quick bursts; a toggle for
  long dictation.
- **Insert modes:** type at cursor (simulate input / paste), copy to clipboard, or drop
  onto the shelf as a text item. Default to paste-at-cursor.
- **Live partials** if the engine supports streaming, so it feels responsive.
- **A dictation dictionary** for code terms ("camelCase", "arrow function", "async
  await") and custom replacements — this is what separates a coding STT from a generic
  one.
- **Language + model pickers** in settings; download models on demand with progress.
- Reuse capture-exclusion so the STT HUD never appears in a screen recording.

Ship phases: (1) push-to-talk → paste-at-cursor with local Whisper; (2) toggle mode +
clipboard/shelf targets + live partials; (3) dictionary/custom words + language picker.

## Text-to-speech (TTS)

Lower priority than STT but cheap to add and a nice symmetry. Flow: select text (or an
AI response) → chord → Octadock reads it aloud, with a floating mini-player
(play/pause/scrub/speed).

- **Default engine:** Windows WinRT `SpeechSynthesizer` (offline, free, decent voices)
  behind an `ITextToSpeechProvider`. Optional cloud voices (ElevenLabs/OpenAI) for
  quality, opt-in with an API key.
- **Good uses for a coder:** read long AI answers while your eyes stay on the editor;
  proofread prose by ear; audibly confirm a long-running command finished.
- Keep it modest: read-selection, read-clipboard, and a small transport UI reusing the
  pin window chrome.

## Other tools worth adding (prioritized)

Grouped by how much they lean on what Octadock already has. Effort is rough
(S/M/L). The top tier is where I'd start.

### Tier 1 — high value, strong reuse

- **Active AI Sessions / Agent Mission Control** (L, but strategic). Track
  local and remote coding-agent runs, show them in the Dock/Shelf, and notify
  when they need attention or finish. Start with `octadock run -- <command>` and
  `octadock watch --pid`, then add Claude Code hooks, Codex/Cursor/Copilot/Jules/
  Vercel adapters. Current code has the generic run/watch foundation, completion
  notifications, prompt-based waiting detection for wrapped runs, full logs for
  wrapped runs, local CLI hook event ingestion, and an AI Sessions window; dock
  cards, provider-specific hook adapters, provider-specific waiting/log
  enrichment, and adapters remain. This was formerly a small "agent-run
  monitor" note; it is now a core
  vibe-developer feature.
- **Clipboard history** (M). A searchable ring of recent clips (text + images), each
  restorable or draggable to the shelf. Pairs perfectly with capture + STT output. This
  is the single most-used "coding utility" feature in the category.
- **Code-screenshot beautifier** (M). Turn a code selection or a capture into a polished
  image (window frame, padding, theme, syntax highlight) for sharing in PRs/social —
  like carbon/ray.so, but from your existing capture flow. High "wow", reuses the
  annotation/export pipeline.
- **OCR-to-clipboard "grab text"** (S — partially built). Promote the OCR path to a
  first-class hotkey: select a region → text on clipboard. Grabbing text from a video,
  error dialog, or image is a daily coding need.
- **Command HUD → launcher** (M). Extend the existing all-in-one HUD into a fuzzy
  command palette: run any Octadock action, open recent files/previews, paste a snippet.

### Tier 2 — developer utilities

- **Snippet / prompt library** (M). Saved text/prompts with hotkeys and placeholders,
  pasted at cursor — great for reusable AI prompts and boilerplate.
- **"Ask AI about this"** (M). Run a local/hosted model over the current
  selection/clipboard/preview: explain, rewrite, translate, generate a regex, summarize.
  This is the connective tissue that makes the toolkit feel AI-native; it also powers the
  CSV card's "Explain this file."
- **Text transform toolbox** (S). Case convert, JSON/CSV/XML pretty-print and validate,
  Base64/URL/JWT decode, hash, timestamp convert, diff two clips. Fast, offline, no UI
  window needed beyond a shelf card.
- **Color picker / palette** (S). You already sample pixels in the selection loupe —
  promote it to a standalone eyedropper that copies hex/rgb and keeps a palette.

### Tier 3 — nice-to-have

- **Scratchpad / quick notes** (S) with a hotkey; notes become shelf items.
- **Window snap/layout manager** (L) — arrange windows into a coding layout.
- **GIF / screen recording** (L — partial) — selected-region video and partial
  failure cleanup are wired; finish audio work, then add GIF export for bug repros.
- **Measuring ruler + pixel inspector** (S) — on-screen dimensions for UI work.
- **Regex tester / cron explainer / markdown preview** (S each) as HUD mini-tools.

## A note on the shape of it all

The through-line: **capture something → transform it → hand it to your editor or your
AI.** STT and OCR are input; clipboard history, snippets, and file previews are
holding/inspection; the beautifier, transforms, and "ask AI" are output. Keeping each
new tool as a command verb + a service (rather than a bolt-on app) is what lets the HUD
launcher, hotkeys, shelf, and pins tie them together into something that feels like one
coherent toolkit.

# Octadock Build Plan

Status: **historical — superseded**
Last updated: 2026-07-04

The July 2026 product strategy removed passive screen understanding and
background discovery from the product direction. Use `../ROADMAP.md` for live
execution; do not implement milestones from this document without reapproval.

## Product Direction

Octadock is the rebranded continuation of the current SnapDock work. The product
should become a desktop companion for capturing, pinning, previewing, explaining,
recording, and acting on what is visible on the screen.

The near-term goal is not only to polish screenshot tooling. The broader goal is
to make the screen itself understandable: Octadock should discover useful regions
on the desktop, keep lightweight local context about them, and make those regions
available for capture, explanation, automation, or voice.

## Principles

- Prefer local-first behavior for capture, OCR, discovery, history, and caching.
- Use AI only when it adds understanding, not for every screen observation.
- Keep user intent explicit for networked model calls and text-to-speech.
- Make selection optional over time: the app should increasingly understand
  visible regions before the user draws a rectangle.
- Cache OCR, summaries, and generated audio by content hash so repeated reads are
  fast.
- Separate product abstractions from temporary provider choices. Codex/Claude CLI
  are a bridge, not the long-term architecture.

## Current Baseline

The new Octadock repo starts from the current SnapDock implementation, including:

- capture area/window/fullscreen/all-monitors flows;
- dock, tray, shelf, pins, history, annotation, file preview, OCR, recording,
  dictation, and automation;
- the first read-aloud slice using local Codex/Claude CLI for explanation and
  ElevenLabs for speech;
- Windows app icon and brand assets based on the supplied blue Octadock logo.

## Milestones

### M0 - Clean Rebrand

- Create a clean `Octadock` repo from the current working state.
- Rename solution, projects, namespaces, assembly names, CLI, IPC, protocol, and
  docs from SnapDock to Octadock.
- Keep transitional compatibility for existing `SNAPDOCK_*` environment
  variables where useful.
- Replace app icon assets with the Octadock logo.
- Add product/spec docs so the roadmap survives outside chat.
- Verify build and tests.

### M1 - Read-Aloud MVP Hardening

- Keep `octadock read` sources: `--text`, `--filepath`, `--clipboard`,
  `--area`, and prompted OCR region.
- Make user-facing errors clearer for missing AI CLI, missing ElevenLabs key,
  OCR failure, and empty text.
- Add settings UI for provider, voice, model, summary length, and privacy mode.
- Start speech faster by streaming ElevenLabs audio instead of waiting for a full
  MP3 file.
- Add cache entries for explanation text and synthesized audio.
- Add cancellation and current playback state to tray/dock UI.

### M2 - Fast Explanation Backend

- Replace Codex/Claude CLI as the default explanation path with a small low
  latency model API.
- Keep CLI providers as development fallback.
- Add provider abstraction for:
  - local OCR text in;
  - structured explanation out;
  - latency/cost metadata;
  - cache key material.
- Use model tiers:
  - rule/local heuristic for obvious UI text and short messages;
  - fast model for normal explanation;
  - larger model only for dense documents, code, or explicit deep explanation.

### M3 - Screen Discovery Layer

- Add a background discovery service that snapshots visible windows/regions only
  when needed.
- Build a local `ScreenMap` of OCR blocks, window metadata, regions, hashes, and
  semantic grouping.
- Use change detection so unchanged regions are not re-OCRed or re-summarized.
- Group text into explainable objects: paragraph, dialog, error, code block,
  table, form, notification, menu, chat/email thread, or file preview.
- Add a discovery overlay that can reveal understood regions on hotkey press.

### M4 - Magic Overlay Interaction

- Introduce a hold-to-reveal overlay with subtle outlines over explainable zones.
- Support hover/dwell/click actions:
  - explain;
  - read aloud;
  - copy text;
  - capture;
  - pin;
  - ask follow-up.
- Allow manual rectangle selection as fallback, not the primary interaction.
- Add "what am I looking at?" command for the active window or screen.

### M5 - Productionization

- Move keys and provider settings into secure settings storage or OS credential
  storage.
- Add privacy controls and provider audit log.
- Add telemetry-free local diagnostics for slow reads and failed providers.
- Create release packaging with Octadock protocol and file associations.
- Run UX polish and manual verification on multi-monitor, high DPI, and Windows
  10/11 setups.

## Open Questions

- Which low-latency model API should become the default explanation provider?
- Should screen discovery run continuously, on hotkey, on mouse pause, or by
  active-window change?
- How much OCR cache should be persisted between launches?
- Should generated speech files be retained, temp-only, or cache-managed?
- How should Octadock visually distinguish capture regions from explainable
  discovery regions?


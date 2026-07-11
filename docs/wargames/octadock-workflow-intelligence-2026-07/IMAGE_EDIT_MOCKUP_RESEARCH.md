# AI image-edit mockups in the Octadock workflow — research report

**Status:** Research only — no implementation authorization requested or granted
**Date:** 2026-07-10
**Companion to:** [`WARGAME_RESULT.md`](WARGAME_RESULT.md) (this feature is an *Enrich*-stage candidate in the Workflow Intelligence learning progression, §14.3 of the brief)
**Founder's concept:** When a design change is requested, an image model first applies it to a **real screenshot** as a visual mockup. The founder approves the look. The approved image then becomes the reference target for a coding agent to make the actual change, verified by recapture and comparison.

Interpretation note: "Seedream/SeedEdit" (ByteDance) and "GPT Image 2" were both confirmed as real, current models — `gpt-image-2` shipped 2026-04-21; SeedEdit was folded into Seedream's unified gen+edit line, currently Seedream 4.5 with Seedream 5.0 Pro released **two days ago** (2026-07-08).

---

## 1. Verdict

**Viable, novel, and unusually well matched to Octadock's shipped plumbing — with two hard truths that dictate the design.**

1. **No API image model preserves untouched pixels.** Every instruction-based editor (gpt-image-2, Nano Banana Pro/2, Seedream, hosted FLUX) regenerates the full canvas; even gpt-image-2's alpha masks are documented as "guidance rather than strict boundaries." Fonts re-render, spacing drifts, text can corrupt. The pipeline must therefore be built on **crop-edit-composite**: crop the change region, edit only that, paste the result back onto the byte-identical original. Octadock already owns every primitive this needs.
2. **The mockup is a decision artifact, not a spec.** Benchmarks (GEBench: ≤24% spatial-grounding accuracy even for the best model) show "move this element"-class edits land with dozens of pixels of jitter. The approved mockup should always travel with a human-approved **text delta** ("accent → #22D3EE, corner radius 8→12") as the binding spec; the image communicates intent.

Market context that strengthens the case: OpenAI productized almost exactly this loop inside Codex (gpt-image-2 + `$imagegen` skill; documented mockup → approve → implement → screenshot-compare workflow), and Google markets Nano Banana explicitly for "change a button color without distorting the surrounding layout." **But no product does this against a real screenshot of an existing native desktop app with provenance, region compositing, and verified implementation.** The nearest neighbors (Google Stitch, the nano-banana/Claude Code community workflow, Onlook) each implement half of it. The composition is unclaimed — and it is Octadock-shaped: capture provenance, region UI, deterministic diffing, and agent handoff are all shipped code.

**For WPF specifically, mockup-first beats real-change-preview-first** — there is no agent-drivable XAML hot reload (VS/Rider debugger-only), so a "real preview" costs a 1–5 min build+launch cycle versus 10–20 s per image edit. Exploring 3–5 design directions as images is 5–20× cheaper than one code iteration. Exception: for small, fully describable tweaks (recolor, padding bump), skip the mockup — go straight to the real-change loop.

---

## 2. Model landscape (as of 2026-07-10)

| | gpt-image-2 | Nano Banana Pro / 2 (Gemini) | Seedream 4.5 / 5.0 Pro | FLUX.2 (API / klein 4B local) | Qwen-Image-Edit-2511 (local) |
| --- | --- | --- | --- | --- | --- |
| Edit-arena rank (Jul '26) | **#1 (1466 Elo)** | #6 / #15 | ~#18–22 / too new | ~#24–34 | (API 2.0-pro #14) |
| Mask/region input | Alpha mask (guidance only) | **None** (prompt-only) | Prompt regions / **5.0 Pro: point+lasso+box, layers** (API exposure unverified) | Fill endpoints / **true inpainting locally** | **True inpainting (ComfyUI)** |
| Untouched pixels preserved | No | No (alignment drift documented; output dims may not match input) | Claimed; unverified | No / **Yes with pixel composite** | **Yes with pixel composite** |
| Small UI text | Best-in-class (marketed for UI) | Excellent (GEBench #1) but over-generation failures documented | Good | Middling / weak | Weak-moderate; can edit text preserving font |
| Max resolution | 3840×2160 (dims must be ×16) | 4K (fixed aspect list) | 4096² / 2048² edit | 4 MP | ~2K practical |
| Cost per edit | $0.04–0.35 | $0.13–0.24 / ~$0.07 | $0.04 / $0.07–0.14 | ~$0.03–0.06 / GPU | GPU only |
| Latency | ~15 s (low) to 3–5 min (high) — **use `medium`** | ~10–30 s / seconds | ~22 s median | ~10 s / sub-second | 40–80 s (RTX 4090 Q4, ~11–12 GB VRAM) |
| Data policy | No training; ~30-day retention; ZDR option | No training (paid); 55-day; ZDR option | **Unclear (BytePlus/fal routing unknown)** | BFL/fal ToS | **Fully local — zero egress** |

**Recommendation:**
- **Primary: `gpt-image-2`, `quality=medium`**, output sized to input (padded to ×16 — note 1920×1080 fails the rule; pad to 1920×1088), wrapped in crop-edit-composite. Arena #1 by ~78 Elo, best small-text story, always-on input fidelity, first-party data policy, and the vendor productized this exact use case.
- **Fast-iteration tier: Nano Banana 2** for cheap draft variants (seconds, ~$0.07); NB Pro for the final approval render. Accept documented alignment drift → composite mitigates.
- **Local/zero-egress: Qwen-Image-Edit-2511** (Apache 2.0) with masked inpainting + pixel composite — the only *guaranteed*-faithful path and the only one that never sends a screenshot off-device. Weaker instruction-following on UI semantics; fine as the privacy-preferred option.
- **Watch (4–6 weeks): Seedream 5.0 Pro** — first API model designed around region-precise editing (point/lasso/box + layer separation, "rest of frame intact"), half gpt-image-2's price. 48 hours old, unproven, and ByteDance data governance is the weakest of the four; re-evaluate once independent tests land and only via a US-terms provider (fal) with the crop-first pattern limiting exposure. Also watch Meta `muse-image` (#2 on the edit arena this week).

---

## 3. Proposed pipeline, mapped to shipped Octadock plumbing

```text
1. Capture           → exists (CaptureCoordinator; physical-pixel, DPI-stamped)
2. Select region +   → annotation editor is the natural UI (rect/crop tools exist;
   type instruction    EditorCanvas + inline text)
3. Crop + margin     → SkiaSharp primitives exist
4. Image-edit API    → NEW: HTTP client following the STT/TTS precedent
                       (OCTADOCK_* env key, static HttpClient, 90 s timeout)
                       + Agent-Workspace-style destination-named consent modal
5. Composite back    → NEW (small): paste edited crop onto byte-identical original
6. Drift disclosure  → exists: SkiaVisualComparisonService diffs original vs mockup
                       and renders a heat map of EVERYTHING the model changed —
                       including what you didn't ask for
7. Approve variant   → NEW: variant pairing on the Shelf (no linked-artifact concept
                       today; precedent: CaptureRecord.ProjectPath, action rows)
8. Handoff to agent  → exists: Agent Packet bundle; Codex gets `--image` flags,
                       Claude reads bundle-relative assets; attach original +
                       approved mockup + text delta; limits (32 sources, 256 MB) fine
9. Implement         → OUTSIDE Octadock: founder's own Claude Code/Codex session.
                       Agent Workspace profiles are deliberately read-only; the
                       write-enabled execution this leg wants is explicitly deferred
                       in AGENT-WORKSPACE.md. Octadock prepares; it does not write.
10. Verify           → recapture same region (NEW: region geometry must be persisted —
                       CaptureRecord has no coords today; this also unblocks the
                       already-deferred "automatic same-region recapture")
                       then TWO checks with TWO baselines:
                       (a) deterministic pixel diff vs ORIGINAL real screenshot
                           → proves only the intended region changed (shipped code)
                       (b) VLM judge vs APPROVED MOCKUP → "list concrete deviations"
                           (never pixel-diff against an AI render — categorically fails)
```

**Step 6 is the differentiator.** Model drift is the known failure of this whole product category; Octadock's deterministic diff turns it into a reviewable artifact at approval time: *"the model also changed these 214 pixels outside your request."* Nobody else surfaces that, and it is shipped code pointed at a new pair of images.

A second novel, cheap trust check from shipped code: **OCR-drift detection** — run the existing OCR over original and mockup *outside* the edited region; any text difference means the model corrupted labels it should not have touched. Deterministic, local, free.

### Verified gaps (net-new work if this proceeds)

| Gap | Size | Notes |
| --- | --- | --- |
| Image-edit API client + consent modal | Small | STT/TTS HTTP precedent + Agent Workspace confirmation precedent |
| Crop-edit-composite service | Small | Skia; must handle ×16 padding, DPI, and output-dims-≠-input (NB) |
| Mockup-as-variant linking (Shelf pairing, before/after presentation) | Medium | No derived-image concept exists; nearest precedent is the annotation project link |
| Region geometry persistence on CaptureRecord | Small-medium | Schema addition; also unblocks deferred same-region recapture; privacy note: window-relative coords preferable to absolute screen coords |
| VLM-judge verification step | Small | Can ride the existing agent CLI path (Claude/Codex read both images, return deviations); treat judge output as advisory, not a ship gate |
| "Design" workflow in Agent Workspace | Medium | Sixth outcome-first workflow beside Build/Investigate/Verify/Extract/Handoff |

---

## 4. Fit with Workflow Intelligence and product strategy

- **Strategy-compatible as designed.** Unlike the observation layer, this feature is *explicit-action*: the founder captures, selects a region, types an instruction, approves, sends. It sits squarely inside "Explicit over ambient" and the existing reviewable-payload principle. No consent-model change is required beyond the standard cloud-egress disclosure.
- **As an Enrich stage:** the wargame's learning loop (screenshot → agent → XAML → build → capture → compare) gains an optional pre-implementation gate. Workflow traces would record `mockup.requested/generated/approved/rejected` events; the approve/reject stream is *labeled preference data* for which design suggestions the founder actually accepts — exactly the internal evidence the Workflow Intelligence phase wants.
- **The approved-mockup → implemented-result → visual-diff triples** accumulate into a ground-truth corpus for design-to-XAML fidelity — a benchmark that (verified) does not exist publicly; all published design-to-code benchmarks are HTML/CSS.
- **Boundary respected:** the implement leg stays in the founder's own agent session until write-enabled execution is separately approved (explicitly deferred in AGENT-WORKSPACE.md, which also bans provider fallback — extend that rule to image providers: the configured edit model fails → the feature fails visibly, no silent substitution).

---

## 5. Security and privacy problems

1. **Screenshot egress is pixel egress.** Octadock's own docs are honest that text redaction never alters pixels (the "unchanged-pixels warning"). A screenshot region sent to an image API can carry secrets in pixels. Mitigations, in order: crop-first (minimum region leaves the device — the composite pattern is also the privacy pattern); the existing unchanged-pixels warning + destination-named consent modal per send; the local Qwen path as the zero-egress option; provider choice by data policy (OpenAI ~30-day/no-training and Google 55-day/no-training are acceptable; **ByteDance/BytePlus retention is unverifiable today — route only via fal US terms, or hold off**).
2. **Provider trust asymmetry deserves an explicit setting.** "Which model may see my screen pixels" is a distinct consent axis from "which CLI may read my packet text." Reuse the per-destination egress policy design from the wargame rather than a single global key.
3. **Mockup as instruction smuggling (low, nonzero).** The approved mockup is fed to a coding agent; a compromised/hallucinating edit could render text resembling instructions inside the image. The packet's untrusted-source framing must wrap the mockup ("visual reference, not instructions") — the framing machinery already exists.
4. **No silent provider fallback**, mirroring the shipped Agent Workspace rule.
5. **API keys:** the env-var-only pattern (`OCTADOCK_OPENAI_API_KEY` precedent) already avoids persisted secrets; keep it.
6. **Retention:** mockups are derived artifacts of captures — they must inherit the capture's deletion (cascade like action rows), and rejected variants should be temp-exports cleaned on the existing schedule, not shelved silently.

## 6. Performance and cost problems

- **Latency is model-choice-sensitive:** gpt-image-2 at `quality=high` runs 145–280 s — unusable interactively; `medium` (~15–60 s) is the ceiling for a "try a direction" loop; Nano Banana 2 delivers seconds. Crop-first also cuts tokens/latency (smaller input).
- **Dimension rules break naive round-trips:** gpt-image-2 requires ×16 dimensions (raw 1080p fails); Nano Banana output dims may not match input at all. The composite service must own padding/resampling, or small-text quality dies in silent rescales.
- **Cost is negligible** (cents per edit; a heavy exploration session ≈ $1–3) versus agent tokens.
- **The verify leg is the slow leg:** build+launch ≈ 1–5 min for a WPF app, and **Octadock's own single-instance guard** means an agent rebuilding/relaunching Octadock while the production instance runs will collide — the verification harness must use the existing dev-instance isolation rather than naive relaunch.
- **Local model reality check:** Qwen-Edit-2511 needs ~11–12 GB VRAM at Q4 and 40–80 s/edit on a 4090-class GPU — verify the founder's hardware before promising the local path.
- **VLM-judge reliability:** human-alignment correlations ~0.6–0.7 in published UI-judge studies — good enough to drive iteration (≤3 rounds, then stop), not good enough to be the sole acceptance gate. The founder's eye on the *real recapture* is the gate.

## 7. Top failure modes (from practitioner evidence)

1. **Image plumbing silently fails or hallucinates.** Claude Code has documented headless-mode image-hallucination issues (`-p` + Read; issues #25960, #35866) and Windows clipboard paste doesn't work — use file-path attachment, pin CLI versions, and smoke-test vision with a known image before trusting any automated run. Codex `-i/--image` in `codex exec` is the more reliable headless channel today.
2. **Mockup drift becomes spec drift.** The implementer chases details the model invented; pixel verification can never pass. Countered by: crop-edit-composite, the drift heat map at approval time, and the text delta as the binding spec.
3. **The desktop loop thrashes.** Without deterministic capture (fixed window size, DPI, theme) and a machine-checkable stop condition, agents declare victory on "looks done" or burn rounds on environmental noise. The recapture harness must pin window geometry/theme, and iteration must be capped.

---

## 8. Experiment plan (cheapest-first)

| # | Hypothesis | Experiment | Gate | Kill |
| --- | --- | --- | --- | --- |
| E0 (zero code, ~1 day) | The loop is useful at all | Run 3 real Octadock design changes manually: capture with Octadock → mock up via Codex `$imagegen` (gpt-image-2) → approve → implement in the founder's own agent session → recapture + existing visual compare | Founder: mockups materially improved decision or implementation on ≥2 of 3 | Mockups mislead or add no value → stop everything |
| E1 (script-level) | Model choice matters and is measurable | 10 real Octadock screenshots × 4 edits × {gpt-image-2 medium, NB 2, NB Pro, Seedream 4.5, local Qwen if GPU allows}; measure with shipped tools: OCR-text drift outside region, pixel drift outside region (visual compare), human accept rate, latency | A model achieves ≥70% founder-accept with zero text corruption outside the region | All models corrupt out-of-region text even with crop-first |
| E2 | Crop-edit-composite makes drift a non-issue | Prototype crop→edit→composite + drift heat map presentation | Out-of-region drift = 0 by construction; in-region accept rate holds | Composite seams/rescale artifacts unacceptable |
| E3 | Reference-driven implementation converges | Packet with original + approved mockup + text delta → agent implements → pinned-geometry recapture → dual verification (diff vs original, VLM judge vs mockup) | ≤3 iterations to founder-accepted real result on 3 of 4 tasks | Loop exceeds 3 rounds routinely, or judge hallucinations pass fiction |
| — | Prereq work item | Region geometry persistence on captures (also unblocks deferred same-region recapture) | — | — |

**Recommended first decision:** run E0 this week — it needs zero Octadock code and directly tests the founder's imagined experience with the exact model OpenAI built for it. Build nothing until E0 says the experience is worth owning natively.

---

## 9. Sources

Model docs/benchmarks: OpenAI gpt-image-2 model page + image-generation guide + Codex "idea to proof of concept"; Google Nano Banana 2/Pro blogs + Gemini image API docs + ZDR docs; ByteDance Seedream 4.5 page + fal 5.0 Pro edit page; BFL FLUX.2/klein blogs + HF; Qwen-Image-Edit-2511 HF/GitHub; LMArena Image Edit leaderboard; GEBench (arXiv 2602.09007); VDE Bench (2602.00122); Design2Code (2403.03163) and successors; "MLLM as a UI Judge" (2510.08783); UI2Code^N (2511.08195). Loop mechanics: Claude Code best-practices + headless docs + issues #25960/#35866/#26679; Codex CLI reference (`-i/--image`, `exec`); paddo.dev nano-banana workflow; Google Stitch; Onlook; FlaUI/WinApp MCP; Applitools Images SDK; XAML Hot Reload docs (VS + Rider 2026.2 EAP). Repo facts: AgentCliRunner.cs, AgentWorkspaceSupport.cs, SkiaVisualComparisonService.cs, CaptureRecord.cs/CaptureMapper.cs, AnnotationEditorWindow.xaml.cs, OpenAiSttProvider.cs, ElevenLabsTtsProvider.cs, OcrHistoryRecorder.cs (paths and line numbers in the research transcript).

# Octadock stabilization and remediation plan

## Goal

Make the current product trustworthy, fast, and visually coherent before adding another major workflow. The sequence is security boundaries first, then responsiveness and reliability, then a unified window system and product polish.

## Release gate

Do not call the current build launch-ready until all four high-severity findings are fixed and regression-tested, the ten medium findings have either been remediated or explicitly accepted with a documented rationale, and the main capture-to-shelf-to-pin loop passes a clean-machine smoke test.

## Wave 0 — Baseline and reproduction

1. Freeze new feature work for this pass.
2. Record CPU, working set, GPU usage, handle count, startup time, idle cost, and capture latency on a representative machine.
3. Add reproducible tests for the reported Shelf duplicate/discard/history behavior, short-image overlay geometry, centered image opening, and Codex CLI launch/sign-in behavior.
4. Establish screenshot baselines for Dock, Shelf, floating preview, Settings, Context, History, and Clipboard at normal and high Windows scaling.
5. Turn the security report into a tracked checklist with one test requirement per finding.

## Wave 1 — High-severity security fixes

1. **Attachment redaction contract:** stop presenting binary/image attachments as redacted. Either omit them from redacted packets or label them clearly as unchanged pixels and require explicit confirmation. Add packet-manifest and UI tests.
2. **Model streaming limit:** enforce the pinned maximum size while streaming, abort immediately when exceeded, delete the partial file, and test chunked/unknown-length responses.
3. **Native model integrity:** pin and verify the digest of `tokens.txt` and every native model companion file before load. Fail closed and give the user a recoverable re-download path.
4. **Crash-report privacy:** replace prefix-only redaction with deny-by-default path/secret handling. Include allowlisted diagnostic fields only; add adversarial tests for secrets and user paths in arbitrary positions.

## Wave 2 — Medium security and consent fixes

1. Require authentication for `/admin/health`; reduce public `/health` to a minimal liveness response.
2. Remove license keys from webhook responses and response logs.
3. Protect trial state from simple deletion/rewriting and define the offline policy explicitly.
4. Prevent protocol activation from replacing a current entitlement without a visible confirmation and identity comparison.
5. Treat `.url` and equivalent active content as executable-risk files.
6. Replace the fixed AI delimiter with a collision-resistant framed protocol or structured input channel.
7. Broaden secret detection to namespaced assignments and add a corpus of bypass tests.
8. Constrain AI CLI child processes to the reviewed input and the minimum required filesystem/shell authority.
9. Make clipboard-history capture opt-in during first run, with a clear retention explanation and one-click pause/clear controls.

## Wave 3 — Performance and reliability

1. Profile before optimizing; identify the top three causes of desktop freezing.
2. Ensure idle windows do no continuous animation, polling, OCR, image decode, or database work.
3. Bound image dimensions, decoded pixel counts, clipboard payload sizes, provider responses, archives, and preview files before allocation or decode.
4. Add cancellation and disposal around capture, OCR, speech, thumbnail generation, and AI child processes.
5. Virtualize History and Clipboard lists; load thumbnails asynchronously and cache with a strict budget.
6. Audit event subscriptions, timers, hooks, bitmap lifetimes, and window ownership for leaks.
7. Move database/file I/O off the UI thread while preserving ordered writes.
8. Add a lightweight internal diagnostics view for frame stalls, queue depth, memory, and recent operation durations; keep it developer-only.

## Wave 4 — Core workflow correctness

1. Make Shelf identity stable so dragging an existing capture back cannot duplicate it.
2. Make swipe-to-discard threshold-based and immediate on intent, with a short undo affordance; discarded items remain visible in History with a discarded state.
3. Fix short/wide/tall screenshot sizing so chrome never exceeds or collides with the image; align radii and clip boundaries.
4. Center opened images in the usable monitor work area and constrain them fully on-screen at every DPI.
5. Finish the Shelf visibility control with the eye affordance and the configured collapse/move behavior.
6. Fix Codex CLI discovery by resolving the signed-in executable/environment exactly as an interactive terminal does, then surface the real stderr rather than a generic sign-in message.

## Wave 5 — Unified window system

Use the floating image preview, Shelf, and Dock capsule as the source language. Build shared tokens/components before restyling individual windows:

1. window surface, shadow, border, and corner system;
2. title/header geometry;
3. compact icon button and tooltip behavior;
4. section, row, segmented control, toggle, and destructive action;
5. typography, spacing, empty/loading/error states;
6. entrance/exit and resize motion with reduced-motion support.

Then migrate in this order:

1. Settings — compact native control surface, not a web dashboard.
2. Context — creation without an awkward inline name row; clearer package contents and export review.
3. History — visual library with fast recovery, filtering, and discarded-item state.
4. Clipboard — dense, private, quickly scannable, with pause/clear controls.

## Wave 6 — Release validation

1. Full unit/integration suite plus focused security regressions.
2. Clean Windows 10/11 installation and upgrade test.
3. 100%, 125%, 150%, and 200% DPI coverage across multiple monitors.
4. Keyboard, screen reader, reduced motion, high contrast, and touchpad gesture checks.
5. Long-run soak test covering repeated capture, drag, discard, history recovery, OCR, dictation, and pin/unpin.
6. Re-run the standard security scan and compare findings; no high findings may remain.
7. Publish measured before/after performance numbers rather than subjective “feels faster” claims.

## Deferred until stabilization is complete

- Signal Lens or ambient reading/summarization.
- Passive session understanding or background screen monitoring.
- New AI windows or workflow dashboards.
- Audio screen recording.
- Hosted collaboration, hosted sharing, or new cloud providers.

The existing AI direction should remain explicit and invisible until invoked: use the user's selected CLI, show the actual outbound boundary, and return results inside an existing Octadock surface rather than creating another permanent window.

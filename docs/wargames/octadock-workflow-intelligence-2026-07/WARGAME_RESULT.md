# Octadock Workflow Intelligence — Wargame Result

**Status:** Wargame output — planning input only, not implementation authorization
**Date:** 2026-07-10
**Input:** `Octadock-Workflow-Intelligence-Wargame-Brief.md` (founder's desktop, 2026-07-10)
**Method:** Evidence-based adversarial review. Every load-bearing claim in the brief was verified before scenario play: repo code audit (§19 foundations, storage, build/CI), product-strategy docs, official Claude Code documentation, official Codex documentation + issue tracker, Windows UIA/Chromium/OCR platform research, and live inspection of the founder's installed Codex/Claude state (`%USERPROFILE%\.codex`, `%USERPROFILE%\.claude\projects`).

---

## 1. Executive verdict

- **Overall verdict: PASS WITH CHANGES.** The narrow first bet (complete long-turn capture + Trusted Brief for Codex and Claude Code) is *more* feasible than the brief assumed. The broad ambition (universal Windows extraction via UIA/OCR tiers 4–7) is *less* feasible than the brief assumed and must be demoted to Labs-grade experiments.
- **Strongest product opportunity:** Both target providers now expose **documented** lifecycle hooks with near-identical payloads — Claude Code `Stop` → `last_assistant_message` + `transcript_path`; Codex `Stop` → `last_assistant_message` + `stop_hook_active` + `transcript_path`, plus a documented app-server JSON-RPC (`thread/list`, `thread/read` with full history, streamed `turn/*`/`item/*` events, explicit backward-compatibility commitment). The brief's priority-1 acquisition tier is not a hope; it is shipping, documented behavior on both providers.
- **Biggest unsupported assumption (now corrected):** that Codex acquisition means scraping undocumented local state. It does not — hooks + app-server are the sanctioned surfaces. Conversely, the brief's implicit assumption that "Claude interfaces" are one adapter is wrong: Claude Code (CLI/VS Code) is strong; claude.ai web requires a browser extension; the consumer Claude Desktop chat app has **no** local transcript or hook mechanism and lands in the weakest tier.
- **Biggest extraction risk:** DOM virtualization. Chromium/Electron accessibility trees only contain *realized* DOM nodes; messages recycled out of a virtualized chat list do not exist in UIA at all. No flag fixes this. UIA tier-4 extraction of chat UIs cannot deliver "complete document" and must never claim to.
- **Biggest summary risk:** unchanged from the brief — fluent omission of a critical constraint. The ledger/verifier architecture is the right countermeasure; it is unproven until Phase 3 runs on the corpus (which, verified: already exists on disk — 1,206 Claude transcripts / 333 MB and 457 Codex rollouts, including a 51 MB single session).
- **Biggest privacy/security risk:** raw-content collection landing on today's storage layer. Verified: **no encryption at rest anywhere**; SQLite is plaintext; the self-healing DB path quarantines corrupt files as `octadock.db.corrupt-<stamp>` copies and salvages tables — a future raw-content table would be duplicated into unmanaged plaintext quarantine files that retention never deletes. This must be redesigned before any full-consent trace is written.
- **Biggest native-UX risk:** "inline in the host layout" is unreachable for the desktop apps (both Electron, no extension surface; Codex desktop merged into the ChatGPT app on 2026-07-09 — install-path and UI churn is live right now). The realistic ceiling for desktop hosts is an anchored no-activate overlay. Additional trap found: attaching a UIA client can flip hosts (notably VS Code) into screen-reader-optimized mode, visibly changing the founder's own editor.
- **Recommended next experiment:** Phase 0 relabeled ("sample and label the existing on-disk corpus") + Phase 1 harness restricted to the two hook-based adapters. Skip all UIA/OCR work until those pass gates.

---

## 2. Evidence base (what was verified)

### 2.1 Claude Code (installed + documented)

- Hooks documented at code.claude.com/docs/en/hooks. `Stop` payload: `stop_reason`, `last_assistant_message`, `permission_mode` + common fields (`session_id`, `prompt_id`, `transcript_path`, `cwd`). `SubagentStop` carries the subagent's response. `PreCompact`/`PostCompact` exist.
- Live transcript inspection (`%USERPROFILE%\.claude\projects\...\<session>.jsonl`): every record carries `uuid`, `parentUuid`, `sessionId`, `timestamp`, `cwd`, `gitBranch`, `version`, `isSidechain`; assistant messages carry `stop_reason`/`stop_details` and typed content blocks (`text`/`thinking`/`tool_use`); tool results link back via `sourceToolAssistantUUID`. That is: stable anchors, complete content, turn chaining, subagent separation, and per-record project evidence.
- **However:** docs explicitly state the transcript entry format "is internal to Claude Code and changes between versions… scripts that parse these files directly can break on any release," recommending `/export`, `stream-json`, or the Agent SDK instead.
- Consumer Claude Desktop chat and claude.ai web: **no documented local transcript or hooks.** VS Code extension shares CLI conversation history (storage location undocumented).

### 2.2 Codex (installed + documented)

- Hooks documented (developers.openai.com/codex/hooks): `SessionStart`, `UserPromptSubmit`, `PreToolUse`/`PostToolUse` (tool I/O), `PreCompact`/`PostCompact`, `SubagentStop`, `Stop` (`stop_hook_active`, `last_assistant_message`) — all with `session_id`, `transcript_path`, `cwd`, `turn_id`. Hooks are hash-pinned and require explicit user trust — a consent property, not a bug.
- App-server JSON-RPC documented: `thread/list`, `thread/read` (`includeTurns: true`, reads full history without resuming), streamed `turn/started|completed`, `item/*` events; version-pinned schema generation; "designed to be backward compatible." Limitation: it cannot live-attach to someone else's interactive TUI turn — hooks fill that gap.
- Live rollout inspection (`%USERPROFILE%\.codex\sessions\...\rollout-*.jsonl`): `session_meta` (id, cwd, git, cli_version), `turn_context` (per-turn cwd/model/policies/`turn_id`), `event_msg/task_started` + `task_complete` (`turn_id`, timestamps, `last_agent_message`), `response_item/message` (role + content), `event_msg/user_message`. Complete turns, exact boundaries, per-turn project evidence.
- **However:** the rollout schema has **no stability contract** (openai/codex issue #20952 open, unanswered); the derived SQLite index is on its 5th schema (`state_5.sqlite`) with documented corruption/drift issues; rollouts can be `.zst`-compressed; the desktop app merged into the ChatGPT app 2026-07-09. Cloud tasks leave no local transcript.
- Machine-specific conflict: `config.toml` `notify` is a single-command slot and is **already occupied** by the computer-use runtime on this machine. `notify` cannot be Octadock's channel; `hooks.json` is the correct mechanism.
- Scale check: 457 rollout files on disk; largest recent single session 51 MB. Any per-event full-file re-parse design fails the latency gate immediately; adapters must tail incrementally by offset.

### 2.3 Windows platform

- Chromium ships a **native UIA provider on by default since Chrome 138**; accessibility trees build on demand when a UIA client connects (first-access latency; also flips hosts into AT mode — VS Code switches to screen-reader-optimized behavior with pagination capping accessible text to ~2,000 lines).
- CSS-scrolled-out content **is** in the Chromium tree (offscreen state modeled); **virtualized/recycled DOM rows are not** — the decisive limit for chat UIs.
- Windows Terminal and modern conhost implement UIA `TextPattern` over the buffer **including off-screen scrollback** (bounded by scrollback cap). WPF TextBox/RichTextBox, Word, Notepad (via `CUIAutomation8`) expose full `DocumentRange`.
- Hang protection is concrete: `IUIAutomation2` `ConnectionTimeout` (default 2 s) / `TransactionTimeout` (default 20 s), MTA-thread rules, `CacheRequest` batching; no hard-cancel exists, so a killable out-of-process extraction host is the safe architecture.
- Elevated targets require a signed, Program-Files-installed, UIAccess-manifested build — Octadock ships an unsigned zip, so elevated windows are unreachable (acceptable: they are auto-exclusion candidates anyway; must be honest in the capability contract).
- OCR: `Windows.Media.Ocr` provides **no real per-word confidence** (Octadock already hardcodes 1.0) and has a max image dimension (~4K screenshots need tiling/downscale). The brief's scroll-OCR pipeline steps that depend on "OCR confidence" are not implementable on the current engine; cross-frame agreement must substitute. Newer `TextRecognizer` (Windows App SDK) has real confidence but is Copilot+/NPU-gated.
- Overlay mechanics confirmed: `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`, `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` (chatty — debounce), per-monitor-DPI coordinate virtualization pitfalls (use PMv2 + `LogicalToPhysicalPointForPerMonitorDPI`).
- Prior art triangulates the design: PowerToys Text Extractor and Windows Recall both chose OCR-of-pixels for universality; Textify chose UIA for fidelity where hosts cooperate. Hybrid = sensible middle ground.

### 2.4 Octadock repo

- All nine §19 "reusable foundations" claims verified TRUE (CaptureSource process/window/HwndHash; `actions` table with 13 ActionTypes; clipboard foreground provenance honoring privacy formats; Windows.Media.Ocr + OCR history with 100k-char cap; ScrollingSession pixel band-matching with honest `Unmatched`-skip and `Truncated` flags, no auto-scroll; Skia visual diff with SHA-256 + heat map; AgentPacketBuilder with untrusted-source framing, deterministic secret redaction, hard size limits, relative-path-only assets).
- The removed AI-session discovery code (~40 files: process exit watchers, WMI creation events, Claude/Codex state collectors, change bus, SQLite store) is **recoverable from git history** (removed at commit `8b36629`). The archived spec records hard-won accuracy rules (12 h reopen-not-duplicate window, 12 s minimum process age, 3 s scan-spacing floor) and a real WAL data-loss incident (un-checkpointed store rolled back to empty on `taskkill /F`).
- All claimed gaps confirmed: no event spine, no UIA usage, no browser/editor adapters, no encryption at rest, no telemetry transport of any kind, **no build flavors/DefineConstants**, no code signing, zip-only distribution, no UI/integration test host (headless xUnit only + `--json` CLI + privacy-canary network test + CI copy-honesty gate).
- Product strategy (PRODUCT-STRATEGY-2026-07.md) explicitly rejects the ambient posture: "Passive AI/session discovery, background screen understanding… and any screen-watching product story" under *Remove or defer*; Experience principle #1: "It does not watch the screen in the background." The AI-sessions surface was implemented and removed the same day (2026-07-05), recorded in PROJECT-STATE.md as "not launch features or future commitments."

---

## 3. Scenario results

Severity: P0 = blocks the concept/phase; P1 = must fix before the affected capability ships; P2 = design change; P3 = note.

### Scenario 1 — 10,000-word Codex response → **Pass with changes** (Confidence: High)

- Expected experience holds: `task_started`/`task_complete` + `turn_id` give exact boundaries; `response_item/message` + app-server `thread/read` give complete content including intermediate messages; hooks give live completion.
- Plausible failure: `Stop`/`notify` carry only the **final** assistant message; intermediate turn content requires transcript tailing (uncontracted format) or app-server reads. A 51 MB session parsed at completion blows the 3 s gate.
- Required plan change: content acquisition = incremental rollout tailing by byte offset during the turn, reconciled against app-server `thread/read` at completion; hooks are the boundary clock, never the content store. Severity of unmitigated latency risk: P1.
- Experiment: Phase 1 harness measures end-of-turn-to-brief latency on the 51 MB session specifically.

### Scenario 2 — Claude edits while streaming; claims vs outcomes → **Pass with changes** (Confidence: Medium)

- `PostToolUse` provides `tool_name`/`tool_input`/`tool_output`; transcripts chain via `parentUuid` with `sourceToolAssistantUUID` on results; `isSidechain` separates subagents; `PreCompact` exists. The ledger can therefore mechanically cross-check "final answer says test passed" against actual tool output — this is a genuine advantage over any summarizer that only sees the final text.
- Plausible failures: session identity changes on resume (new sessionId/file — verified per-record `sessionId` makes stitching possible but the adapter must implement it); docs don't state whether Stop can fire multiple times per logical turn for Claude Code (Codex documents `stop_hook_active` for exactly this; Claude behavior must be measured, not assumed).
- Required plan change: a per-provider turn-state machine keyed on `turn_id`/`prompt_id` with idempotent completion handling; a corpus slice containing interrupted/resumed/compacted sessions (the on-disk corpus already contains these). Severity: P1.

### Scenario 3 — Scrollable virtualized chat → **Fail as scoped; pass after scope change** (Confidence: High)

- Verified: recycled DOM rows do not exist in the accessibility tree; UIA cannot reconstruct a virtualized conversation. Scroll-and-accumulate works only with user/consented scrolling and inherits every OCR/stitching failure mode.
- The brief's own fail condition ("calls a partial reconstruction complete") is avoidable via the capability contract — but the *intent* ("brief an entire conversation") is not deliverable on this tier.
- Required plan change (P0 for scope, not for the concept): complete-conversation briefs are **provider-adapter-only** in v1. For virtualized UIs without adapters, the product offers "visible section" briefs with explicit partial labeling, or nothing. This kills the implied promise, not the product.

### Scenario 4 — OCR-only unknown application → **Pass with changes, demoted to Labs** (Confidence: High)

- Verified blockers to the proposed pipeline: no OCR confidence signal on the shipping engine (steps 3, 7, 9 of §8.7 as written are unimplementable); 4K exceeds engine max dimension (tiling required); auto-scroll is explicitly rejected in the product and unsupported in code; existing stitcher is honest (skips unmatched) but pixel-based and truncates at bounds.
- Required plan change: replace engine-confidence with **cross-frame agreement** (same text recognized in ≥2 overlapping frames = confirmed; disagreement = flagged); reuse ScrollingSession's consensus band-matching for frame ordering; cap scope to "user scrolls, Octadock accumulates, result labeled partial-by-default." Severity: P1 within the Labs track.

### Scenario 5 — Summary omits a dangerous constraint → **Pass with changes** (Confidence: Medium)

- The separation of extraction/composition/verification is the correct architecture; nothing verified contradicts it. It is unproven until Phase 3.
- Verified acceleration: the ground-truth corpus (20–50 sessions) **already exists on disk** — Phase 0 becomes sampling + labeling, not collection. Include the brief's stress corpus items (negations, "expected to fail," corrections, numbers in code blocks) drawn from real sessions.
- Required plan change: none architectural. Add one rule: any evidence item whose `normalizedStatement` contains a negation or modality marker ("do not," "never," "expected to fail," "not verified") is auto-promoted to high importance pending model ranking — cheap deterministic backstop for the exact failure the scenario targets. Severity: P1.

### Scenario 6 — Summary invents certainty → **Pass with changes** (Confidence: Medium)

- Same architecture, same dependency on Phase 3 measurement. The verified availability of actual tool outcomes (S2) means "build output is missing / test was not run" is mechanically checkable for provider adapters rather than inferred — a real advantage.
- Required plan change: the verifier's contradiction/uncertainty checks must run against the ledger (typed evidence with modality), not against prose. Already implied; make it a gate: zero unqualified conclusions from `assumption`/`unresolved question` category items. Severity: P1.

### Scenario 7 — Wrong project correlation → **Pass with changes** (Confidence: High)

- Verified: for the primary use case, correlation is nearly solved at the source — Claude transcript records carry `cwd` + `gitBranch` per record; Codex `turn_context` carries per-turn `cwd` and `session_meta` carries git info. Two Codex tasks / similar repo names / mid-session cwd changes are all distinguishable from provider data.
- Residual risk is cross-application inference (screenshot → paste → build ancestry), which remains heuristic exactly as the brief says.
- Required plan change: tiered correlation policy — **automation and content attachment require provider-anchored project evidence (cwd/git/session id); time-proximity-only correlation may annotate, never attach.** Severity: P1.

### Scenario 8 — Full developer trace captures a secret → **Pass with changes; P0 controls before first trace** (Confidence: High)

- Verified assets: deterministic `TextSecretDetector` + redaction with per-kind counts; untrusted-source framing; privacy-canary network test pattern; no telemetry transport exists (nothing can phone home today).
- Verified gaps (each P0 before full-trace collection is enabled even internally):
  1. **No encryption at rest.** The raw store must be a separate, encrypted (DPAPI user-bound at minimum), separately-keyed store — not a table in `octadock.db`.
  2. **Self-healing salvage/quarantine leaks.** Corruption handling copies DB files to `*.corrupt-<stamp>` and salvages listed tables. Raw-content stores must be excluded from salvage, and retention/delete-all must sweep quarantine copies, WAL/SHM sidecars, and temp exports.
  3. **Hook host logging.** Hook payloads (containing `last_assistant_message`) flow through Octadock's hook receiver; Serilog paths must classify them as raw content — never into normal logs or crash reports (crash reporter is currently redacted-local-only; keep it that way).
- Fail-condition assessment: with the three controls above plus the existing canary pattern extended to the trace store ("plant sentinel → export/logs/crash-report must not contain it"), the zero-tolerance gates are testable, not aspirational.

### Scenario 9 — Public-build contamination → **Pass with changes** (Confidence: Medium)

- Verified reality: there is **no build-flavor mechanism at all** (no DefineConstants, no configurations beyond Debug/Release), no signing, no update channels, zip-only distribution. The brief's control "separate signing/update channels" has a missing dependency: signing infrastructure does not exist yet.
- Verified precedent: the CI copy-honesty gate proves this repo already does artifact-inspection gates; extending CI to fail on internal-collector types/strings/migrations in public artifacts is an established pattern here, not novel process.
- Required plan changes: internal collector = separate assembly + separate csproj excluded from the public solution build (not an `#if`); public CI job inspects published artifacts for the assembly name, raw-content schema strings, and developer export verbs; migrations for raw stores live only in the internal assembly; settings import must reject unknown consent levels (fail-closed enum parsing). Severity: P0 for the mechanism existing before any collector code lands on main; P1 for signing/channel separation (blocked on distribution work that is already on the product roadmap).

### Scenario 10 — Application update breaks integration → **Pass with changes** (Confidence: High)

- This scenario fired *during the wargame*: Codex desktop merged into the ChatGPT app on 2026-07-09; the Codex SQLite index is on schema revision 5; Claude transcript format is explicitly uncontracted; `developers.openai.com/codex/*` URLs began redirecting after the rebrand.
- Required plan changes: (1) build on the documented surfaces (hooks payloads, app-server with version-pinned schema generation, `stream-json`) and treat file parsing as fallback behind a version-tolerant reader; (2) every adapter runs a **startup self-test** against known-good fixtures plus a live smoke check (parse the newest session; if unrecognized record kinds exceed a threshold, degrade to metadata-only and surface the degradation); (3) capability contract downgrades must be user-visible ("Codex adapter running in reduced mode since update X"). The brief's fail condition (silently complete-looking briefs from degraded input) is prevented by the contract only if downgrade paths are tested — add induced-schema-change fixtures to CI. Severity: P1.

### Scenario 11 — Augmentation feels like an Octadock popup → **Pass with changes; founder gate** (Confidence: Medium)

- Verified: inline insertion into Codex/Claude desktop apps is not possible (Electron, no extension surface, active UI churn). The ceiling is an anchored `WS_EX_NOACTIVATE`+`TOOLWINDOW` overlay tracked via `SetWinEventHook`, PMv2-DPI-correct — all confirmed mechanics with known pitfalls (chatty location events need debouncing; cross-DPI coordinate translation).
- New trap: connecting a UIA client to hosts to find anchor rectangles can flip them into AT mode (VS Code visibly changes). Anchoring for hook-based adapters should use window geometry only (Win32), not UIA content queries.
- Browser extension remains the only path to true inline presentation (claude.ai, chatgpt.com) and should be where "native" is proven.
- Required plan change: Phase 4 tests the anchored overlay against the founder's fail condition *as the primary hypothesis*, with the browser extension as the inline comparison arm. If the overlay fails the founder test on desktop hosts, the presentation for CLI/desktop degrades to the existing Shelf/HUD surfaces (capsule result) rather than a new window. Severity: P0 as a decision gate (this is the brief's §13.4 "hard truth" — it stays open until Phase 4).

### Scenario 12 — Too much automation too early → **Pass** (Confidence: High)

- The brief's own controls (frequency ≠ permission; augmentation policy separate from learned confidence; observe → enrich progression) are consistent with verified Agent Workspace DNA (no provider fallback, destination-named confirmation, explicit boundaries). Keep learning at observe/enrich for the entire internal phase; predict/automate stay design-only.
- One addition: augmentation policies must fail closed when the provider/model named in the policy is unavailable — no silent substitution (mirrors the shipped "no provider fallback" rule). Severity: P2.

### Scenario 13 — Very short and low-value content → **Pass with changes** (Confidence: High)

- Trivially satisfiable for provider adapters: token usage and content length are in the transcripts/rollouts, so the reading-burden threshold is computable from ground truth, not estimated. Suppress briefs for code-verbatim turns (detectable: majority of content in code blocks) and turns with an existing summary heading.
- Required plan change: make "did nothing" a measured outcome (log suppression decisions under metadata consent) so the dismiss/ignore metric in §18.3 has a denominator. Severity: P2.

### Scenario 14 — Performance collapse → **Pass with changes** (Confidence: High)

- Verified stressors: 51 MB single rollout on this machine; 457 sessions; 333 MB of Claude transcripts; WAL checkpoint data-loss history; UIA default timeouts (2 s / 20 s) that *block* for their duration; Chromium first-access tree-build cost; OCR tiling on 4K; `EVENT_OBJECT_LOCATIONCHANGE` flood.
- Required plan changes: (1) workflow/trace data in a **separate SQLite database file** from `octadock.db` so the event spine can never corrupt captures/clipboard/settings (directly addresses the §17 recovery gate); (2) incremental tailing with persisted offsets, bounded queues, backpressure = drop-to-metadata (never block the host); (3) UIA and OCR work in a killable child process; (4) focus coalescing + debounced window tracking as spec'd in §15.3; (5) storage estimate is now computable from real data — do it in Phase 0, not later. Severity: P1.

---

## 4. P0 blockers

1. **Product-strategy contradiction requires an explicit founder decision, in writing, before any code.** The shipped strategy's first experience principle is "does not watch the screen in the background," and the AI-session surface was deliberately removed (2026-07-05) as "not a future commitment." This concept — even hook-based, even internal — reverses that posture for the founder's build. Required artifact: a strategy addendum distinguishing (a) internal consented research instrumentation (allowed, internal build only), (b) public metadata-only workflow learning (not approved; separate future decision), (c) undisclosed screen watching (prohibited, unchanged). The wargame does not decide this; it can only insist the decision be explicit.
2. **Raw-content data safety before first trace:** encrypted separate store; exclusion from DB self-heal salvage; retention that sweeps quarantine/WAL/temp files; hook payloads classified as raw content in logging. (Scenario 8.)
3. **Build-flavor separation mechanism before internal collector code exists on main:** separate internal assembly + public CI artifact-inspection gate (copy-honesty-gate pattern). Signing/update-channel separation is acknowledged as blocked on distribution work and tracked as P1. (Scenario 9.)
4. **Presentation decision gate:** anchored overlay is the desktop ceiling; the founder must accept it (Phase 4 test) or the desktop presentation degrades to existing Octadock surfaces. Inline presentation is proven on the browser extension arm only. (Scenario 11.)

---

## 5. Risk register

| Risk | Area | Severity | Likelihood | Evidence | Mitigation | Verification | Owner |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Transcript/rollout formats change and silently break parsing | Extraction | P1 | High | Claude docs: format "internal… can break on any release"; Codex issue #20952 unanswered; `state_5` = 5th schema; desktop app merged 2026-07-09 | Documented surfaces first (hooks, app-server, stream-json); version-tolerant fallback parser; startup self-test; degrade to metadata-only, visibly | Induced-schema-change fixtures in CI; adapter reports capability downgrade | Extraction |
| Virtualized chat UIs unreconstructable via UIA | Extraction | P0 (scope) | Certain | Chromium a11y docs: recycled nodes absent from tree | Complete briefs = provider adapters only; visible-section briefs elsewhere, labeled | Scenario-3 test in Phase 1 capability matrix | Extraction |
| OCR pipeline assumes confidence signal that doesn't exist | Extraction | P1 | Certain | `Windows.Media.Ocr` no per-word confidence; Octadock hardcodes 1.0 | Cross-frame agreement replaces engine confidence; App SDK TextRecognizer where NPU exists | Phase 2 scoring vs ground truth | Extraction |
| Brief omits critical constraint fluently | AI | P0 (gate) | Medium | Known LLM failure mode; unmeasured until Phase 3 | Typed ledger + independent verifier + negation/modality auto-promotion + coverage gate | 100% labeled-P0 recall on held-out corpus | AI eval |
| Verifier self-correlation (same model composes and checks) | AI | P1 | Medium | §12.2 risk noted in brief | Different model/profile for verification; deterministic checks (numbers, filenames, negations) don't use a model at all | Ablation in Phase 3: verifier catches seeded errors | AI eval |
| Raw traces leak secrets via logs/quarantine/exports | Privacy | P0 | Medium | No encryption at rest; salvage copies files; hook payloads carry response text | Encrypted separate store; salvage exclusion; retention sweeps; log classification; canary tests | Extended privacy-canary suite incl. crash + corrupt-DB paths | Security |
| Public build ships internal collector | Privacy | P0 | Low | No build flavors exist today | Separate assembly excluded from public solution; CI artifact inspection; fail-closed settings import | Public CI gate red-teams a deliberately contaminated build | Security |
| Prompt injection: captured content steers the brief or borrows Octadock's authority | Security | P1 | Medium | Brief §16.1; AgentPacket untrusted framing exists for outbound, not inbound briefs | Ledger treats imperatives in source as data; brief UI visually distinguishes quoted source text from Octadock text; red-team corpus | Injection corpus items rendered; zero instruction-following | Security |
| Hook receiver executed inside agent context is tampered with (user-writable hooks.json) | Security | P2 | Low | Codex hash-pins hooks (helps); same-user malware out of scope | Document threat model; verify hook binary path; treat hook stdin as untrusted input | Threat-model doc + input fuzzing | Security |
| UIA client flips hosts into AT mode / hung provider blocks | Platform | P1 | High | Chromium on-demand a11y; VS Code screen-reader mode; 2 s/20 s blocking timeouts | Win32-only geometry for anchoring; UIA only where it's the content path; killable child process | Phase 1 harness on each target app | Platform |
| Overlay feels like a popup; founder rejects | UX | P0 (gate) | Medium | No extension surface in Electron hosts; §13.4 | Anchored no-activate overlay done properly (PMv2 DPI, debounced tracking, theme-matched); browser-extension inline arm as comparison | Phase 4 founder test, Scenario-11 stress list | UX |
| Event spine corrupts or bloats core DB | Performance | P1 | Medium | WAL data-loss incident on record; 51 MB sessions; focus-event volume | Separate DB file; coalescing; bounded queues; offsets; storage estimate from real data | Crash/kill tests; storage projection in Phase 0 | Platform |
| Latency gate missed on long sessions | Performance | P1 | Medium | 51 MB rollout on disk | Incremental streaming extraction; work completes during the turn | Measure on the actual 51 MB session | Extraction |
| Cross-app correlation attaches wrong project | Product | P1 | Medium | Heuristic-only for non-provider events | Provider-anchored evidence required for attach/automation; annotate-only otherwise | Precision measured vs hand-labeled traces (Phase 5) | Product |
| Codex cloud tasks invisible locally | Product | P3 | Certain | Cloud docs; results server-side | Scope note; observe apply-diff moments only | n/a | Product |

---

## 6. Decision log

| Decision | Options considered | Choice | Evidence | Revisit trigger |
| --- | --- | --- | --- | --- |
| Codex acquisition path | scrape rollouts / hooks / app-server / exec --json | **Hooks (boundaries + last message) + app-server thread/read (content) + rollout tailing (streaming fallback)** | All three documented; rollout format uncontracted | App-server drops thread/read or hooks change |
| Claude acquisition path | transcript parsing / hooks / Agent SDK / stream-json | **Hooks (boundaries + last message) + transcript tailing (content, version-tolerant) with self-test** | Stop carries last_assistant_message; transcript verified complete but uncontracted | Transcript format break detected by self-test |
| Claude surfaces split | one "Claude adapter" | **Claude Code (CLI/VS Code) = tier 1; claude.ai web = browser extension; Claude Desktop chat = out of scope v1** | Consumer app has no local data or hooks | Anthropic ships desktop hooks/transcripts |
| UIA scope | universal tier-4 extraction | **Narrow: Windows Terminal scrollback, WPF/Word/Notepad documents. No UIA on chat UIs or VS Code editor** | Virtualization; pagination; AT-mode side effects | Chromium exposes virtualized content (unlikely) |
| Scroll-OCR | pursue as universal fallback | **Labs experiment; partial-by-default labeling; cross-frame agreement instead of engine confidence** | No confidence signal; auto-scroll rejected in product | App SDK TextRecognizer becomes broadly available |
| Presentation for desktop hosts | inline / overlay / capsule | **Anchored no-activate overlay, founder-gated; browser extension proves inline; Shelf/HUD as fallback** | No extension surface in Electron hosts | Provider ships an extension/plugin API |
| Raw store | table in octadock.db / separate encrypted store | **Separate encrypted store, salvage-excluded, quarantine-aware retention** | Plaintext SQLite + self-heal copy behavior | Security review of key management |
| Internal/public separation | #if flags / separate assembly + CI gate | **Separate assembly excluded from public build + CI artifact inspection (copy-honesty-gate pattern)** | No flavor mechanism exists; gate pattern proven in repo | Signing/channels exist → add channel separation |
| Event spine storage | shared octadock.db / separate DB | **Separate SQLite file with own WAL/checkpoint policy** | WAL data-loss history; §17 recovery gate | n/a |
| Phase 0 corpus | collect 20–50 sessions | **Sample + label the existing 1,206 Claude / 457 Codex sessions on disk** | Verified present, includes long/interrupted/resumed/tool-heavy sessions | n/a |
| Phase 5 watchers | build new | **Resurrect from git history (commit `8b36629`) + apply archived accuracy rules** | ~40 files recoverable; field-tested heuristics documented | Code rot vs current architecture |

---

## 7. Revised scope

### Internal build (full developer consent) — in scope
- Claude Code adapter (hooks + transcript tailing + self-test).
- Codex adapter (hooks + app-server + rollout tailing + self-test).
- Boundary engine, segmenter, evidence ledger, brief composer, verifier — evaluated offline against the labeled corpus before any live surface.
- Encrypted raw store + retention + delete/export with manifest.
- Event spine (separate DB) + trace correlator, observe-only.
- Anchored overlay prototype + browser-extension inline prototype (Phase 4).

### Public build — unchanged from today
- Nothing from this concept ships publicly. No consent level above "Octadock activity only" is exposed. The "Workflow metadata" tier remains a *candidate* pending internal evidence and a separate product/legal review, exactly as the brief states.

### Killed / demoted
- Universal UIA extraction of arbitrary apps: killed as a v1 goal; narrowed to Terminal/WPF/Word/Notepad Labs matrix.
- Complete reconstruction of virtualized chat via scrolling: demoted to Labs, partial-by-default.
- Consumer Claude Desktop chat adapter: out of scope until a data surface exists.
- Any automation beyond enrich: stays out of scope for the entire internal phase (unchanged from brief, now firm).

### Adapter priority
1. Claude Code (founder's primary tool; strongest verified data)
2. Codex CLI/Desktop (documented surfaces; churn risk managed by self-tests)
3. Browser extension (claude.ai / chatgpt.com; only true inline presentation)
4. Windows Terminal via UIA TextPattern (cheap, structured, real scrollback)
5. WPF/Word/Notepad UIA documents (Labs)
6. Scroll-OCR accumulation (Labs, last)

---

## 8. Consent and retention policy (recommended)

- Keep the brief's four consent levels; add: adapter installation is itself a consent act — installing the Claude/Codex hook writes user-visible, hash-pinned config the user can audit (`hooks.json`), and Octadock's settings must show exactly which hooks it installed and offer one-click removal.
- Full-developer-trace defaults: raw retention **7 days** rolling (founder can pin/export specific traces to keep); metadata retention 90 days; pause switch suspends hook ingestion *and* file tailing immediately; delete-all covers raw store, quarantine copies, WAL/SHM sidecars, temp exports, and installed-hook telemetry.
- Exclusions: per-project and per-app exclusion lists enforced at the event spine (drop before persistence, not after); the auto-exclusion list from §6.3 adopted as-is; elevated windows are excluded by capability (unreachable without UIAccess) and must be reported as such, not silently.
- External processing: off by default even at full consent; per-destination allowlist reusing Agent Workspace's no-fallback + destination-named confirmation pattern.

---

## 9. Metrics and kill criteria — revisions

Keep §18 and §24 with these changes:

- **Add gate:** adapter self-test detects an induced format change and downgrades visibly (Scenario 10). Kill signal: any silently-complete brief from degraded input in testing.
- **Amend:** "at least 99% normalized text recovery for structured transcript adapters" — apply to hook/app-server adapters; measured against `/export` or app-server output as ground truth (not against the same file being parsed).
- **Amend:** brief latency gate is measured on the worst real session in the corpus (51 MB), not an average.
- **Add metric:** suppression correctness (Scenario 13): % of below-threshold turns where the system correctly did nothing.
- **Add kill criterion:** if the founder-decision P0 (strategy addendum) is not resolved, the project stops before Phase 1 — evidence quality is irrelevant if the product refuses the posture.
- Numeric gates otherwise stand; revisit after first corpus scoring as the brief already requires.

---

## 10. Experiment backlog

| Priority | Hypothesis | Cheapest valid experiment | Success metric | Kill condition |
| --- | --- | --- | --- | --- |
| P0 | Existing on-disk sessions suffice as ground-truth corpus | Sample 30 Claude + 20 Codex sessions incl. interrupted/resumed/tool-heavy; label boundaries + critical facts | Reviewed corpus + scoring script | Corpus lacks diversity → collect targeted sessions |
| P0 | Hook+tail reconstructs complete turns with exact boundaries | Read-only harness: install Stop/PostToolUse hooks + offset-tailing; replay against corpus | ≥99% normalized text recovery; 100% boundary accuracy on labeled turns | Either provider's hooks lack turn coverage |
| P0 | Brief latency <3 s on worst-case session | Incremental extraction during streaming; measure on 51 MB rollout | <3 s post-completion | >10 s → redesign or narrow |
| P1 | Ledger+verifier catches seeded critical omissions | Offline runner; seed 20 corpus items with known-critical constraints; compare model profiles | 100% labeled-P0 recall; 0 unsupported high-impact claims | Recall <100% and failures undetected |
| P1 | Anchored overlay passes founder's native test | Overlay prototype on Windows Terminal + Codex desktop; Scenario-11 stress list (DPI, theme, focus, typing) | Founder accepts without managing a new surface | Founder rejects → Shelf/HUD fallback or browser-first pivot |
| P1 | Adapter self-test detects format drift | Mutate fixture schemas; verify degrade-to-metadata + visible downgrade | 100% of induced changes detected | Silent complete-looking output |
| P1 | Raw-store safety controls hold | Canary suite: sentinel secret → logs, crash report, corrupt-DB salvage, export, retention sweep | Zero sentinel escapes | Any escape |
| P2 | Terminal TextPattern yields complete scrollback | Harness against Windows Terminal + conhost with known output | Text recovery ≥99% within scrollback cap | Boundary bugs make ranges unreliable |
| P2 | Cross-frame agreement substitutes for OCR confidence | Two-frame overlap corpus; measure dedup/order accuracy | Ordering/duplication errors always surfaced | Hidden errors persist |
| P3 | Cross-app correlation precision without content | Replay founder's real week of metadata events vs hand-labeled traces | Precision at agreed threshold (set after labeling) | Precision unacceptable → metadata-only public mode dies |

---

## 11. Final decision

**Proceed to the isolated harness (Phase 0 + Phase 1), restricted to the Claude Code and Codex hook-based adapters, after the founder resolves P0-1 (strategy addendum) — and with P0-2/P0-3 (raw-store safety, build-flavor gate) designed before any trace is written.**

The wargame's core finding is asymmetric: the brief was *pessimistic about the wrong thing*. Structured acquisition from the two providers that matter is documented, verified on this machine, and richer than assumed (tool outcomes, per-turn project identity, stable anchors). The genuinely hard, low-yield territory is exactly the universal-extraction ambition (virtualized UIs, OCR reconstruction, elevated windows) — and the plan survives by shrinking that ambition to Labs experiments rather than letting it block the valuable core.

What kills this project if ignored, in order: shipping a posture the product strategy explicitly rejected without an explicit reversal; writing raw AI conversations into a plaintext, self-healing, quarantine-copying storage layer; letting the internal collector exist in a repo with no build-flavor boundary; and betting the UX on inline presentation that the desktop hosts cannot offer.

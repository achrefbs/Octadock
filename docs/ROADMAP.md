# Octadock Roadmap And Recovery Execution Plan

Last updated: 2026-07-19 (Phase 1 implementation/evidence refresh)
Status: **active execution authority**
Code baseline: `219b487` (`main`) plus the fingerprinted dirty Phase 1 candidate
Execution branch: `main`

Use `docs/PROJECT-STATE.md` and `docs/CAPABILITIES.md` for implementation
truth, and `docs/PRODUCT-STRATEGY-2026-07.md` for durable positioning, pricing,
and product decisions. Older dated plans, specs, proposals, and wargames are
history unless this roadmap explicitly imports an open task from them.

## Outcome

Ship one trustworthy paid-beta product built around:

`capture or dictate -> Shelf/Context -> review -> export or explicit AI handoff`

Recovery is complete only when the repository has one canonical execution
path, CI is a trustworthy signal, known security findings are closed or
explicitly accepted, the four primary workflows pass real Windows acceptance,
and the signed download/purchase/activation path is rehearsed end to end.

## What Is Stopping Release

1. **External control-plane and cleanup authority remain blocked.** The local
   implementation path is now `main`, but GitHub billing/plan controls and
   destructive retirement of historical worktrees require founder access or
   approval.
2. **Remote CI is not a signal.** Local Release validation passes, but all 18
   recent GitHub Actions runs end instantly as synthetic `BuildFailed` startup
   failures with zero jobs or logs. The workflow parses and passes `actionlint`;
   the remaining evidence points to account billing/quota/budget state, not a
   demonstrated YAML defect.
3. **One security-policy blocker remains.** The closure batch fixes admin
   auth/cache isolation, webhook disclosure, activation replacement, shell-active
   file handling, untrusted framing, namespaced/quoted/suffix-form secret detection, and
   explicit first-run clipboard consent. Offline-trial deletion/replay policy
   still needs a founder decision before the final rescan.
4. **Real-Windows acceptance is incomplete.** The contextual review,
   capture/history, Context, accessibility, dictation, performance, and soak
   code paths now have deterministic gates, but rendered focus/owner/DPI,
   assistive-technology, microphone, and long-run hardware evidence remains.
5. **Automated success does not cover release risk.** Mixed DPI, multiple
   monitors, microphones, scrolling capture, recording finalization,
   accessibility, and clean-machine installation remain manual gates.
6. **The commercial path is placeholder-backed.** There is no signed installer
   URL, live checkout, waitlist endpoint, published checksum, or rehearsed
   money-to-entitlement lifecycle.
7. **Repository hygiene still needs approval.** Live authority is consolidated,
   but large historical archives and dirty experimental website files remain and
   must not be deleted until their inventory is approved.

## Verified Baseline

Observed on 2026-07-17 from clean `main` at `a60c779`:

| Gate | Result |
| --- | --- |
| `build/build.ps1 -Configuration Release` | Passed |
| Desktop tests | 1,010 passed; 0 failed; 0 skipped |
| License-service tests | 65 passed |
| Internal Workflow Intelligence tests | 27 passed; excluded from public artifacts |
| Version, public-boundary, copy-honesty gates | Passed at `0.2.0-alpha.0` |
| Website JavaScript syntax | 13 production modules passed |
| Analyzer warnings | 613; tracked debt, not a release stop by itself |
| GitHub Actions | Blocked before jobs begin |
| Website npm/56-test claim | Not reproducible: no committed package manifest, lockfile, or tests |

Recovery-candidate evidence observed on 2026-07-17 from
`codex/recovery-2026-07-17`:

| Gate | Result |
| --- | --- |
| Release build and self-contained single-file App/CLI publish | Passed |
| Desktop tests | 1,111 passed: Core 685, App 276, Data 82, Platform.Windows 47, CLI 21 |
| License-service tests | 75 passed |
| Internal Workflow Intelligence tests | 27 passed; excluded from public artifacts |
| Version, public-boundary, copy-honesty gates | Passed at `0.2.0-alpha.0` |
| Website JavaScript syntax | 13 production modules passed; 15 including generated model data |
| Analyzer warnings | 615; existing tracked debt, not a release stop by itself |

Phase 1 candidate evidence observed on 2026-07-19 from `main` at `219b487`
plus the fingerprinted dirty candidate:

| Gate | Result |
| --- | --- |
| Canonical `build/build.ps1 -Configuration Release` | Passed build, all tests, web validation, publish, and boundary gates |
| Desktop tests | 1,219 passed: Core 729, App 329, Data 88, Platform.Windows 50, CLI 23 |
| License / internal tests | 75 license-service and 27 internal passed |
| Website | 15 syntax, 6 static-contract, and 21 Chromium tests passed |
| C-04 deterministic matrix | 59/59 passed; microphone/hardware rows pending |
| C-06 static source gate | Clean: 0 new, 0 baselined findings |
| Acceptance tooling self-test | 30 assertions, including exact ID/SQLite/file-hash correlation and quoted untracked paths |
| Publish boundaries | Self-contained App/CLI, version, copy honesty, and public boundary passed |

## Scope And Operating Rules

1. `main` in `Desktop/Octadock` is the canonical local implementation path for
   this pass. Do not create a competing product branch/worktree.
2. Do not reset, move, delete, or merge dirty worktrees until their unique diffs
   are inventoried and classified as keep, archive, or discard.
3. Freeze major feature work, new AI surfaces, new landing concepts, hosted
   services, recorder expansion, MCP, and passive monitoring until Gates A–C
   exit. The first post-recovery feature slice is Gate E (memory spine) per
   `docs/strategy/SCOPE-RESET-2026-07-19.md`.
4. Every task requires evidence, an acceptance gate, and a source-of-truth
   update. Code and executed evidence outrank prose.
5. Keep one integration PR. Preserve experimental website states as tags or
   archives, not competing product branches.
6. “Done” means acceptance evidence exists; merged code alone is not done.

## Product Decisions For This Pass

These are the recovery defaults unless the founder explicitly overrides them.

| Area | Direction |
| --- | --- |
| Thesis | Local-first Windows capture-to-context workspace |
| Primary pillars | Capture/Shelf, cursor dictation, Context, explicit reviewed handoff |
| AI experience | Contextual “Prepare handoff / Use with AI” review; no ambient monitoring or permanent mission-control dashboard |
| AI implementation | Keep deterministic packet, redaction, verification, and read-only CLI engine; remove standalone positioning and contradictory entry points |
| Website | Aquarium on `main` is the selected beta direction; harden it instead of redesigning again |
| Recording | Beta, video only |
| Scrolling capture | Beta, manual vertical only |
| Analytics | No outbound product analytics unless separately specified and consented |
| Deferred | MCP, hosted sharing, teams, sync, audio recording, universal document editing, command-dashboard expansion |
| Memory spine (approved 2026-07-19) | Post-recovery Gate E: Thoughts + explicit project registry + pull-based Project Lens + read-only Tidy; see `docs/strategy/SCOPE-RESET-2026-07-19.md` |
| File preview | Frozen at glance scope: current formats only, land in-flight hardening, no expansion ever roadmapped |

## Now — Regain Control And Trust

### Gate A — Canonical repository and CI

| ID | Work | Acceptance evidence | Status |
| --- | --- | --- | --- |
| A-01 | Create clean recovery worktree from latest `main` | Recovery branch at `a60c779`; existing dirty roots untouched | **Done** |
| A-02 | Record current validation baseline | Clean-main baseline and recovery candidate recorded; candidate passes Release build, publish, 1,111 desktop, 75 service, and 27 internal tests | **Done** |
| A-03 | Resolve Actions account startup block | Billing/quota/budget/payment checked; dispatched run is named `CI` and creates jobs rather than `BuildFailed` | **Blocked — founder GitHub billing access** |
| A-04 | Fix real job-level failures, if any | Windows, Linux, license-service, and lint lanes execute; blocking lanes green | **Blocked on A-03** |
| A-05 | Make website validation reproducible | Committed syntax, link/asset, fallback, accessibility, and browser-smoke checks; unsupported npm claims removed or backed by committed tooling | **Done — locked npm/Playwright gate** |
| A-06 | Add release packaging workflow | Strict tag workflow creates the versioned ZIP, manifest, verified checksums, and executed public-boundary evidence; exact-tag path and local preflight are documented; workflow passes `actionlint` and local packaging | **Done** |
| A-07 | Normalize GitHub control plane | `main` default; stale description fixed; redundant PR #3 closed; recovery PR targets `main`; protection enabled after plan/public-repo decision | **Blocked — owner settings and GitHub plan** |
| A-08 | Inventory every worktree and unique branch diff | Evidence table records keep/archive/discard recommendation; no destructive cleanup | **Done** |
| A-09 | Retire approved obsolete work | One clean primary checkout; unrelated content moved out; temporary worktrees follow one convention | **Blocked — founder cleanup approval** |
| A-10 | Refresh source-of-truth documents | README, project state, capabilities, strategy, testing, specs/wargames indexes, service ops docs, and roadmap agree on authority and current behavior | **Done** |

#### Worktree inventory snapshot (historical 2026-07-17 evidence)

All seven worktree HEADs have zero commits not already reachable from
`origin/main`. The preservation risk is dirty/untracked files, not missing commits.

| Location | State vs `origin/main` | Dirty material | Recommendation |
| --- | --- | --- | --- |
| Primary workspace `Workspace/Octadock` | Detached `a708392`, 5 behind | 8 tracked website/legal edits; about 1,350 untracked files (mostly `outputs/` and `humanity-25/`) | Quarantine; review five unique docs/web files; move unrelated outputs outside the repo; do not delete yet |
| `Desktop/octadock-web-redesign` | `claude/web-context-core` at `a708392`, 5 behind | 3 tracked and 3 untracked website files | Preserve one visual snapshot/diff, then archive as a superseded website direction after approval |
| `.claude/worktrees/frosty-hopper-72892f` | Clean detached `7c86aa5`, 10 behind | None | Remove after approval |
| `.claude/worktrees/octadock-landing-page-64b4fd` | Clean detached `da01b6c`, 15 behind | None | Remove after approval |
| `.claude/worktrees/recursing-euler-412c29` | Detached current `a60c779` | 2 untracked `.vercel` files | Inspect hosting identifiers, then archive/remove after approval |
| `.codex/worktrees/recovery-2026-07-17` | Current `a60c779` recovery branch | Intentional recovery diff | Preserve until founder-approved cleanup; it is no longer the canonical implementation path |
| `Workspace/Octadock-octopus-production` | Clean `main` at `a60c779` | None | Keep as canonical main until recovery merges; normalize the folder name later |

GitHub currently returns `403` for branch-protection configuration on this
private repository; protection requires GitHub Pro or making the repository public.

Gate A exits when there is one unambiguous code path, one roadmap, one website,
one integration PR, and reproducible local/remote validation.

### Gate B — Security and consent closure

| ID | Work | Acceptance evidence | Status |
| --- | --- | --- | --- |
| B-01 | Require admin authentication; keep public health minimal | Public health has only status; absent config returns 503; missing/wrong/query-only auth returns 403 with no metrics; valid header returns 200 with no-store caching | **Done** |
| B-02 | Remove license material from webhook responses/logs | Actual HTTP webhook response contains only outcome/message; no key or `OCTA-`; issuance and replay idempotency persist | **Done** |
| B-03 | Stage protocol activation and compare entitlement identity | Replacement of a distinct existing entitlement makes no activation/store call before visible default-No confirmation; cancel preserves it; first/same-key activation stays usable | **Done** |
| B-04 | Define and harden offline trial policy | Delete, mutate, truncate, replay, and rollback cannot silently grant a fresh trial under the approved policy | **Blocked — policy decision** |
| B-05 | Classify `.url` and equivalent shell-active content as risky | Case/trailing-dot-space normalization, App Installer/theme packages, conservative shell-active/Windows package formats, benign double extensions, and the App warning seam have regressions | **Done** |
| B-06 | Unify collision-safe untrusted-source framing | Exact delimiters across LF/CRLF/CR/VT/FF/NEL/LS/PS remain indented in both text-action and packet paths; labels cannot inject header lines | **Done** |
| B-07 | Expand secret-detector bypass corpus | Namespaced/bracketed/suffix-form env/config keys and complete quoted/multiline secrets are redacted downstream; max-size non-match is non-backtracking | **Done** |
| B-08 | Make clipboard collection first-run opt-in | Fresh settings default off; first run persists explicit enable/decline; the listener starts only when enabled; Settings pause, local-retention copy, and Clear are covered | **Done** |
| B-09 | Re-run standard security scan | No high finding; every medium is fixed or explicitly accepted with rationale | **Blocked on B-04** |

Remaining order: decide/implement B-04, then run B-09.

Gate B exits when known high findings are closed, medium findings are fixed or
accepted, and no data/process boundary is silent.

#### B-04 decision package

The current alpha stores `trial.json` and `trial-clock.json` as unsigned local
state. Missing, malformed, truncated, unreadable, deleted, or replayed state can
silently produce a fresh trial. The monotonic high-water check protects only
intact local state.

Recommended paid-beta policy: an anonymous, server-authoritative 14-day device
trial.

1. A cold start creates a nonce and requests `POST /trial/session`; no account,
   card, or email is required.
2. The service derives a subject with `HMAC(serverPepper, canonicalMachineHash)`
   and stores only that pseudonymous subject plus immutable start/expiry/status
   and audit timestamps.
3. The first expiry never moves. The response is Ed25519-signed and bound to the
   subject, nonce, and times; the running app gets at most a 24-hour monotonic
   allowance before it must refresh.
4. An offline cold start shows an explicit “trial needs a connection” state and
   never creates a replacement trial. Paid signed entitlements continue offline.
5. Privacy copy discloses the stable pseudonym and ordinary service/IP logs.

Strict replay prevention and zero-network offline restarts cannot both be
guaranteed. If the founder instead chooses a fully offline trial, the product
must explicitly accept a casual-tamper-only honor system and document that a
state wipe/reinstall can reset it.

Acceptance requires: delete/reinstall returns the original expiry; malformed,
truncated, mutated, or replayed local state fails closed; stale response nonces
are rejected; HTTP retries and concurrent first requests preserve one expiry;
clock changes and VM snapshots never extend it; expiry cannot renew; the service
stores no raw machine hash or nonce; and paid entitlements still work offline.

### Gate C — Focused product reliability

| ID | Work | Acceptance evidence | Status |
| --- | --- | --- | --- |
| C-01 | Replace standalone Agent Workspace positioning with contextual review | Shelf, Pin, Context, History, Clipboard, Dock, tray, and aliases enter one source-bound fail-closed review; exact-source, reset, and temp-lease regressions pass; owner/topmost logic is implemented | **Implemented locally — rendered owner/topmost/focus/close and mixed-DPI evidence pending** |
| C-02 | Capture/Shelf/History correctness pass | Duplicate, discard/restore, thumbnail/restart, source identity, and preview recovery regressions pass | **Implemented locally — rendered centered-open/restart/mixed-DPI matrix pending** |
| C-03 | Context launch-scope pass | Exact included/exported items, changed/missing/unseen fail-closed behavior, naming, notes, reorder, migration, and salvage regressions pass | **Implemented locally — rendered workflow evidence pending** |
| C-04 | Dictation reliability pass | 59 deterministic controller/Core/platform rows pass for model/provider/cancel/partial/clipboard recovery | **Automated pass — real microphone/device/privacy/accent/language evidence pending** |
| C-05 | Performance baseline and top-three fixes | Reproducible startup/resource/GPU/capture-latency harness and evidence schema pass tooling tests | **Harness ready — isolated product before/after measurements and top-three fixes pending** |
| C-06 | Unified visual/accessibility acceptance | Static WPF gate is clean with 0 new/known findings; tokens, keyboard alternatives, names, and contrast defects remediated | **Static pass — Narrator/NVDA, composed contrast, live theme, reduced-motion, viewport, and mixed-DPI evidence pending** |
| C-07 | Long-run integrity soak | Capture JSON returns durable IDs; harness independently correlates ID → SQLite row → managed SHA-256 → decoded evidence; synthetic 1/1/1 test passes | **Harness/correlation implemented — dedicated-profile real 50/10/1 soak and review pending** |

Gate C exits when the focused loop works predictably on real Windows hardware
and matches the stated product direction.

## Next — Produce And Rehearse The Paid Beta

### Gate D — Distribution and commercial readiness

| ID | Work | Acceptance evidence | Status |
| --- | --- | --- | --- |
| D-01 | Select installer/signing/update architecture | Decision covers protocol/file associations, startup, uninstall, upgrade, rollback, and signing | **Not started** |
| D-02 | Build and sign release candidate | Installer and binaries pass `signtool verify /pa`; checksum and signed update manifest published | **Not started** |
| D-03 | Clean-machine matrix | Install, upgrade, uninstall, launch, and core smoke pass on clean Windows 10/11 VMs | **Blocked on D-02** |
| D-04 | Finish website delivery plumbing | Real download, checksum, checkout, and waitlist URLs; static fallback and accessibility checks pass | **Blocked on D-02/D-05** |
| D-05 | Rehearse commercial lifecycle | Purchase, webhook, email, activation, device limit/reset, refund, revocation, and support runbook pass | **Not started** |
| D-06 | Legal, support, DNS, and observability readiness | Reviewed legal URLs, working mailbox, production DNS, and privacy-safe alerts | **Not started** |
| D-07 | Paid-beta onboarding | New clean-machine user completes one capture action and one dictated insertion within ten minutes | **Not started** |

Gate D exits when a customer can discover, buy, download, install, activate,
use, update, refund, and receive support without a hidden workaround.

## Then — Memory Spine (Gate E, approved 2026-07-19)

Begins only after Gates A–C exit; Gate D external tasks (signing, checkout,
legal) proceed in parallel. Full decision record and rationale:
`docs/strategy/SCOPE-RESET-2026-07-19.md`.

| ID | Work | Acceptance evidence | Status |
| --- | --- | --- | --- |
| E-01 | Thought capture: insert / prompt / save-Thought dictation destinations with audio-first persistence and a `thoughts` schema migration | Three destinations selectable at the capture surface; a Thought whose transcription fails keeps playable audio; migration verified up and down | **Not started** |
| E-02 | Project registry + resolver v1 (explicit roots; spoken-name, registry, and prior-correction signals) | Visible confidence chip with one-click correction; low confidence lands in Unplaced, never a silent guess; no disk-wide scanning | **Not started** |
| E-03 | Thoughts surfaced in Shelf and Library with an Unplaced queue | Recent Thoughts render beside captures; Unplaced is searchable; delete and retention covered | **Not started** |
| E-04 | Projects screen: git-only lens plus resume actions | Branch, dirty count, last commit, and last touch read on open; resume opens the configured terminal/editor; mixed-DPI and keyboard acceptance | **Not started** |
| E-05 | Session lens: read-on-open Claude Code/Codex session summaries behind the Read consent boundary | Local session files parsed only on explicit open; consent shown at the boundary; no background acquisition | **Blocked — internal Workflow Intelligence revisit gate** |
| E-06 | Opt-in synthesis (catch-me-up, thought clustering, idea → brief) through the existing packet review | Exact payload and default-on redaction on every send; no new provider path | **Not started** |
| E-07 | Tidy report, read-only slice | Dirty + no-remote, stale 30 days+, and artifact sizes within registered roots only; zero destructive actions | **Not started** |

Paid-beta timing: the recorded recommendation is to launch after E-01–E-04 so
the beta ships the retention story, with E-05–E-07 as entitled updates; the
founder may pull launch earlier once Gates A–D close.

## Later — Demand-Led Only

- MCP and local-model destinations with separate capability/permission design.
- Richer safe previews where licensing and sandboxing are clear.
- Recording audio and advanced recorder features after video-only Beta is stable.
- Hosted sharing, sync, teams, or accounts only after measured paid demand.
- Command dashboard or developer mini-tools only when the focused workflow has
  repeatable retention.

Passive discovery, background monitoring, hidden sends, provider fallback, and
new generic AI workspaces remain out of scope. Explicit, pull-based reads of
durable local artifacts (git state, agent session files) on a user action are
Gate E scope and are not passive discovery.

## Decisions Requiring Founder Authority

Execution proceeds with contextual handoff and the Aquarium website as the
recovery defaults. Only these remaining choices require founder authority:

1. Select offline trial policy after options expose enforcement and support
   consequences.
2. Approve archive/delete actions after the non-destructive worktree inventory.
3. Supply or approve GitHub billing/plan access, code-signing identity/certificate,
   legal text, pricing, refund terms, and production checkout activation.

## Release Definition

The paid beta is release-ready only when Gates A–D are complete, the release
commit is green in GitHub Actions, the exact signed artifact passes the clean-VM
matrix, and the website points to that artifact and a rehearsed checkout. A
passing local unit suite alone is not a release decision.

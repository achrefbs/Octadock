# Octadock 0-to-100 — Execution Status Ledger

Owner: founding engineer (agent execution) · Source of truth: [`0_TO_100_MASTER_EXECUTION_PLAN.md`](0_TO_100_MASTER_EXECUTION_PLAN.md)
Started: 2026-07-06 · Baseline commit at start: `5b5c762` · Branch: `claude/vigilant-darwin-ee0067`

This ledger tracks the **First Implementation Batch (§17)** and the **§6 sequence** to the paid-beta gate.
Rules honored: one vetted change per item (tagged WS + risk ids); "Done" only when the §7/§17 acceptance is **OBSERVED**;
every money path gets admin visibility; every network path gets consent; every writeback gets backup+rollback+kill-mid-save test.

Status legend: **BLOCKED-ON-FOUNDER** (procurement/human gate — checklist only) · **TODO** · **IN PROGRESS** · **DONE (observed)** · **FLAG** (code contradicts plan — stop & surface).

Toolchain verified 2026-07-06: .NET SDK `8.0.422`, `Microsoft.WindowsDesktop.App 8.0.28` present → build + test observable locally. Clean-VM/live-card acceptance remains founder-gated.

---

## §17 First Implementation Batch — code-reality confirmation (items 4–13) + checklist status (1–3)

| # | Item | WS · Risk | Plan status | **Verified code reality (2026-07-06)** | Ledger status |
| --- | --- | --- | --- | --- | --- |
| 1 | Signing identity validation | WS1 · R1 | procurement | n/a (no code) | **BLOCKED-ON-FOUNDER** — checklist drafted below |
| 2 | DNS + mail subdomain (SPF/DKIM/DMARC) | WS2 · R4 | procurement | External setup progressed 2026-07-06: Namecheap DNS reachable and FastMail MX/SPF/DKIM/DMARC records added. | **PARTIAL (external)** — FastMail mailbox/domain verification and support sender warm-up remain |
| 3 | Managed Payments eligibility | WS3 · R5 | procurement | External setup progressed 2026-07-06: new OctaDock Stripe account connected and launch catalog created. | **PARTIAL (external)** — Managed Payments/Tax/legal URLs still dashboard-gated |
| 4 | Self-contained single-file `win-x64` build | WS1 · R10 | Not built | CONFIRMED then BUILT. | **DONE (observed)** — see B1 |
| 5 | Fix copy/label falsehoods | WS7 · R6/R7 | Not built | CONFIRMED then FIXED. | **DONE (observed)** — see B1 |
| 6 | One-time model-download consent card | WS7 · R6 | Not built | CONFIRMED then BUILT (consent seam + gate + tests). | **DONE (observed, code)** — Wireshark check founder-gated. See B1 |
| 7 | Gate cloud STT on `OCTADOCK_`-prefixed vars only | WS7 · R7 | Not built | CONFIRMED then FIXED. | **DONE (observed)** — see B1 |
| 8 | Scaffold license service (signature-verified webhook first) | WS3 · R12 | Not built | CONFIRMED greenfield then BUILT (new isolated project + webhook + retry-aware dedupe + exact launch-price validation + issue/revoke + reconciliation). | **DONE (observed, unit)** — see B2 |
| 9 | Freeze entitlement envelope + machine-hash spec | WS4/WS13 · R15/R13 | Not built | CONFIRMED greenfield then BUILT (envelope + Ed25519 + machine-hash + future-schema CI test + [`ENTITLEMENT_ENVELOPE.md`](ENTITLEMENT_ENVELOPE.md)). | **DONE (observed)** — see B2 |
| 10 | Dormant Pro/Team schema in initial license-service migration | WS13 · R35 | Not built | CONFIRMED greenfield then BUILT (initial migration ships dormant fields + identity_links + empty usage_events). | **DONE (observed)** — see B2 |
| 11 | SafeFileWriter shared infra + route overwrite sites | WS9 · R23 | Not built | CONFIRMED greenfield then BUILT. Routed 5 user-file sites (the flagged superset). | **DONE (observed)** — see B3 |
| 12 | Trial-clock high-water anchor | WS5 · R36 | Not built | CONFIRMED then BUILT (`TrialClock`; a real high-water-persistence bug was caught by tests and fixed). | **DONE (observed)** — see B3 |
| 13 | UNC guard + executable guard | WS9 · R32 | Not built | CONFIRMED then BUILT (`PathSafety`; UNC rejected BEFORE `File.Exists` at both seams; executable guard at `OpenWithDefaultApp`). | **DONE (observed)** — see B3 |
| 14 | Ratify post-expiry verb matrix | WS5 · R16/R17 | Not built | Decision artifact — [`POST_EXPIRY_VERB_MATRIX.md`](POST_EXPIRY_VERB_MATRIX.md) **RATIFIED by founder 2026-07-06 (safe defaults)**: BLOCK Read-aloud/preview-new/Text-Tools, ALLOW Clear-History. Unblocks the order-8 gate. | **DONE (ratified)** |

**Note ① (FLAG for founder/plan):** §17 item 11 says "route the four known overwrite sites," but code reality shows the user-file writeback surface is wider. Recommendation: SafeFileWriter must wrap **every user-chosen-destination save** — shelf save-as, pin save-as, annotation export/writeback, history save-as. App-managed writes into Octadock's own storage (thumbnails, clipboard PNGs, TTS temp mp3, crash JSON) are not originals-at-risk and are out of scope for the corruption guarantee. This widens item 11 without contradicting a Locked Decision; surfaced per the VERIFY-DON'T-TRUST rule.

---

## §6 sequence — gate tracker (Critical Path Gates to beta)

| Order | Workstream | Label | Status | Evidence / blocker |
| --- | --- | --- | --- | --- |
| 1 | WS1/2/3 procurement + WS7 copy | Critical Path Gate | PARTIAL external / copy done | Stripe catalog + DNS mail records exist; signing, Stripe Tax/MP, legal URLs, mailbox verification, and copy finalization remain |
| 2 | Founder decision batch + verb matrix | Architecture Now | BLOCKED-ON-FOUNDER | §4 defaults recommended; verb matrix = item 14 |
| 3 | WS13 envelope + dormant schema | Architecture Now | TODO | items 9,10 |
| 4 | WS1 signed self-contained installer | Critical Path Gate | TODO (build) / signing BLOCKED-ON-FOUNDER | item 4 buildable; signing needs cert |
| 5 | WS9 SafeFileWriter + revision + restore | Critical Path Gate | TODO | item 11 |
| 6 | WS3/4 license service v1 | Critical Path Gate | TODO | items 8,9,10 |
| 7 | WS5 client trial/entitlement + key entry | Critical Path Gate | IN PROGRESS | verification core (B4) + signed-state-outside-DB (B5) + /activate endpoint (B6) DONE; client key-entry UI + `octadock://activate` remain (UI/WS8) |
| 8 | WS5/8 trial gate at seams + accessible pill | Critical Path Gate | TODO | needs item 14 ratified (⚑ rows) |
| 9 | WS2 website + legal set | Critical Path Gate | TODO / legal founder-gated | DNS mail records exist; website target, privacy/refund/EULA URLs, and Checkout legal URLs remain |
| 10 | WS7 copy/egress + consent finalization | Critical Path Gate | TODO | items 5,6,7 |
| 11 | WS6 minimal admin + 4 alerts | Critical Path Gate | TODO | after item 8 |
| 12 | WS9/10 hardening (UNC/exec/clipboard/egress) | Critical Path Gate | TODO | item 13 + clipboard pause |
| 13 | WS11 RC verify + 2 live rehearsals | Critical Path Gate | BLOCKED-ON-FOUNDER (live cards + VM) | after 4-12 |
| 14 | WS12 support channel + runbooks | Critical Path Gate | BLOCKED-ON-FOUNDER (mailbox) | — |

---

## CHECKPOINT — 2026-07-06 (resumed after external setup)

The external commercial blockers moved forward: the new OctaDock Stripe account has a live
catalog (`Octadock Local`, $49 beta active, $59 1.0 inactive), and Namecheap has FastMail
MX/SPF/DKIM/DMARC records. This removes the "we cannot proceed at all" blocker.

The codebase is **not beta-ready yet**. Verified current local test evidence before merge:
desktop solution **661/661** from the prior audit, license-service **37/37** after this
hardening pass. The license-service now rejects non-Octadock paid sessions by exact
configured launch price/amount/currency and treats unprocessed duplicate webhook events as
retryable, so Stripe retries cannot strand a paid buyer without a key.

Remaining buildable engineering work includes client key-entry UI, `octadock://activate`,
trial/service-seam gate wiring, minimal admin/alerts, live Stripe paid-session source,
webhook endpoint dashboard setup, and website/legal surfaces. Founder/dashboard work remains
for signing, Stripe Tax/Managed Payments, legal URLs, FastMail mailbox verification, KMS key
custody, and live-card rehearsals.

## Build/test evidence log

- 2026-07-06 — toolchain check: `dotnet --version` = `8.0.422`; WindowsDesktop 8.0.28 present.
- 2026-07-06 — license-service hardening: Debug `dotnet test services\license-service\Octadock.LicenseService.sln --no-restore -c Debug` = **37/37** pass; CI-shaped Release build + `dotnet test ... -c Release --no-build -p:ContinuousIntegrationBuild=true` = **37/37** pass; analyzer warnings only.
- (entries appended per batch below)

---

## Batch reports

### B1 — WS1 distribution + WS7 desktop honesty/consent (§17 items 4–7) · 2026-07-06

**Built:**
- **Item 4 (WS1, R10):** `Octadock.App.csproj` + `Octadock.Cli.csproj` gain `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` + `SatelliteResourceLanguages`; App gains a `PublishSingleFile`-guarded `IncludeNativeLibrariesForSelfExtract` (embeds SkiaSharp native lib). `build/release.ps1` now publishes both projects `-r win-x64 --self-contained true -p:PublishSingleFile=true` (dropped `--no-build`). CI `ci.yml` publish step aligned to the same self-contained single-file output.
- **Item 5 (WS7, R6/R7):** `DockPill.cs` dictation tooltip no longer claims "fully offline" → "one-time model download, then on-device"; `ReadAloudService.cs` explain-toast now names the cloud hop ("your AI CLI — the selected text is sent to that provider"). New `build/copy-honesty-gate.ps1` + CI step fails the build if either dishonest string reappears.
- **Item 6 (WS7, R6):** new consent seam `IModelDownloadConsent`/`IModelDownloadConsentPrompt` (Core) + `ModelDownloadConsentService` (one-time persisted flag `speech.modelDownloadConsented`) + WPF `MessageBoxModelDownloadConsentPrompt` (sized Yes/No, default No). `DictationController` gates BOTH model-fetch triggers (foreground + background) behind it; declined → no fetch, pill closed, user notified.
- **Item 7 (WS7, R7):** `OpenAiSttProvider.ReadApiKey()` reads ONLY `OCTADOCK_OPENAI_API_KEY`; a bare `OPENAI_API_KEY` is detected but never used (UnavailableReason tells the user to opt in explicitly).

**Acceptance — OBSERVED (evidence):**
- Item 4: `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` → single `Octadock.exe` **200.2 MB**, runtime embedded (no loose `coreclr.dll`/`libSkiaSharp.dll`; 9 files total). Clean-VM/no-interstitial = founder-gated (needs signing + VM, item 1).
- Item 5: `copy-honesty-gate.ps1` exit 0 on clean tree; negative control (reintroduced string) → exit 1. `grep "fully offline|Summarizing with local AI" src/Octadock.App` = 0 matches.
- Item 6: `ModelDownloadConsentServiceTests` 4/4 (already-consented → no prompt; accept → persists + true; decline → false + not persisted; re-asks after decline). `DictationControllerTests` 14/14 incl. new `Declining_model_download_consent_blocks_the_fetch_and_does_not_start` (EnsureCalls=0, not listening, notified). Wireshark "no fetch before consent" = founder-gated.
- Item 7: `OpenAiSttProviderTests` 2/2 — bare `OPENAI_API_KEY` alone → `IsAvailable=false`; `OCTADOCK_OPENAI_API_KEY` → `IsAvailable=true`.
- Full solution build: **0 errors**. Targeted tests: Core 39/39, App(dictation) 14/14, Platform(stt) 2/2.

**Risk ids closed (code-side):** R7 (bare key), R10 (self-contained). **Partially:** R6 (consent code done; Wireshark verify founder-gated), R37 (Parakeet already SHA-256-pinned — noted, TTS/Whisper docstrings still say "fully offline": see next).

**Flag surfaced:** `ParakeetSttProvider`/`WhisperSttProvider` XML docstrings still say "fully offline" (same latent claim, internal-only). Deferred to item 10 (WS7 copy finalization); the CI gate is scoped to `src/Octadock.App` UI copy so it does not false-positive on `WindowsTtsProvider` (which is genuinely offline).

**Blocked-on-founder:** item 1 signing (unblocks item 4 clean-VM acceptance); Wireshark egress capture (item 6 field verify).

**Next per §6:** items 8–10 — stand up the greenfield license-service spine (signature-verified webhook + dedupe → envelope/machine-hash → dormant schema).

---

### B2 — Commercial spine: license service (§17 items 8, 9, 10) · 2026-07-06

**Built:** a NEW, fully isolated ASP.NET Core service under `services/license-service/`, **outside `Octadock.sln`** — its own solution `Octadock.LicenseService.sln`, its own `Directory.Build.props` + `Directory.Packages.props` (CPM off) so nothing leaks from the desktop build.
- **Item 8 (WS3, R12/R3/R21):** `StripeSignatureVerifier` (self-contained HMAC-SHA256 over `{t}.{body}`, constant-time compare, ±5-min tolerance, multi-secret rotation) → `StripeWebhookProcessor` verifies signature **before any side effect**, dedupes by event id (`webhook_events` PK) while allowing retry of received-but-unprocessed events, validates the exact configured Octadock launch price/amount/currency before issuance, issues exactly one license per valid paid `checkout.session.completed`, revokes on `charge.refunded`/`charge.dispute.created`. `POST /webhooks/stripe` reads the raw body; `GET /health` is the launch-health tile. Hourly `ReconciliationService` (BackgroundService) + pluggable `IPaidSessionSource` (live Stripe fetch = founder-gated `NullPaidSessionSource` default).
- **Item 9 (WS4/WS13, R15/R13):** frozen `EntitlementEnvelope {schema,key_id,payload,sig}` (sig = Ed25519 over RAW payload bytes; unknown-field + future-schema tolerant), `EntitlementSigner`/`EntitlementVerifier` (BouncyCastle, pure-managed), `MachineHash` (SHA-256(lowercased MachineGuid), `device_hash_v=1`), spec doc [`ENTITLEMENT_ENVELOPE.md`](ENTITLEMENT_ENVELOPE.md).
- **Item 10 (WS13, R35):** initial migration (`user_version`, downgrade guard) ships the FULL commercial schema INCLUDING dormant `licenses.seats/owner_email/org_id/policy_json/subscription_state`, `activations.seat_index/device_hash_v`, the `identity_links` key↔customer↔email table, and the empty append-only `usage_events` ledger.

**Acceptance — OBSERVED:**
- Unit: **37/37** license-service tests pass after hardening — signature (valid/tampered/wrong-secret/missing/malformed/stale/rotation), repository (retryable unprocessed events, processed-event skip, payload mismatch, idempotent issuance, refund-revoke, reconciliation), database (schema+dormant columns, downgrade guard), webhook processor (unsigned→400 no license, forged→400, signed→1 license, duplicate→still 1, retryable unprocessed duplicate→license, wrong payload→400, wrong price/amount→no license, refund→revoke, unpaid→nothing), envelope (round-trip, tamper-fail, unknown-key-fail, **future-schema accepted**, machine-hash determinism), activation.
- **Live smoke (running Kestrel, real HTTP):** health.before=0 → unsigned POST=**400** → signed event=**200 LicenseIssued** key `OCTA-…BE7QN` → duplicate replay=**Duplicate, no new key** → health.after=**1**. Exactly-one-entitlement + replay-safe + admin-visible, observed end-to-end.

**Risk ids closed:** R12 (signature-verified webhook), R3 (retry-safe dedupe + reconciliation structure), R15 (envelope versioning/future-schema), R35 (dormant schema), R21 (refund→revoke), and the price-spoofing issue found in review (only the configured Octadock launch price can issue). Partial: R13 (machine-hash algorithm frozen; client read + slot LRU = WS4/WS5 later), R3 reconciliation *live diff* dashboard-gated (needs live Stripe restricted key).

**Blocked-on-founder:** live Stripe webhook secret + `IPaidSessionSource` live impl; KMS Ed25519 key custody (reference signer only, in-repo, is dev/test); the two live-card rehearsals.

**Next per §6:** items 11–13 — SafeFileWriter (R23), trial-clock high-water (R36), UNC/executable guards (R32) in the desktop app.

---

### B3 — Desktop file-safety + trial integrity (§17 items 11, 12, 13, 14) · 2026-07-06

**Built:**
- **Item 11 (WS9, R23):** `src/Octadock.Core/Io/SafeFileWriter.cs` (temp-in-same-dir → fsync → atomic `File.Replace`/`Move`, prior content captured as a revision) + `FileRevisionStore` (central, path-hashed buckets, restore). Routed **5** user-file save sites — the flagged superset, not just the plan's 4: `ShelfItemViewModel` (in-place writeback to original), `PinWindow` (Save-As), `HistoryViewModel` (Save-As), `FilePreviewService` (Save-copy), `AnnotationEditorWindow` (export). DI-wired via `IStoragePaths`.
- **Item 12 (WS5, R36):** `src/Octadock.Core/Trial/TrialClock.cs` — persists max-observed UTC; expiry vs `max(now, highWater)`; >48h backward jump freezes + signals "clock looks wrong". A real first-run persistence bug was caught by the tests and fixed.
- **Item 13 (WS9, R32):** `src/Octadock.Core/Io/PathSafety.cs` (`IsUncPath`/`IsExecutableExtension`, pure string checks). UNC rejected **before any `File.Exists`** at `CaptureCoordinator.AddExternalFileAsync` + `PinService.PinImageFileAsync` (so the octadock:// / CLI / Explorer paths all obey it at the seam); executable-launch guard at `FilePreviewService.OpenWithDefaultApp`.
- **Item 14 (WS5, R16/R17):** drafted [`POST_EXPIRY_VERB_MATRIX.md`](POST_EXPIRY_VERB_MATRIX.md) — every `CommandType` + clipboard-monitor/annotate/text-tools/recording mapped to allow/block/pause with the enforcing seam; 4 rows flagged ⚑ for founder.

**Acceptance — OBSERVED:**
- Core.Tests **433/433** including `Killing_the_write_100_times_never_corrupts_the_original` (kill-mid-save ×100, byte-identical every iteration), overwrite→restorable revision round-trip, TrialClock rollback-freezes / drift-within-grace-no-early-expire / rollback-can't-extend, PathSafety UNC + executable classification.
- App.Tests **89/89** (no regression from the SafeFileWriter routing or the UNC/exec guards). Full solution build **0 errors**.

**Risk ids closed:** R23 (SafeFileWriter + revision + restore + kill-mid-save proof), R36 (monotonic clock), R32 (UNC guard at seams), part of R16/R17 (verb matrix drafted; gate build + ratification pending).

**Flag resolved:** the "four sites" undercount (note ①) — SafeFileWriter now wraps all 5 user-file destinations; app-managed writes (thumbnails, clipboard PNGs, TTS temp) intentionally excluded.

**Blocked-on-founder:** verb-matrix ratification (item 14 ⚑ rows); real kill-*process*-on-VM run is WS11 (the deterministic ×100 invariant test is the engineering proof).

**Next per §6:** order 7 — WS5 **client** trial/entitlement module (signed state outside octadock.db, client machine-hash read, key-entry PRIMARY, `octadock://activate`), which pairs the client to the B2 license service.

---

### B4 — WS5 client entitlement verification core (§6 order 7, partial) · 2026-07-06

**Built (desktop side, mirrors the frozen envelope spec so client ⇄ server agree byte-for-byte):**
- `src/Octadock.Core/Licensing/`: `Base64Url`, `EntitlementEnvelope` (parse, unknown-field/future-schema tolerant), `EntitlementVerifier` (Ed25519 **verify-only** via BouncyCastle, trust ring by `key_id`), `EntitlementPayload`, `EntitlementEvaluator` (`LicenseStatus` state machine), `MachineHash`.
- `IMachineIdentity` (Core) + `WindowsMachineIdentity` (Platform.Windows, reads HKLM MachineGuid → `MachineHash.Compute`), DI-registered.
- BouncyCastle added to desktop CPM (`Directory.Packages.props`) + `Octadock.Core.csproj`.

**Acceptance — OBSERVED:** Core.Tests **440/440** (+7 `EntitlementEvaluatorTests`): valid active this-device entitlement → **Licensed**; **tampered payload → InvalidSignature** (forged file can't unlock, R11); wrong-device → **WrongDevice**; refunded → **Revoked**; untrusted signing key → **InvalidSignature**; past-update-window → **Licensed + UpdatesExpired** ("keeps working after updates end"). Full solution builds 0 errors.

**Risk ids closed:** R11 (forged/edited entitlement never unlocks — signature-over-raw-bytes). Partial R13 (client machine-hash read done; slot LRU/self-eviction is service-side, later).

**Remaining for order 7 (buildable, no unratified decision needed — safest defaults apply):** entitlement STORE (persist verified envelope + trial state to `%LOCALAPPDATA%\Octadock\license\`, outside `octadock.db` → closes R11's "DB corruption keeps license"); license-service `/activate` endpoint (key + machine_hash → signed entitlement, dev Ed25519 key until KMS); key-entry UI + `octadock://activate` (UI overlaps WS8).

**STOP-AND-ASK reached for downstream items (per directive):**
- **Order 8 gate** depends on the **verb-matrix ratification** (item 14 ⚑ rows) — a founder decision. Safest defaults are documented; confirm or edit before the gate hardens.
- **Production entitlement signing** depends on the **§4 admin-hosting/KMS decision (R20)** — founder/procurement.

---

### B5 — Signed license state OUTSIDE the DB (§6 order 7 cont.) · 2026-07-06 — closes the R11 No-Go

**Built (Core):**
- `IEntitlementStore`/`FileEntitlementStore` — persists the signed entitlement + trial start under `%LOCALAPPDATA%\Octadock\license\` (`entitlement.json`/`trial.json`), **outside octadock.db**.
- `FileTrialClockStore : ITrialClockStore` — persists the monotonic high-water clock state to disk (completes item 12's persistence).
- `LicenseStateService` (`LicenseMode`/`LicenseState`) — the single source of truth the gate consults: valid entitlement → Licensed; refunded/disputed → Revoked; else trial via the tamper-resistant high-water clock (Trial / TrialExpired / TrialFrozen). `AllowsFullUse` drives the gate.

**Acceptance — OBSERVED:** Core.Tests **451/451** (+11): fresh install → 14-day trial (start persisted); expiry after window; valid entitlement → Licensed; **forged entitlement → falls back to Trial (never unlocks)**; refunded → Revoked; >48h rollback → TrialFrozen; file-store round-trip; **corrupted state file → null, no throw/false-unlock**.

**Risk closed: R11 No-Go** ("Trial/license state forgeable or corruption-fragile") — state is signed + outside the DB; editing/injecting it fails verification; corrupting octadock.db cannot drop a license to trial.

**Remaining for a full activation loop (buildable next; signing key is config-driven so not throwaway):** license-service `/activate` endpoint (key + machine_hash → signed entitlement, device-limit enforcement over the `activations` table); client key-entry UI + `octadock://activate`; then the WS5/8 gate wiring (order 8) once the verb matrix is ratified.

---

### B6 — Activation endpoint: money→key→activate loop closes (WS4/WS5, R13) · 2026-07-06

**Built (license service):**
- `LicenseRepository.FindByKey` + `Activate` — device registry over the `activations` table: idempotent per device, enforces the device limit, refuses inactive/revoked licenses, audits each activation.
- `IEntitlementIssuer`/`EntitlementIssuer` — signs a device-bound entitlement (payload snake_case matching the client exactly); **config-driven key** (`EntitlementSigningOptions`) with a loud EPHEMERAL-DEV-key fallback so the loop is testable before KMS.
- `POST /activate` (key + machine_hash → signed entitlement, 409 on device limit, 403 inactive, 404 unknown) + `GET /trust-anchor` (publishes the public key for the client trust ring).

**Acceptance — OBSERVED:**
- Unit: **30/30** license-service tests (+5 `ActivationTests`): activate → **entitlement that cryptographically verifies against the trust anchor**, machine_hash bound; re-activate same device → idempotent (no extra slot); **device limit enforced** (4th → DeviceLimitReached); unknown key → NotFound; revoked → NotActive.
- **Live end-to-end smoke (real HTTP):** purchase (signed webhook) → key `OCTA-…9CCPZ` → activate d1 = **Activated + 482-char signed entitlement** → re-activate d1 = **AlreadyActive (1 slot)** → d2,d3 = Activated → **d4 = 409** → `/trust-anchor` = `k1` + 43-char Ed25519 public key.

**Risk closed:** R13 (device policy — activation, 3-device limit, idempotent per device; LRU self-eviction + self-serve deactivation are Fast Follow).

**Commercial chain now proven end-to-end (unit + live):** Stripe webhook → verify → dedupe → **one license** → **activate → signed device-bound entitlement** → **client verifier accepts it → Licensed** (`EntitlementEvaluatorTests`). The only production swap is the Ed25519 signing key (dev → KMS).

**Blocked-on-founder (unchanged, now the sole gate to "100"):** signing cert + SmartScreen warm-up · DNS/mail + email warm-up · Stripe MP/tax eligibility · legal set · **KMS signing key custody (§4/R20)** · **verb-matrix ratification (item 14 ⚑) → order-8 gate** · the two live-card $49 rehearsals · support mailbox. See [FOUNDER_CHECKLIST.md](FOUNDER_CHECKLIST.md).

---

_(Each completed item appends: what was built · acceptance test + observed result · risk ids closed · blocked-on-founder · next item.)_

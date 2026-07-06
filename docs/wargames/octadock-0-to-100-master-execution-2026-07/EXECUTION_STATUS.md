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
| 7 | WS5 client trial/entitlement + key entry | Critical Path Gate | **DONE (observed)** | verification core (B4) + signed-state-outside-DB (B5) + /activate endpoint (B6) + client DI/trust-anchor/activation service (B7) + Account key-entry UI (B8) + `octadock://activate` (B8). Trust anchor `dev1` embedded; production KMS key founder-gated |
| 8 | WS5/8 trial gate at seams + accessible pill | Critical Path Gate | **DONE (observed)** | `LicenseGate` gates 10 seams + clipboard-monitor PAUSE per the ratified matrix; refusals route to one announced toast; Account chips carry text labels (B9). Narrator-on-VM verify remains founder-gated |
| 9 | WS2 website + legal set | Critical Path Gate | **DONE (built) / legal review founder-gated** | static `web/` site (index/pricing/privacy/refunds/eula/terms) with honest copy + egress table; download URL/SHA-256, live Checkout link, DNS host, and legal review founder-gated (B11) |
| 10 | WS7 copy/egress + consent finalization | Critical Path Gate | **DONE (observed)** | items 5,6,7 (B1); egress table published on the website (B11); copy-honesty CI gate green |
| 11 | WS6 minimal admin + 4 alerts | Critical Path Gate | **DONE (observed)** | `/admin/health` launch-health page (~8 numbers) + 4 alert seams via `IAlertSink`; email/phone delivery + Cloudflare Access founder-gated (B10) |
| 12 | WS9/10 hardening (UNC/exec/clipboard/egress) | Critical Path Gate | **DONE (observed)** | UNC/exec guards (B3) + clipboard-monitor PAUSE at expiry (B9) |
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

Remaining buildable engineering work (client key-entry UI, `octadock://activate`,
trial/service-seam gate wiring, minimal admin/alerts, live Stripe paid-session source,
website/legal surfaces) is now **BUILT and observed** — see batches B7–B11 and the
"paid-beta client + gate + admin milestone" checkpoint at the end of this file. Founder/dashboard
work still remains for signing, Stripe Tax/Managed Payments + webhook secret, legal review,
FastMail mailbox verification, KMS key custody, and the live-card rehearsals.

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

### B7 — WS5 client licensing DI + trust anchor + activation service (§6 order 7) · 2026-07-06

**Built (Core):** the client licensing module is now COMPOSED and reachable, not just present.
- `ClientTrustAnchors` — the embedded Ed25519 trust ring. Ships the DEV key `dev1` (public half embedded; the matching private seed lives ONLY in the license service's `appsettings.Development.json`) so the money→key→activate→Licensed loop is verifiable end-to-end locally. Production `k1` (KMS) public key is added here when custody lands — **founder-gated (R20)**.
- `LicenseKeyNormalizer` — dash/space/case-tolerant canonicalization to `OCTA-XXXXX-XXXXX-XXXXX-XXXXX`.
- `IActivationClient`/`HttpActivationClient` (POST `/activate`, maps HTTP status → transport outcome; unreachable host is a first-class `EndpointUnavailable`, not a crash) + `ActivationOptions` (base URL `api.octadock.com`, env-overridable; **live host founder-gated**).
- `ActivationService` — normalizes the key, calls the service, then **verifies the returned entitlement locally (Ed25519, this-device, active) before persisting** — a hostile/misconfigured server cannot unlock the app.
- `AddOctadockLicensing()` DI extension (Core) wired into `Program.cs`: `IEntitlementStore`/`ITrialClockStore`/`TrialClock`/`EntitlementVerifier`(trust ring)/`EntitlementEvaluator`/`LicenseStateService`/`ILicenseGate`/`ActivationService`. Signed state lives under `%LOCALAPPDATA%\Octadock\license\`, outside octadock.db.

**Acceptance — OBSERVED:** Core.Tests licensing **51** pass, incl. `ClientTrustAnchorsTests` — the embedded `dev1` PUBLIC key verifies a signature from the committed DEV private seed (the two halves are proven to match); `ActivationServiceTests` — valid this-device entitlement stored; wrong-device / forged entitlement rejected and NOT stored; transport failures map to user kinds; malformed key never calls the service; `HttpActivationClientTests` — request shape + status→outcome mapping + connection-failure→EndpointUnavailable. The running license service's `/trust-anchor` returns exactly the embedded key `uot8gEBPMjaM6JwMAN03DZJgPDSvxmUyyJYmHgrMuuw` (cross-checked against the dev keypair).

**Risk closed (code-side):** the client counterpart to R2/R13 — activation composes and stores only locally-verified entitlements. **Blocked-on-founder:** live `api.octadock.com` host + KMS production signing key (R20).

---

### B8 — Activation UI + `octadock://activate` (§6 order 7 cont.) · 2026-07-06

**Built:**
- **Settings → Account & Billing** (`SettingsWindow.xaml` + `SettingsViewModel`): the PRIMARY activation path (R2). A dash/space/case-tolerant paste box, an **Activate** button (disabled while in flight), text-labelled state chips (Trial N days left / Licensed / Trial ended / Paused / Revoked — **text, not colour alone**, R40), and a screen-reader-announced result line (`AutomationProperties.LiveSetting="Assertive"`). State refreshes on window activation. Keyboard-operable; the box carries `AutomationProperties.Name`.
- **`octadock://activate?key=…`** (+ the `activate` CLI verb): new `CommandType.Activate` + token + parser `key` option; `CommandDispatcher.RouteActivateAsync` runs activation, announces the outcome via a notification, and opens Account & Billing. **Exempt from the protocol/CLI automation toggles** (`AutomationLaunchSafety.IsActivationLaunch`) so a buyer's deep link works before they enable automation. CLI help + footer added.

**Acceptance — OBSERVED:** full desktop solution builds 0 errors (incl. XAML); Core command tests **106**, CLI **17**, App `AutomationLaunchSafety` **12** (incl. new activation-launch exemption cases). Clean-VM Narrator run remains WS11/founder-gated.

**Risk closed (code-side):** R2 (key-entry as the primary path + a visible, announced deep-link accelerator).

---

### B9 — Trial/license gate at the service seams (§6 order 8) · 2026-07-06 — closes R16

**Built:** `ILicenseGate`/`LicenseGate` (Core) consulting `LicenseStateService.AllowsFullUse`. Enforced at the **service seams**, never at scattered UI call sites, so dock/hotkey/`octadock://`/CLI/Explorer all obey one rule. Per the RATIFIED verb matrix it BLOCKS new content/compute and ALLOWS viewing/exporting existing data. Seams gated: `CaptureCoordinator` (capture chokepoint + `AddExternalFileAsync`), `OcrService` (region×2 + file), `DictationController` (start only), `ReadAloudService` (start), `FilePreviewService` (preview-new), `PinService` (new pins; restore exempt), `RecordingController` (start only), `AnnotationService` (new raster job; existing `.octadock` open exempt), text-tools (via `WindowPresenter` chokepoint), and the **clipboard monitor PAUSES** at expiry (R17). Every refusal routes to one non-modal, throttled, screen-reader-announced toast that deep-links to Account & Billing.

**Acceptance — OBSERVED:** `LicenseGateTests` (Core) **5/5** — active trial allows without prompting; expired trial blocks and raises exactly one announced prompt; valid license allows; revoked blocks; repeated refusals throttle to one toast. App.Tests over the gated services (Dictation/Recording/Clipboard) **25** green under an always-allow fake. Full desktop suite **701** pass.

**Risk closed:** R16 (gate at seams, no silent no-op), R17 (clipboard pause). Ambient dock badge (day-7/day-11) and the on-VM Narrator walkthrough are WS8/WS11 follow-ups.

---

### B10 — License-service admin/ops + alerts + live Stripe reconciliation source (§6 orders 6/11) · 2026-07-06

**Built (isolated license-service solution):**
- **Launch-health** expanded (`GetLaunchHealth` + `/admin/health` HTML page + JSON `/health`): licenses issued 24h, total/active/revoked, webhook count + most-recent-event age, reconciliation diff + configured flag, activation success rate (failures now audited), issued-not-activated %. Email delivered/bounced + resend are `null` / "no data by design" (email delivery founder-gated) — never fabricated. `/admin/health` supports an optional `Admin:Token`; an UNAUTHENTICATED banner shows until Cloudflare Access + WebAuthn front it (founder-gated, R20).
- **Four alert seams** via `IAlertSink`/`AlertEvaluator` (default `LoggingAlertSink`): webhook staleness >60 min, reconciliation diff >0, activation success <90% over ≥20 attempts, and an email-bounce branch that only fires once an email-status source exists. Evaluated each reconciliation cycle, non-fatal. Email/phone delivery founder-gated.
- **`StripePaidSessionSource`** — live Stripe REST source (injectable handler, pagination) selected when a restricted API key is configured; else the existing `NullPaidSessionSource` (which reports "not configured" rather than a false-clean diff). **Live restricted key founder-gated (external blocker).**
- `appsettings.Development.json` carries the DEV `dev1` signing key matching the client trust anchor; production key stays empty (KMS).

**Acceptance — OBSERVED:** license-service **65/65** tests pass (Release, CI-shaped); live smoke: `/health` full JSON, `/trust-anchor` = the embedded dev key, `/admin/health` enforces the token gate. **Blocked-on-founder:** live Stripe key, email-status source, phone paging, KMS key, Cloudflare Access.

---

### B11 — Minimum honest website + legal surfaces (§6 orders 9/10) · 2026-07-06

**Built (`web/`, static, self-contained — no CDN):** `index.html` (download-first hero, signed-installer note, SHA-256 placeholder, honest feature list incl. recording marked video-only, network-egress table), `pricing.html` ($49 one-time Local · 3 devices · 12 mo updates · includes 1.0; Pro = **waitlist**, violet; Context = **in development**; EU immediate-supply consent note), `privacy.html` (enumerates every license-service field + retention + processors + egress table), `refunds.html` (voluntary 14-day + EU withdrawal/immediate-supply + statutory carve-out), `eula.html`/`terms.html` (marked **DRAFT — pending legal review**), `styles.css`, `README.md`.

**Acceptance — OBSERVED:** honesty grep clean — no "fully offline", "local AI", "AI Discovery/Sessions", or "recording with audio" in any page; renders standalone; mobile has no horizontal body overflow; contrast/focus/reduced-motion meet WCAG AA. Palette taken from the shipped WPF dark theme (teal local, violet reserved for cloud/Pro). **Founder-gated:** download build URL + SHA-256, live Stripe Checkout link, DNS host target, and legal review.

---

## CHECKPOINT — 2026-07-06 (paid-beta client + gate + admin milestone)

Every buildable Critical-Path-Gate engineering item for the paid beta is now built and
observed green. The money→key→activate loop is proven end-to-end at the unit level on BOTH
sides and cross-checked at runtime (`/trust-anchor` = the client's embedded `dev1` key). The
gate is enforced at the service seams per the ratified matrix, with announced refusals. The
license service has a launch-health surface + four alert seams + a live-Stripe reconciliation
seam. A minimal honest website + legal set exist.

Verified local evidence this pass: desktop **701/701** (`dotnet test Octadock.sln -c Debug`);
license-service **65/65** (Release, CI-shaped); copy-honesty gate green. The sole remaining
gates to "100" are founder/dashboard/procurement (see §6 rows 13–14): code-signing cert +
SmartScreen warm-up, live Stripe restricted key + Tax/MP posture + webhook secret, KMS signing
key custody, FastMail mailbox/support sender, legal review, and the two live-card $49
rehearsals on a clean VM.

## Build/test evidence log (appended)

- 2026-07-06 — client licensing composed: Core.Tests licensing **51/51** (trust-anchor keypair cross-check, activation orchestration, HTTP client, gate).
- 2026-07-06 — full desktop suite: `dotnet test Octadock.sln --no-restore -c Debug` = **701/701** pass (Core 484, App 96, Data 77, Platform 27, Cli 17); 0 skipped, 0 errors.
- 2026-07-06 — license-service admin/alerts/Stripe source: Release build 0 warnings/0 errors; `dotnet test ... -c Release` = **65/65** pass.

---

_(Each completed item appends: what was built · acceptance test + observed result · risk ids closed · blocked-on-founder · next item.)_

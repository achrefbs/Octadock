# Octadock License Service

The commercial backend (WS3/WS4/WS6). A verified paid Stripe Checkout produces
exactly one license record; activation of that license produces a signed,
device-bound entitlement. This is a **separate, cross-platform ASP.NET Core
service** with its own solution (`Octadock.LicenseService.sln`) and is **not**
part of the Windows desktop `Octadock.sln`.

Build/test:

```
dotnet build services/license-service/Octadock.LicenseService.sln -c Release
dotnet test  services/license-service/Octadock.LicenseService.sln -c Release
```

## Endpoints

| Route | Purpose |
| --- | --- |
| `POST /webhooks/stripe` | Verified webhook money-path: create a license record on paid Checkout; revoke it on refund/dispute. |
| `POST /activate` | Key + machine hash → signed, device-bound entitlement (enforces the 3-device limit). |
| `GET /trust-anchor` | The signing key's public half; clients embed it in their trust ring. |
| `GET /health` | Public minimal liveness response: `{ "status": "ok" }`. |
| `GET /admin/health` | Authenticated, non-cacheable founder launch-health page (HTML, below). |

Webhook responses deliberately contain only outcome/message, never the issued
license key. Email/success-page/resend delivery is not built yet and remains a
paid-beta release blocker.

## Launch-health surface (WS6)

`GET /admin/health` is a self-contained HTML page with no external assets. After
authentication it reports:

- **Licenses** — total, active, revoked, issued last 24h, and issued-not-activated
  (active licenses with zero live device activations).
- **Webhook events** — total and the age of the most recent received event.
- **Activation success rate** — `device.activated` vs `device.activation_failed`
  audit rows over the last N attempts. Failures are a **live** number: the
  `Activate(...)` failure branches (`LicenseNotFound` / `LicenseNotActive` /
  `DeviceLimitReached`) each write a `device.activation_failed` audit row (no schema
  change), mirroring the `device.activated` success audit.
- **Reconciliation** — the paid-but-no-key diff from the background reconciliation
  pass, plus whether the paid-session source is configured.
- **Email delivered / bounced / resend count** — **`null` / "no data by design"**.
  Email delivery + the resend endpoint are **founder-gated** (not built); these are
  never fabricated.

Public `GET /health` is liveness-only and exposes none of these metrics.

### `/admin/health` auth (`LicenseService:AdminToken`)

Production must sit behind a **network gate — Cloudflare Access + WebAuthn**
(founder-gated). A configured `LicenseService:AdminToken` is also required and
must be presented only in the `X-Admin-Token` header. Missing/wrong headers and
query-string-only tokens return `403`; absent server configuration returns `503`.
All admin responses expose no metrics on failure and set `Cache-Control: no-store`.

## Founder-paged alerts (WS6)

`AlertEvaluator` decides which of four alerts fire from current health/reconciliation
state and dispatches them to an `IAlertSink`. The default `LoggingAlertSink` logs at
Error (critical) / Warning. **Real email + phone/SMS paging is founder-gated** — the
sink is the seam a production pager plugs into. The evaluator is invoked each cycle by
`ReconciliationService.RunOnceAsync` (after computing the diff) and is non-fatal — it
never throws out of the background loop. The four alerts:

- **(a) Webhook staleness** — the most recent received webhook event is older than
  60 min (only when at least one event exists).
- **(b) Reconciliation diff > 0** — paid Checkout sessions with no license (critical).
- **(c) Activation success rate < 90%** over ≥ 20 attempts.
- **(d) Email bounce/spam spike** — **founder-gated**: the email-status source is not
  built, so the branch is skipped by design and only fires once such a source is wired
  (`AlertState.EmailBounceRate` non-null).

## Live Stripe reconciliation source

`StripePaidSessionSource : IPaidSessionSource` lists PAID Checkout sessions from the
Stripe REST API since a timestamp, filtered to the configured launch price (by
expanded line-item price id / lookup key, falling back to amount + currency), and
paginates via `has_more` / `starting_after`. It uses an injectable
`HttpMessageHandler` so tests can supply canned Stripe JSON.

- Config: `LicenseService:StripeApiKey` (a **restricted, read-only** key) and
  `LicenseService:StripeApiBaseUrl`. The key is **empty in `appsettings.json` on
  purpose** — the real restricted key is **founder-gated (external blocker)** and is
  injected from env / a secret store.
- `Program.cs` selects `StripePaidSessionSource` **only when a key is configured**,
  else keeps `NullPaidSessionSource`. `IsConfigured` reflects reality, so the admin
  tile never claims a clean diff on a source that never ran.

## Entitlement signing key (dev vs KMS)

- **Development:** `appsettings.Development.json` carries a **DEV-ONLY** Ed25519 key
  (`KeyId=dev1`). Its public half is embedded in the desktop client's trust ring, so a
  locally-run service issues entitlements the client accepts. `appsettings*.json` is
  copied to build output by the Web SDK — no csproj change needed.
- **Production:** the private key stays **EMPTY** in `appsettings.json`. Production
  **must** set `EntitlementSigning:PrivateKeyBase64` from **KMS (founder-gated)**, and
  clients must add the **production public key** to their trust ring. With no key
  configured the service generates an ephemeral dev key and logs a loud warning.

## Founder-gated items (external blockers)

- Live restricted **Stripe API key** for reconciliation.
- **Email delivery + resend** pipeline (health email metrics, alert (d)).
- **Phone/SMS paging** delivery behind `IAlertSink`.
- **KMS-backed production signing key** + production public key in client trust rings.
- **Cloudflare Access + WebAuthn** network gate in front of `/admin/health`.

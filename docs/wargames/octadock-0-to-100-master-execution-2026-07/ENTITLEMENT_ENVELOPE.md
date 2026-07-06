# Entitlement Envelope + Machine Hash — FROZEN SPEC (WS4/WS13, R13/R15)

Status: **FROZEN 2026-07-06.** This format is versioned and must not change in a
breaking way once the first real key issues — shipped client verifiers are
immortal (R15). Reference implementation + tests:
`services/license-service/src/Octadock.LicenseService/Licensing/` and its tests.

## Why freeze now

Beta clients ship a verifier that will still be running years later. If a future
release needs a richer entitlement, those old verifiers must still accept it.
Therefore: **the signature is over the raw payload bytes, and both the envelope
and the payload are unknown-field tolerant.** Old verifiers accept new fields and
new schema numbers as long as the signature checks out.

## Envelope (outer wrapper)

```json
{
  "schema": 1,
  "key_id": "k1",
  "payload": "<base64url of the raw payload bytes>",
  "sig": "<base64url Ed25519 signature over the raw payload bytes>"
}
```

| Field | Type | Rule |
| --- | --- | --- |
| `schema` | int | Envelope format version. Verifiers **accept unknown/future values**. Informational only. |
| `key_id` | string | Names the signing key. Verifier holds a **trust ring** (`key_id → public key`) so a leaked key retires in a normal client release. |
| `payload` | string | Base64Url (RFC 4648 §5, unpadded) of the **raw** payload bytes. |
| `sig` | string | Base64Url Ed25519 signature computed over the **exact raw payload bytes** (not a re-serialization). |

**Verification contract (immutable):**
1. Look up `key_id` in the trust ring; unknown key ⇒ reject.
2. Base64Url-decode `payload` and `sig`.
3. Ed25519-verify `sig` over the decoded payload bytes.
4. On success, hand the caller the **raw payload bytes**; the caller parses them
   tolerantly. Never re-serialize before verifying.

Unknown envelope fields are ignored; unknown `schema` values are accepted.

## Payload (signed content)

The payload is itself JSON, unknown-field tolerant. v1 known fields:

```json
{
  "schema": 1,
  "license_key": "OCTA-XXXXX-XXXXX-XXXXX-XXXXX",
  "product": "octadock-local-beta",
  "status": "active",
  "machine_hash": "<64-hex>",
  "device_hash_v": 1,
  "seats": 1,
  "updates_until": "2027-07-06T00:00:00Z",
  "issued_at": "2026-07-06T00:00:00Z"
}
```

Future releases MAY add fields (e.g. Pro/Team, org scope). Clients ignore fields
they do not understand. Removing/renaming a v1 field is a breaking change and
requires a new envelope `schema` with additive verification — never edit v1.

## Signature algorithm

- **Ed25519** (RFC 8032). Private key is **sign-only, non-exportable, in KMS**,
  physically separate from the webhook host (WS4). The reference signer
  (`EntitlementSigner`) exists for dev/test/tooling only.
- Signature input = the raw payload bytes, nothing else (no canonicalization step,
  because the transmitted bytes are the authority).

## Machine hash — the "3 devices" identity (R13)

- `device_hash_v = 1` (versioned so the algorithm can evolve without invalidating slots).
- **v1 algorithm:** `machine_hash = lowercasehex( SHA-256( utf8( lowercase(trim(MachineGuid)) ) ) )`.
- **Source of `MachineGuid`:** Windows `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`.
  Reinstalling Windows can change it; slot migration + LRU self-eviction (WS4)
  keep the slot count stable so a reinstall does not cost a device.
- Reference: `MachineHash.Compute` in the license service; the desktop client MUST
  reproduce this byte-for-byte.

## Trust ring / rotation

- Clients ship a map `{ key_id → Ed25519 public key }` with one or more keys.
- To rotate: add the new public key to the client trust ring in a release, start
  signing with the new `key_id`, then retire the old key in a later release once
  no live entitlements reference it.

## Frozen-ness test (CI)

`EntitlementEnvelopeTests.Future_schema_entitlement_with_unknown_fields_is_accepted_by_todays_verifier`
signs a payload with `schema: 999` and unknown fields inside a `schema: 42`
envelope and asserts **today's verifier accepts it** — the executable guarantee
behind R15. If that test ever needs to change to keep passing, the format is no
longer backward-compatible: stop and design a new additive schema instead.

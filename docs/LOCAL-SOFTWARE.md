# Local software contract — 13 September 2026

This document supersedes the previous paid-product, trial, activation, update-service and cloud-provider plans.

1. Every shipped desktop feature is free, with no account, activation, subscription or device-count gate.
2. Captures, OCR, dictation, read-aloud, history, settings and Context/export processing run on this PC.
3. Speech models are explicitly imported from local disk. No implicit network fetch occurs during dictation or startup.
4. The app has no HTTP client, telemetry, automatic update check, remote speech provider, or external AI runner. Developer restore/build tooling and the static website are separate from desktop runtime behavior.
5. Local export means writing a reviewed packet and attachments to a user-selected folder. Nothing is sent to an AI service.
6. Existing history and annotations remain compatible. Old activation URLs return a clear removed-feature response. Old entitlement files and unknown settings keys are ignored, not destructively erased.
7. Window positions use physical desktop coordinates. Store dock anchors separately for each display, normalized to its work area. Display origins must not be divided by another display's DPI.
8. Utility windows must shrink to the available work area and keep content reachable. Motion respects the existing accessibility settings.
9. MIT remains the project license. Public release artifacts exclude internal research collectors and user data.

Do not change repository visibility, publish releases, replace hosted websites, or claim hardware acceptance merely because a build passed. Record those actions and evidence separately.

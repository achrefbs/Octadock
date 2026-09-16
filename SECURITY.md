# Security

## Reporting a vulnerability

Email [support@octadock.com](mailto:support@octadock.com) with the subject **Octadock security report**. Include the affected version or commit, reproduction steps, expected impact and a minimal example using synthetic data. Please keep exploit details out of public issues until we have coordinated a fix.

Do not send passwords, API keys, personal captures, full capture databases or unredacted logs. If private evidence is needed, describe it first so we can agree on how to share it.

## Supported code

Security fixes target the latest `main` and current 0.3 preview. Historical branches and old paid-beta builds are unsupported. Releases are currently unsigned Windows x64 previews; compare downloads with the SHA-256 values on the official download page.

The current desktop application has no licensing service, telemetry, cloud provider or automatic updater. Local processing does not protect files from other programs running as the same Windows user. The optional website mailing list is separate from the app.

## Historical development credentials

The retired license-service history contains a deliberately committed `dev1` Ed25519 signing key in `appsettings.Development.json`. Its original documentation identifies it as a development-only key; production configuration left the private key empty and required a separately provisioned key. Treat the historical key as public test material and never use or trust it in a deployed service. The current app has removed both that service and entitlement verification.

Secret-redaction tests also contain intentionally fake key-shaped strings. Real credentials must never be added to the repository, including examples, screenshots or Git history.

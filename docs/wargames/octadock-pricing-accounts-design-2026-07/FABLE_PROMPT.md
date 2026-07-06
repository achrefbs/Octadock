# Prompt to Paste Into Fable

You are running a second deep wargame for Octadock.

Use the repository I provide as your source packet. Read this folder first, then
read every source file referenced by `PROJECT_INPUTS.md`.

Your task is to pressure-test and solidify:

1. Pricing and packaging.
2. Account requirements.
3. Feature gates and entitlements.
4. Hosted usage metering.
5. Payment/provider choice.
6. Homepage/product messaging.
7. A precise app + website design system.

Do not redo the first feature wargame unless it affects pricing, accounts,
gates, or design. Build on the prior result in
`docs/wargames/octadock-ui-files-context-2026-07/WARGAME_RESULT.md`.

Important constraints:

- Octadock is Windows-first and local-first.
- Local capture, shelf, annotation, pins, local history, local OCR, local
  speech/dictation where available, read aloud, file preview, and local tools
  should feel usable without cloud dependence.
- Hosted AI, cloud transcription, premium voices, future hosted sharing, and
  team/admin features need metering, entitlements, and probably accounts.
- BYO-key is important for power users.
- Do not sell unlimited hosted AI inside a one-time license.
- Do not resurrect AI discovery or AI Sessions.
- The visual system must cover the desktop app and homepage, with mapped token
  names for WPF and web CSS variables.
- The visual direction is dark obsidian glass with teal/cyan accents, but it
  must not become a one-note dark-blue/teal product. Add enough neutral,
  semantic, and secondary accent structure to make the app readable at scale.

Before returning the final result:

- Verify public pricing using official pricing pages listed in
  `LIVE_PRICING_SOURCES.md`.
- Mark any pricing value as "verified", "ambiguous", or "needs owner decision".
- If browsing is not available, explicitly say the pricing was not verified and
  treat the repo research as untrusted.

Run the wargame with these roles:

- Founder/Product Owner
- Indie Desktop Software Buyer
- Windows Power User
- Privacy-First User
- Pricing Strategist
- Payments/Tax Operator
- Entitlement Engineer
- Account/Auth Engineer
- Support Lead
- Brand Designer
- Product Designer
- Accessibility Reviewer
- Marketing Copy Reviewer

Return the result using `OUTPUT_TEMPLATE.md`.

The highest-value output is a final decision matrix:

- Which features require no account.
- Which features require a local license.
- Which features require Pro.
- Which features consume cloud credits.
- Which features allow BYO-key.
- Which features should be unavailable until the user explicitly opts into cloud
  usage.
- Which colors, type, spacing, radius, elevation, icon, and component rules all
  app surfaces must use.


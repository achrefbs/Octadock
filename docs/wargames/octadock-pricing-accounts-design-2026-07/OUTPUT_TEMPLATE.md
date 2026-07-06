# Wargame Output Template

## Executive Summary

- Overall verdict:
- Recommended launch model:
- Biggest pricing risk:
- Biggest account/gating risk:
- Biggest design-system risk:
- P0 decisions required before implementation:

## Verified Pricing Snapshot

| Item | Source | Verified Price/Claim | Status | Notes |
| --- | --- | --- | --- | --- |
| OpenAI transcription | Official URL |  | Verified/Ambiguous/Stale |  |
| ElevenLabs API/subscription | Official URL |  | Verified/Ambiguous/Stale |  |
| Lemon Squeezy | Official URL |  | Verified/Ambiguous/Stale |  |
| Paddle | Official URL |  | Verified/Ambiguous/Stale |  |
| Stripe | Official URL |  | Verified/Ambiguous/Stale |  |
| CleanShot X | Official URL |  | Verified/Ambiguous/Stale |  |
| Snagit | Official URL |  | Verified/Ambiguous/Stale |  |
| ShareX | Official URL |  | Verified/Ambiguous/Stale |  |

## Recommended Packaging

### Free / Trial / Beta

- Recommendation:
- Included:
- Limits:
- Account required:

### Local License

- Recommended price:
- Included:
- Update entitlement:
- Device policy:
- Account required:
- Offline behavior:

### Pro Subscription

- Recommended price:
- Included:
- Monthly quotas:
- Overage/top-ups:
- BYO-key behavior:
- Cancellation behavior:

### Team Later

- Later features:
- Entitlement architecture to preserve:

## Account And Payment Feature Spec

### Required User Flows

For each flow, include happy path, error states, UI surfaces, data stored, and
acceptance criteria.

| Flow | Happy Path | Error/Edge States | UI Surfaces | Data Stored | Acceptance Criteria |
| --- | --- | --- | --- | --- | --- |
| First install/no account |  |  |  |  |  |
| Start trial |  |  |  |  |  |
| Buy Local license |  |  |  |  |  |
| Activate second device |  |  |  |  |  |
| Offline usage |  |  |  |  |  |
| Upgrade to Pro |  |  |  |  |  |
| Cancel Pro |  |  |  |  |  |
| Payment failed/past due |  |  |  |  |  |
| Credits exhausted |  |  |  |  |  |
| BYO-key setup |  |  |  |  |  |
| Refund/revocation |  |  |  |  |  |
| Account recovery |  |  |  |  |  |

### Entitlement States

| State | User Meaning | Allowed Features | Blocked Features | UI Treatment | Backend/Local Requirement |
| --- | --- | --- | --- | --- | --- |
| No account |  |  |  |  |  |
| Trial active |  |  |  |  |  |
| Trial expired |  |  |  |  |  |
| Local license active |  |  |  |  |  |
| Update entitlement expired |  |  |  |  |  |
| Pro active |  |  |  |  |  |
| Pro past due |  |  |  |  |  |
| Pro canceled |  |  |  |  |  |
| Offline grace |  |  |  |  |  |
| Device limit reached |  |  |  |  |  |
| BYO-key configured |  |  |  |  |  |
| Hosted credits exhausted |  |  |  |  |  |
| License revoked/refunded |  |  |  |  |  |

### Account/Billing UI Inventory

List every new account/payment screen or state the app needs.

| Surface | Purpose | Entry Point | Required Controls | Empty/Error States | Copy Notes |
| --- | --- | --- | --- | --- | --- |
| First-run activation |  |  |  |  |  |
| Settings > Account & Billing |  |  |  |  |  |
| Settings > Cloud Providers |  |  |  |  |  |
| Usage meter |  |  |  |  |  |
| Upgrade/gate banner |  |  |  |  |  |
| Cloud-send confirmation |  |  |  |  |  |
| License renewal state |  |  |  |  |  |
| Offline state |  |  |  |  |  |

## Account Requirement Matrix

| Workflow | No Account | Local License | Pro Account | Notes |
| --- | --- | --- | --- | --- |
| Install/open app |  |  |  |  |
| Capture/shelf/pin/annotate |  |  |  |  |
| Local OCR |  |  |  |  |
| Local STT/read aloud |  |  |  |  |
| File preview |  |  |  |  |
| Context package export |  |  |  |  |
| BYO-key cloud use |  |  |  |  |
| Hosted transcription |  |  |  |  |
| Premium voices |  |  |  |  |
| Future team/admin |  |  |  |  |

## Feature Gate Matrix

| Feature | Free/Beta | Local License | Pro | BYO-Key | Team Later | Not V1 | Rationale |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Capture modes |  |  |  |  |  |  |  |
| Capture Shelf |  |  |  |  |  |  |  |
| Annotation |  |  |  |  |  |  |  |
| Pins |  |  |  |  |  |  |  |
| History |  |  |  |  |  |  |  |
| Local OCR |  |  |  |  |  |  |  |
| Local dictation |  |  |  |  |  |  |  |
| OpenAI/hosted STT |  |  |  |  |  |  |  |
| Premium TTS |  |  |  |  |  |  |  |
| File preview |  |  |  |  |  |  |  |
| PDF preview |  |  |  |  |  |  |  |
| Office preview |  |  |  |  |  |  |  |
| Context package export |  |  |  |  |  |  |  |
| Future AI context |  |  |  |  |  |  |  |

## Hosted Usage and Metering

| Meter | Unit | Applies To | Included Quota | Warning Threshold | Hard Stop | BYO-Key Behavior |
| --- | --- | --- | --- | --- | --- | --- |
| Cloud transcription | minutes |  |  |  |  |  |
| High-accuracy transcription | minutes |  |  |  |  |  |
| AI summarize/explain | tokens/credits |  |  |  |  |  |
| Premium TTS | characters |  |  |  |  |  |
| Future hosted storage | GB/month |  |  |  |  |  |

## Payment Provider Recommendation

- Recommended v1 provider:
- Why:
- What it handles:
- What Octadock still must build:
- When to revisit:
- Risks:

## Entitlement Architecture

- License states:
- Subscription states:
- Offline grace:
- Device activation:
- Account recovery:
- Local secure storage:
- Server-side requirements:
- Events to log:
- Failure modes:

## Design System Verdict

### Brand Principles

- 

### Visual Direction

- Name:
- One-sentence description:
- What it keeps from current obsidian glass:
- What it changes:
- What it explicitly forbids:

### Primitive Palette

Provide exact primitive values before semantic tokens.

| Primitive | Hex | Role | Notes |
| --- | --- | --- | --- |
| Obsidian 950 |  |  |  |
| Obsidian 900 |  |  |  |
| Obsidian 800 |  |  |  |
| Slate/Fog neutral |  |  |  |
| Teal primary |  |  |  |
| Cyan secondary |  |  |  |
| Secondary accent |  |  |  |
| Success |  |  |  |
| Warning |  |  |  |
| Danger |  |  |  |
| Info |  |  |  |

### Semantic Color Tokens

Provide exact hex values and token names for WPF and web:

| Purpose | WPF Token | CSS Variable | Dark Hex | Light Hex | Usage |
| --- | --- | --- | --- | --- | --- |
| App background |  |  |  |  |  |
| Surface |  |  |  |  |  |
| Surface raised |  |  |  |  |  |
| Border |  |  |  |  |  |
| Text primary |  |  |  |  |  |
| Text secondary |  |  |  |  |  |
| Accent primary |  |  |  |  |  |
| Accent secondary |  |  |  |  |  |
| Success |  |  |  |  |  |
| Warning |  |  |  |  |  |
| Danger |  |  |  |  |  |
| Info |  |  |  |  |  |
| Focus ring |  |  |  |  |  |
| Gated/locked |  |  |  |  |  |
| Cloud/Pro |  |  |  |  |  |
| BYO-key |  |  |  |  |  |
| Quota warning |  |  |  |  |  |

### Contrast Requirements

| Pair | Minimum Ratio | Proposed Ratio | Pass/Fail | Notes |
| --- | --- | --- | --- | --- |
| Primary text on app background |  |  |  |  |
| Metadata text on raised surface |  |  |  |  |
| Accent text on accent fill |  |  |  |  |
| Warning text on warning surface |  |  |  |  |
| Danger text on danger surface |  |  |  |  |
| Disabled text on surface |  |  |  |  |
| Focus ring on surface |  |  |  |  |

### Typography

| Role | Font | Size | Weight | Line Height | Usage |
| --- | --- | --- | --- | --- | --- |

### Spacing, Radius, Elevation, Motion

- Spacing scale:
- Radius scale:
- Border widths:
- Elevation/shadow rules:
- Motion durations/easing:
- Reduced motion behavior:
- Reduced transparency behavior:

### Iconography

- Recommended icon family:
- WPF implementation approach:
- Web implementation approach:
- Existing glyphs to replace:
- Icon sizes:
- Stroke/fill rules:
- Tooltip/screen-reader rules:

### Components

| Component | Variants | States | Accessibility | Notes |
| --- | --- | --- | --- | --- |
| Icon button |  |  |  |  |
| Action rail |  |  |  |  |
| Shelf row |  |  |  |  |
| Context row |  |  |  |  |
| Inspector panel |  |  |  |  |
| File preview tab |  |  |  |  |
| Toast/banner |  |  |  |  |
| Pricing card |  |  |  |  |
| Account status badge |  |  |  |  |
| Usage meter |  |  |  |  |
| Pro gate banner |  |  |  |  |
| Cloud-send confirmation |  |  |  |  |

### Surface-by-Surface Application

For each surface, specify layout, components, tokens, density, and states.

| Surface | Layout Rules | Components | Token Notes | States To Support | Deprecated Patterns |
| --- | --- | --- | --- | --- | --- |
| Dock |  |  |  |  |  |
| Capture Shelf |  |  |  |  |  |
| Context |  |  |  |  |  |
| File Preview |  |  |  |  |  |
| History/Library |  |  |  |  |  |
| Pin Viewer |  |  |  |  |  |
| Annotation Editor |  |  |  |  |  |
| Settings |  |  |  |  |  |
| Account/Billing |  |  |  |  |  |
| Capture HUD |  |  |  |  |  |
| Homepage |  |  |  |  |  |

### Token Migration Plan

| Existing Token/Pattern | Keep/Replace | New Token/Pattern | Migration Notes |
| --- | --- | --- | --- |
| `Octadock.Brush.Surface` |  |  |  |
| `Octadock.Brush.SurfaceAlt` |  |  |  |
| `Octadock.Brush.Accent` |  |  |  |
| `Octadock.Corner.Large` |  |  |  |
| hardcoded preview colors |  |  |  |
| Segoe MDL2 glyph icons |  |  |  |

## Homepage Corrections

- Hero:
- Octopus asset:
- Pricing copy:
- Privacy copy:
- Context copy:
- CTAs:
- Claims to avoid:

## Risk Register

| Risk | Area | Severity | Likelihood | Mitigation | Verification |
| --- | --- | --- | --- | --- | --- |

## Revised Backlog

### P0 Before Paid Beta

- 

### P1 Before Public Launch

- 

### P2 Later

- 

## Open Questions

| Question | Owner | Blocking? | Suggested Default |
| --- | --- | --- | --- |

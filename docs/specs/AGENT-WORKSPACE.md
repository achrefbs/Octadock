# Agent Workspace Architecture

Status: implemented and hardened end to end, 2026-07-10.

## Decision

Octadock does not compete with Codex or Claude by presenting generic Explain,
Summarize, or Rewrite buttons. Its job is to carry local evidence into a concrete
job that a capable agent can execute or assess.

The supported loop is:

`capture / pin / Context / History / Clipboard -> choose outcome -> add missing intent -> review -> finish`

The historical `ai` command, aliases, license feature, presenter method, and
text-action services remain intact as compatibility adapters. All visible entry
points route to the dedicated Use with AI workspace.

## Usability contract

AI starts where the evidence already exists; users do not rebuild context in a
separate console.

- A Shelf screenshot or pin carries that exact image, including non-destructive
  quick-pen annotations.
- Context carries only the items currently included in the selected package.
- History and Clipboard carry the selected row rather than an unrelated recent
  item.
- Voice dictates the missing intent directly into the workspace.
- The Dock opens an empty workspace for voice-first or file-first work.

The first decision is an outcome, not a model verb:

- **Build from this** creates an implementation-ready task.
- **Investigate an issue** separates observations, hypotheses, cause, and fix.
- **Verify a result** evaluates baseline/result evidence and returns a verdict.
- **Extract usable data** preserves exact source values in reusable structure.
- **Prepare a handoff** preserves context, decisions, unknowns, and next action.

Each outcome seeds a useful goal and definition of done. The user edits only
what the evidence cannot supply. Provider selection, exact packet contents,
privacy disclosure, and destination confirmation appear at the review boundary;
they are safety controls, not the primary workflow.

## Packet contract

`IAgentPacketBuilder` accepts trusted task metadata, one or more untrusted
sources, and one or more required acceptance criteria. It returns an immutable
`ReviewedAgentPacket` containing:

- deterministic `TASK.md`, `manifest.json`, and exported `SHA256SUMS` payloads;
- normalized provenance without absolute machine paths;
- safe bundle-relative asset paths, media metadata, dimensions, and SHA-256;
- per-source and aggregate text-secret counts;
- prompt-injection-resistant source framing;
- an exact outbound and manifest digest.

The reviewed object is the only payload eligible for handoff. The unredacted
source text is not retained in a redacted review.

## Sources

The first slice resolves sources directly from app services, never by coupling
feature view models:

- recent non-recording `CaptureRecord` entries with process/window/time/dimensions
  provenance;
- current clipboard text and/or image;
- files dragged or selected from local storage;
- each item in a selected Context package, including fresh size/hash validation
  for reference-owned items;
- local OCR attached to a selected raster;
- untouched or quick-pen-composed pin images;
- baseline, candidate, and deterministic heat-map images from visual comparison.

UNC inputs, mapped network drives, and reparse-point paths are rejected before
content is read. File, text, image, collection, character, dimension,
pixel-count, and 256 MB aggregate attachment limits are enforced.

## Privacy boundary

Text secret detection and redaction are deterministic and local. Redacting OCR
or other text does not alter the pixels in an attached screenshot. Agent
Workspace must therefore display an unchanged-pixels warning whenever a raster
is included. Pixel redaction is not claimed or implied.

User-saved bundles are explicit. Handoff-only bundles live under managed
temporary export storage and are removed after success, cancellation, refusal,
or failure. A link-skipping recursive retention pass scavenges crash leftovers;
composed pin images transfer through expiring in-memory leases. CLI stdout is
ephemeral until copied; prompts, results, stderr, sessions, and API keys are not
logged by Octadock.

## Destination profiles

There is no provider fallback.

- Codex: ephemeral execution, approval never, read-only sandbox, ignored user
  configuration/rules, and disabled shell/unified-exec/shell-snapshot tools.
  The reviewed text is sent through stdin and reviewed images are attached
  explicitly, so Codex has no general filesystem-read tool in this profile.
- Claude: print mode, safe mode, no session persistence, `dontAsk`, and only
  packet-relative `Read(./**)` / `Glob(./**)` permissions. No edit or shell tool
  is enabled.

A fresh destination-named confirmation discloses reviewed text size, attachment
count/bytes, destination, read-only profile, and unchanged image pixels.

Write-enabled execution is not part of this profile. If introduced later, it
requires a separate capability model, selected workspace, destination-named
confirmation, and tests proving the analyze-only path cannot escalate.

## Verification

`IVisualComparisonService` compares two bounded local raster files at native
size on a top-left-aligned common canvas. It reports exact match, changed pixels
and ratio, mean/max RGBA delta, and tight change bounds, and emits a deterministic
PNG heat map. Inputs are never modified.

Automatic same-region recapture is deferred because `CaptureRecord` does not
currently persist selected-region coordinates. Adding that feature must use a
new nonbreaking capture-return/target seam rather than changing existing
`ICaptureCoordinator` signatures.

## Compatibility

- `CommandType.AiActions` and wire token `ai` remain stable.
- `ask-ai`, `ai-actions`, `explain`, and `summarize` remain valid; `agent`,
  `agent-workspace`, and `handoff` are additive aliases.
- Legacy `--action` values become editable goal templates.
- New automation can pass `--workflow build|investigate|verify|extract|handoff`,
  `--goal`, `--criteria` (pipe-separated),
  `--captureid`, `--contextid`, `--project`, `--target`, and `--environment`.
- `IWindowPresenter.ShowAiActions` remains the stable seam and opens
  `AgentWorkspaceWindow`.

## Deferred extensions

- true pixel/region redaction;
- automatic same-target recapture and threshold policies;
- Windows UI Automation maps linked to numbered screenshot elements;
- local semantic history search;
- MCP packet/resource exposure;
- local-model destinations and reusable project/task profiles;
- handle-bound final-path/file-identity verification if Octadock ever runs
  elevated or accepts paths from lower-integrity callers; the current guard is
  designed for the same-user, non-elevated desktop threat model;
- explicitly permissioned write-enabled agent execution.

None of these extensions may introduce passive screen watching, hidden sends,
provider fallback, or undisclosed retention.

# Workflow Intelligence internal harness

This project is intentionally excluded from `Octadock.sln` and the public
application. It reads provider corpora without changing them, validates adapter
shapes, creates metadata-only sample manifests, and accepts explicitly
configured hook payloads into a separate encrypted raw-content store.

It does **not** install hooks, start watchers, modify Octadock settings, or add a
public feature.

Build and test directly:

```powershell
dotnet build tools/internal/Octadock.WorkflowIntelligence.Internal/Octadock.WorkflowIntelligence.Internal.csproj
dotnet test tests/internal/Octadock.WorkflowIntelligence.Internal.Tests/Octadock.WorkflowIntelligence.Internal.Tests.csproj
```

Read-only checks:

```powershell
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- inventory
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- self-test
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- sample --count 50
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- tail --provider claude --file PATH
```

`tail` stores only a hashed path, file identity metadata, and the last complete
JSONL byte offset. It never writes transcript lines to the cursor file, never
advances past an incomplete line, and resets safely after truncation or file
replacement. If one JSONL record exceeds the configured memory cap, the command
fails visibly without moving its cursor; rerun with a larger `--max-bytes` or
route that provider shape to a bounded oversized-record adapter.

Full-consent reconstruction and Trusted Brief evaluation:

```powershell
$env:OCTADOCK_INTERNAL_FULL_CONSENT='I_UNDERSTAND_RAW_CONTENT_IS_CAPTURED'
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- reconstruct --provider claude --file PATH
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- brief --provider claude --file PATH --show
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- evaluate-briefs --provider all --max-files 25
Get-Content stop-hook-payload.json -Raw | dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- process-hook --provider claude
```

The default brief ranker is deterministic. For materially better prioritization,
an installed Claude or Codex CLI can arrange immutable evidence IDs; the final
brief still resolves those IDs to exact source-linked ledger text:

```powershell
$env:OCTADOCK_INTERNAL_MODEL_EGRESS='I_UNDERSTAND_EVIDENCE_IS_SENT_TO_SELECTED_CLI'
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- brief --provider codex --file PATH --ranker claude --show
dotnet run --project tools/internal/Octadock.WorkflowIntelligence.Internal -- brief --provider claude --file PATH --ranker codex --show
```

The ranker receives at most 180 bounded evidence candidates, cannot introduce
displayed prose, cannot select unknown IDs, cannot repeat an ID, and has no
silent fallback. High-importance evidence omitted by a layout is restored by the
deterministic coverage backstop before independent verification.

`brief` prints only metrics unless `--show` is explicitly supplied. `--store`
writes the derived brief and verifier result into the same DPAPI/AES-GCM
encrypted, retention-bounded internal store as hook content. The deterministic
profile is an evidence-only safety baseline; its evaluation metrics measure
ledger-to-brief coverage, not yet founder-labeled real-world critical recall.
`process-hook` is the headless end-to-end completion path. It validates the
hook/session boundary, reconstructs the complete turn, suppresses low-value
content, verifies the brief and source anchors, and stores one deterministic
encrypted artifact. Repeated Stop events return the same artifact instead of
creating duplicates. This command does not install hooks by itself.

Measured baseline and remaining quality gate:
`docs/strategy/TRUSTED-BRIEF-INTERNAL-EVALUATION-2026-07-10.md`.

Raw-content commands require the full-consent environment value printed by
`--help`. Keep generated manifests and stores local.

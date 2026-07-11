# Signal Lens — trustworthy compression inside the app you are already using

Status: product direction for the internal founder build. No public background
collection is approved by this document.

## The actual problem

The problem is not “make a summary.” The user is forced to read a long response
because a normal summary provides no proof that it kept the one warning,
constraint, correction, filename, number, or unfinished task that matters.

Signal Lens must reduce reading time **without asking the user to trust a shorter
paragraph blindly**.

## Product decision

There is no summary screen, AI workspace, inbox, or new workflow.

When a supported app finishes rendering a long response, Octadock places one
quiet signal on the edge of that response. It visually belongs to the source app.
It is absent for short or low-value responses.

The collapsed signal says only what is useful at a glance, for example:

`3 outcomes · 1 warning · 2 things still open`

Hovering reveals the first three source-linked essentials. Clicking expands a
thin reading layer over the source response—not a new Octadock window—with:

1. **Outcome** — what actually happened.
2. **Do / decide** — what needs the user's attention.
3. **Do not miss** — warnings, negations, conditions, exact numbers, filenames,
   failures, and work that was proposed but not completed.
4. **Coverage** — how many critical source items were represented and whether
   the response boundary was complete.

Every displayed item can reveal or jump to its exact source. “7 details folded”
is shown rather than silently omitted. When critical coverage cannot be proven,
Octadock withholds the compressed view and says why.

## Acquiring the complete text

Signal Lens never treats a screenshot as the primary source for long text.

1. **Provider-native adapter** — Codex and Claude completion hooks/session JSONL
   provide exact start/end boundaries, text, model, project directory, and stable
   source anchors. This is the first internal slice.
2. **Accessible-document adapter** — for other apps, UI Automation TextPattern
   reads the complete logical text container, including off-screen text when the
   app exposes it. A container identity and mutation quiet-period establish the
   boundary.
3. **Explicit selection/clipboard** — a safe fallback when the app exposes only
   the user's selected/copied text.
4. **OCR** — last resort only. It is labeled partial and cannot receive a trusted
   coverage badge unless all pages/scroll segments and overlap checks pass.

If the adapter cannot prove where the response begins and ends, it may offer
“read visible text” but must not claim complete compression.

## Trust pipeline

1. Reconstruct one complete response and exclude private reasoning blocks.
2. Split source text into immutable, hash-anchored evidence units.
3. Classify evidence: outcome, completed work, proposal, action, decision,
   warning, failure, assumption, unresolved question, constraint, number, file,
   and correction.
4. Let the selected model rank/group **evidence IDs only**. It cannot author new
   claims.
5. Resolve displayed language from the anchored evidence.
6. Deterministically verify critical recall, exact numbers/files/negations,
   source hashes, and completed-versus-proposed wording.
7. Show the Lens only after verification. Otherwise withhold it.

The existing internal reconstruction/ledger/verifier code is useful engineering
infrastructure. Its old “brief as a destination” presentation is not the product.

## Model strategy

Model selection is measured, not guessed. The internal bake-off uses the same
immutable ledger with deterministic, Claude CLI, Codex CLI, and one local model.
There is no API-key requirement and no hidden provider fallback. The winning
profile may vary by content class, but the verifier is identical for all models.

## Natural behavior

- No setup beyond the already-approved consent level and provider adapter.
- No prompt and no “Summarize” button for supported apps.
- No interruption while the response is streaming.
- The signal appears only after completion and only when expected reading time
  is meaningfully reduced.
- Escape or moving away dismisses the layer; source content never changes.
- A long response can still be read aloud, but the default audio mode speaks the
  verified essentials first and asks before continuing through folded details.

## Internal acceptance gate

Before any automatic overlay ships, label 20 stress responses and a held-out set.
Required results:

- 100% human-labeled critical-item recall;
- zero unsupported high-impact claims;
- exact preservation of numbers, filenames, negations, failures, and uncertainty;
- correct completed-versus-proposed classification;
- complete boundary detection for every “trusted” result;
- under three seconds after provider completion on the founder machine;
- at least 60% reduction in founder reading time without increasing missed facts.

## Build order

1. Finish the labeled corpus and model bake-off using existing provider logs.
2. Add a read-only completion adapter for Codex, then Claude, in the isolated
   internal assembly.
3. Build one anchored signal against those two supported apps.
4. Test source jumping, multi-monitor placement, app movement, scrolling, and
   disappearing/recycled message containers.
5. Only after the trust and interaction gates pass, evaluate a generic UIA
   adapter. OCR and public telemetry remain out of scope.

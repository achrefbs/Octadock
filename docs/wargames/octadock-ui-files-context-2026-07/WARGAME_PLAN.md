# Wargame Plan

## Objective

Pressure-test the implementation plan before engineering begins. The goal is to
find weak assumptions, missing guardrails, confusing UX boundaries, and hidden
technical risks.

## Roles

- Power User: wants speed, shortcuts, batch workflows, and low friction.
- Casual User: wants obvious labels, safe defaults, and low cognitive load.
- Designer: challenges hierarchy, density, responsiveness, and visual cohesion.
- Windows Shell Reviewer: challenges Explorer verbs, Open With behavior, CLI
  routing, registry cleanup, and multi-file selection.
- File Format Reviewer: challenges PDF, Office, archive, image, unknown, and
  design file assumptions.
- Security/Privacy Reviewer: challenges macro execution, untrusted files, path
  handling, clipboard leakage, and package contents.
- Data-Loss Reviewer: challenges direct writeback, backup, undo, failed saves,
  and retention cleanup.
- QA Reviewer: challenges edge cases, migration, high DPI, multi-monitor,
  corrupted files, large files, and testability.
- Maintainer: challenges architecture, coupling, dependency size, and future
  maintenance.
- Product Owner: protects v1 scope and success criteria.

## Method

For each scenario:

1. State the user's intent.
2. Simulate expected user flow.
3. Simulate planned Octadock behavior.
4. Have each role identify concerns.
5. Decide whether the plan passes, passes with changes, or fails.
6. Record concrete plan changes.
7. Score severity and confidence.

Severity:

- P0: Blocks v1 or risks data loss/security/trust.
- P1: Major usability or implementation risk.
- P2: Important polish or follow-up.
- P3: Nice-to-have.

Confidence:

- High: direct evidence from code, platform constraints, or obvious UX risk.
- Medium: plausible but needs verification.
- Low: speculative.

## Scenarios

### 1. Tiny Screenshot Capture

The user captures a short/narrow screenshot and it appears on Capture Shelf.

Stress:

- Does the new row layout avoid awkward empty cards?
- Are actions visible without dominating the row?
- Can the user copy/save/annotate/pin quickly?
- Does add-to-context feel separate from normal capture handling?

### 2. Capture to Context

The user captures three screenshots, adds two to Context Shelf, adds notes, and
exports a context package.

Stress:

- Is the boundary between Capture Shelf and Context Shelf clear?
- Does the user understand what will be included in `context.md`?
- Are thumbnails, OCR, annotations, and original files represented correctly?
- Can the package be inspected before export?

### 3. Open Unknown File

The user right-clicks an unknown binary file and chooses Open in Octadock.

Stress:

- Does fallback preview feel useful rather than broken?
- Does Octadock avoid executing or parsing unsafe content?
- Does the UI clearly offer open externally, reveal, copy path, add to context?

### 4. Add Many Files to Context

The user selects 20 mixed files in Explorer and chooses Add to Context Shelf.

Stress:

- Does the CLI/IPC flow support multi-file input?
- Does the UI handle progress, failures, duplicates, and huge files?
- Are unsupported files included as references without blocking the operation?

### 5. Annotate and Overwrite Image

The user opens a PNG, annotates it, and saves over the original.

Stress:

- Is there an automatic backup?
- Is the save atomic?
- Does the user understand original vs copy?
- Can the user recover if the output is wrong?

### 6. Annotate PDF

The user opens a PDF, highlights text, adds arrows/comments, and saves.

Stress:

- Is this true PDF annotation or flattened page rendering?
- What happens with signed, encrypted, or form-heavy PDFs?
- Does save preserve pages, metadata, selectable text, and existing annotations?
- What is the fallback when writeback is unsafe?

### 7. Open Office Document

The user opens a `.docx`, adds a comment/highlight, and saves.

Stress:

- What dependency is needed?
- Does save preserve formatting, images, comments, track changes, and protection?
- How does Octadock handle macros in `.docm`?
- Is v1 scope annotation/comment only, not full Office editing?

### 8. Open Large Zip

The user opens a multi-gigabyte zip with many files.

Stress:

- Does preview avoid loading everything?
- Can the user browse safely?
- Are extraction paths protected against traversal?
- Can Context Shelf export its own packages as zip?

### 9. Explorer Integration Toggle

The user enables all Explorer verbs, moves the app, then disables the verbs.

Stress:

- Does registration repair correctly?
- Does unregister fully clean up HKCU entries?
- Does Octadock avoid becoming the default app?
- Are settings understandable?

### 10. Upgrade From Current Build

The user upgrades from the current app to the new schema/UI.

Stress:

- Do current captures/history/projects survive?
- Are new Context Shelf tables migrated safely?
- Are removed AI Session references absent from primary UI?
- Can rollback or repair handle partial migration?

### 11. UI Density and Accessibility

The user runs Octadock on small laptop, high-DPI monitor, and ultrawide monitor.

Stress:

- Do rows, action rails, and inspector panels scale without overlap?
- Does text fit without viewport-based font scaling?
- Are keyboard navigation and screen reader labels adequate?
- Does dark glass styling retain contrast?

### 12. Privacy-Sensitive Context Package

The user adds screenshots, files, and OCR that contain secrets, then exports a
package.

Stress:

- Can the user inspect contents before export?
- Are OCR text and attachments obvious?
- Is there a redaction/exclude workflow?
- Does package metadata leak local paths unnecessarily?

## Required Wargame Outputs

Use `OUTPUT_TEMPLATE.md`.

Minimum deliverables:

- Top 10 plan changes.
- P0 blocker list.
- Risk register.
- Revised v1/v2 scope.
- File-type writeback policy.
- Explorer integration policy.
- Context package policy.
- UI changes required by the reference images.


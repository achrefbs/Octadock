# Prompt to Paste Into Fable

You are running a structured plan wargame for Octadock, a Windows desktop app for
capture, OCR, file preview, annotation, shelf workflows, and local context
packaging.

Use the folder I provide as your source packet. Read the files in this order:

1. `PROJECT_CONTEXT.md`
2. `IMPLEMENTATION_PLAN.md`
3. `WARGAME_PLAN.md`
4. `OUTPUT_TEMPLATE.md`
5. Inspect all images in `reference-images/`

Your task is to pressure-test the Octadock plan before implementation.

Do not invent a replacement product. Wargame the supplied plan. The goal is to
discover failure modes, missing requirements, confusing UX boundaries, data-loss
risks, implementation traps, and scope problems before engineering starts.

Important constraints:

- Capture Shelf and Context Shelf are separate features.
- Capture Shelf is for recently captured items.
- Context Shelf is for intentional, persistent context bundles.
- Unknown file types may open with a safe fallback preview.
- Rich previews should phase in, with PDF, Office, and archives first.
- Explorer integration should include right-click verbs.
- Octadock must not silently become the default app for all files.
- Direct editing/writeback is allowed when safe, but must protect originals.
- AI discovery and AI Sessions are out of scope.
- Design must follow the provided reference images, especially the compact shelf
  rows and dense inspector-style utility UI.

Run the wargame with these roles:

- Power User
- Casual User
- Designer
- Windows Shell Reviewer
- File Format Reviewer
- Security/Privacy Reviewer
- Data-Loss Reviewer
- QA Reviewer
- Maintainer
- Product Owner

For each scenario in `WARGAME_PLAN.md`:

1. Simulate what the user expects.
2. Simulate how the planned product behaves.
3. Let each role challenge the plan.
4. Identify where the plan succeeds, breaks, confuses, or needs guardrails.
5. Propose concrete plan changes.
6. Score severity and confidence.

Return your result using `OUTPUT_TEMPLATE.md`.

The most valuable output is a prioritized list of changes to the plan, not a
long narrative. Be direct. Identify assumptions that are dangerous, expensive, or
unclear. Separate v1 blockers from v2 improvements.


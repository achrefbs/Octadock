# Voice, Thought, and Project Resolver concept

> **Status:** Exploratory product proposal. This is not roadmap approval or implementation authorization.
>
> **Source:** Consolidated on 2026-07-17 from the 2026-07-11 desktop concept study so the repository remains the single source of truth.

## Problem and intent

Octadock's voice surface could support three peer destinations without forcing the user to choose a productivity system before speaking:

1. insert the transcript at the cursor;
2. shape the transcript into a reviewed prompt;
3. save it as a durable **Thought**.

The experience should recognize likely project identity locally, make that inference visible and correctable, and request access only at the moment a stronger boundary is crossed.

## Proposed domain: Thought

A Thought is one captured idea with provenance. It can begin as audio or text, gain tasks and evidence, connect to a project, and later participate in a reviewed AI handoff.

Proposed properties include:

- original audio and its transcription;
- editable text or content blocks;
- lightweight task state, checklist items, and an optional due date;
- linked captures or other evidence;
- provenance for every source;
- an optional primary project and related projects;
- the resolver's confidence, explanation, and correction history.

Tasks are a state of a Thought, not a separate container. Project membership is a relationship, not a notebook hierarchy. The original recording remains playable if transcription fails or the text is edited.

## Voice destination policy

Refactor dictation conceptually into:

`audio session -> transcript -> destination policy`

Insert, create-prompt, and save-Thought then become peer outcomes. Saving a Thought should persist the audio immediately; local transcription may finish in the background without risking the capture.

The destination belongs in the capture surface rather than Settings. The surface should also show the proposed project and allow one-click correction or undo.

## Project Resolver

The proposed resolver uses strong, local signals such as:

- an explicit project name in speech;
- the working directory or verified Git root;
- the active process and window identity;
- a known project registry;
- a prior user correction.

It must not infer from a disk-wide crawl or arbitrary background screen capture. The selected project appears as a visible chip with a confidence/explanation trail. Low confidence produces an unplaced Thought instead of a silent guess.

## Three consent boundaries

Do not bundle recognition, file access, and cloud sending into one permission.

1. **Identify:** show the likely project using local metadata. No project files are read.
2. **Read:** ask before loading instructions, structure, branch information, changed-file names, or source content. Source content is a narrower, separately reviewable scope.
3. **Send:** name the provider and show the exact reviewed material before external processing. Never silently fall back to another provider.

## Proposed surfaces

- **Capture pill:** speak, choose the destination, correct the project, save, or undo.
- **Shelf:** show recent Thoughts beside captures and other local work.
- **Project Lens:** an invoked lightweight view of open tasks and recent Thoughts for the active project.
- **Library:** secondary search across Thoughts, including an explicit **Unplaced** queue and completed work.
- **Context / AI handoff:** a Thought may be selected into a curated evidence bundle; it does not turn Context into a notebook system.

## Architectural fit

Audio, typed text, captures, and explicitly enabled clipboard material feed a dedicated Thought service. The Project Resolver associates the Thought. The Thought can then surface in the Shelf, Project Lens, Context, or a reviewed AI handoff.

This should reuse Octadock's speech, provenance, Context, and Agent Workspace foundations without overloading capture actions or Context packages.

## Explicit anti-scope

Do not treat this proposal as authorization for:

- disk-wide project scanning;
- background screen-content watching;
- nested notebooks or heavy tagging;
- Kanban boards, collaboration, or team planning;
- automatic AI interpretation without review;
- a general-purpose project-management suite.

## Smallest credible experiment

If promoted into the roadmap, test the concept in this order:

1. split dictation into insert, create-prompt, and save-Thought destinations;
2. save audio first and transcribe locally in the background;
3. show a project suggestion from verified roots with one-click correction;
4. add open/done state and small checklists;
5. surface recent Thoughts in the Shelf;
6. add Project Lens, search, and Unplaced retrieval;
7. reuse exact-payload review for any AI handoff.

Measure trust and retrieval rather than capture volume: project-suggestion accuracy, corrections per capture, time to a safely saved Thought, later retrieval, accepted task suggestions, and preferred capture gesture.

# AI Session Discovery v2 — Event-Driven, Real-Time

Status: implemented 2026-07-05 (first slice), living spec
Owner request: "a system that never fails to find all the active AI sessions
with their details and stays updated in real time".

## What was wrong with v1

v1 was a single 10-second polling sweep: WMI process query + Codex state-DB
read + rollout-log tails, with the overlay driving the same full sweep every
2 seconds. Consequences: up to 10 s of lag on session start, sessions that
stayed "Running" after their process died (the only exit signal was "missing
from the next sweep"), duplicate rows from read races, and heavy constant
scanning.

## The v2 architecture

Detection is now **event-driven with a polling safety net**. Four independent
signal sources all converge on the same store, and every store write pushes to
every UI instantly:

```
 WMI process-creation events ──┐
 ~/.codex file watcher ────────┤   debounced      ┌──────────────┐
 ~/.claude/projects watcher ───┼──> targeted ────>│ session store │──┐
 10s reconciliation sweep ─────┘      scan        └──────────────┘  │
                                                        ▲           │ IAiSessionChangeBus
 process-exit awaits (instant) ─────────────────────────┘           │ (publish on every write)
                                                                    ▼
                                          overlay cards · AI Sessions window · exit watcher
```

1. **Instant exits — `AiSessionProcessExitWatcher`.** Every active session
   with a PID gets a held process handle and a `WaitForExitAsync`. The moment
   the process dies, the session is re-fetched and completed (with the exit
   code when readable) and the UIs update. This kills the #1 accuracy
   complaint: nothing ever *shows running after it exited*. The watch set
   self-heals from the change bus, so sessions created by discovery, `run`,
   `watch`, or hooks — including rows restored after an app restart — are all
   covered.
2. **Instant starts — WMI `__InstanceCreationEvent` subscription**
   (`AiSessionActivityWatchers`). When claude/codex/node/ollama/cursor-agent/
   gemini/copilot processes start, a scan fires within ~1–2 s instead of
   waiting for the sweep. Works as a standard user; if WMI is unavailable it
   degrades silently to the sweep.
3. **Instant activity — file watchers.** `%USERPROFILE%\.codex` (state DB +
   rollout logs) and `%USERPROFILE%\.claude\projects` (conversation
   transcripts) are watched; any append triggers a debounced scan, so Codex
   working→done transitions and Claude activity surface in well under a
   second.
4. **Instant UI — `IAiSessionChangeBus`.** The SQLite repository is wrapped in
   a notifying decorator; *every* mutation publishes. The overlay and the AI
   Sessions window subscribe and refresh immediately (the overlay's timer
   dropped from a 2 s driver to a 10 s safety net that mostly refreshes the
   relative "Live for …" labels).
5. **The sweep stays** (10 s) purely as reconciliation: PID reuse by
   unreadable processes, WMI outages, watcher buffer overflows.

## Provider coverage

| Tool | Detection | Details captured |
| --- | --- | --- |
| Claude Code | process (claude.exe / node + package path), parent/child deduped; transcript watcher for activity | title, cwd, PID, start time |
| Codex (CLI + Desktop) | runtime processes + state DB threads + rollout run-state, torn-line and locked-DB safe | thread title, workspace, model, run state |
| `octadock run` / `watch` | explicit wrapping | full stdout/stderr logs, prompts, exit codes |
| Cursor | `cursor-agent` CLI processes (new) | title, cwd, PID |
| GitHub Copilot CLI | `copilot` CLI / node package (new) | title, PID |
| Gemini CLI | `gemini` CLI / node package (new) | title, PID |
| Ollama | server + per-model runner processes (new) | model name per runner |
| Any local process | `octadock watch --pid` | liveness, exit |

## Honest limits (and the path past them)

"Never fails, zero delay" has physical limits that no local monitor can wave
away; here is where the truth sits and what closes each gap:

- **Cloud agents are invisible locally.** GitHub Copilot *coding agent* and
  Cursor *background agents* run on GitHub/Cursor servers — there is no local
  process to find. Closing this needs authenticated provider APIs (a settings
  page for tokens + a poller). Tracked as the "remote adapters" roadmap item.
- **Semantic state needs cooperation.** "Working vs waiting for your input"
  cannot be inferred perfectly from the outside. The precise fix is the hook
  path that already exists (`octadock ai-session-event`): a Claude Code
  `settings.json` hook (SessionStart/Stop/Notification) makes lifecycle
  push-accurate. Next step: an opt-in "Install Claude Code hooks" button in
  Settings that writes those hooks automatically.
- **WSL / containers.** Processes inside WSL are not Win32 processes. The
  transcript/rollout file watchers still see their activity when the tools
  write to the Windows-mounted home; process-level tracking there is future
  work.
- **Elevated processes** cannot be opened for exit waits; the sweep's
  start-time check completes them within one interval.

## Test coverage

- Live integration: spawn a real process, track, kill → session Completed
  instantly with a timeline event; dead-PID sessions completed on first sync;
  no duplicate completion when another writer got there first.
- Change bus: throwing subscribers isolated; decorator publishes on every
  mutation and never on reads.
- Classifiers: Ollama server/runner (with model-name extraction), one-shot
  Ollama CLI ignored, cursor-agent, Copilot CLI, Gemini CLI, Claude
  parent/child dedup, Codex thread/rollout states.

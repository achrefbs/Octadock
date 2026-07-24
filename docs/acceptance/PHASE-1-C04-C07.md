# Phase 1 acceptance tooling: C-04 through C-07

Updated: 2026-07-23

These tools make automatable evidence reproducible while preserving the manual
Windows boundary. They do not mark hardware checks as passed, launch cloud
services, or silently capture the desktop or microphone. Generated JSON goes
under the ignored artifacts/acceptance directory unless OutputPath is supplied.

Exit code 0 means requested automated work ran or a plan was emitted. Exit code
1 means executed evidence failed validation. Exit code 2 means RequireComplete
was requested while honest manual or hardware rows remain.

## Current acceptance state

| Gate | Automated evidence | Still required |
| --- | --- | --- |
| C-04 | 74 deterministic controller/Core/platform rows pass, including cancellation and clipboard recovery, plus the versioned SAPI-corpus benchmark baseline below | Real microphone/device/privacy/accent/language/insertion checks |
| C-05 | Machine-readable startup signal, idle CPU, memory, handles, GUI objects, GPU availability, and opt-in artifact latency | Isolated Octadock before/after runs and measured top-three fixes |
| C-06 | Static scan is clean: 0 new and 0 baselined findings across token, raw-color, accessible-name, and pointer-only rules | Rendered keyboard, Narrator/NVDA, composed contrast, live preferences, reduced motion, and DPI matrix |
| C-07 | Explicit 50/10/1 plan, timeouts, decoded/hashed artifacts, resources/trends, exact CLI-ID/SQLite/file hash correlation, release-state contract, and SQLite checks | Real Octadock probes, operator review, and dedicated-Windows-profile soak |

## C-04 dictation

Run deterministic coverage:

    .\tools\acceptance\Invoke-DictationAcceptance.ps1 -Configuration Release

The optional local-model run is explicit because it may download approximately
640 MB into the real %LOCALAPPDATA%\Octadock model store:

    .\tools\acceptance\Invoke-DictationAcceptance.ps1 -IncludeLiveModel

That opt-in uses synthesized Windows English speech. It is not microphone
acceptance. Supply real-hardware results with ManualEvidencePath; the schema is
illustrated by tools/acceptance/examples/manual-evidence.c04.example.json. A
manual pass requires schema version 1, gate C-04, a parseable UTC timestamp, a
machine profile, and at least one evidence/hash reference.

Run the versioned corpus benchmark (23-case manifest; SAPI-generated audio is
created locally and never committed; the first run downloads the ~640 MB
Parakeet model):

    .\tools\acceptance\stt\Invoke-SttBenchmark.ps1

Cases whose SAPI voice is not installed are reported pending, never fabricated;
real-microphone speaker rows are never produced by this harness. Recorded
baseline (AMD Ryzen 7 5800H, 16 threads, 31.4 GB, Windows 11 26200): cold
start 2,785 ms; warm median RTF 0.062; onset→first partial 64–132 ms wall on a
fast-forward feed; 0 dropped/duplicated segments across 20 streaming cases.
Batch WER by category: natural-english 2.9%, short-phrases 0%, dev-vocab 3.6%,
long-form 0%, punctuation 0%, quiet-mic 0%, noise 5.9%, commands 18.2%,
filenames/paths 25.8%, numbers 57.1% (digit-vs-word tokenization),
auto-language 48.3% (es/fr SAPI). Accent/German and all real-microphone rows
remain PENDING. Measured limitation: segment-wise streaming WER exceeds batch
WER on some categories (paths 0.50 vs 0.26, numbers 0.71 vs 0.57, French 0.67
vs 0.48) from decode context loss — documented as future work, not gated.

## C-05 performance measurement

The harness records observations without applying invented product thresholds.
For startup, prefer the PID-specific log marker over InputIdle or MainWindow;
the Serilog flush interval can add up to roughly two seconds.

    .\tools\acceptance\Measure-PerformanceBaseline.ps1 -ProcessPath C:\acceptance\Octadock.exe -StartupReadyMode FilePattern -StartupReadyFilePath "$env:LOCALAPPDATA\Octadock\Logs" -StopCommandPath C:\acceptance\cli\octadock.exe -StopCommandArguments @('quit') -OutputPath .\artifacts\acceptance\performance\before.json

Repeat cold and warm runs on the same machine profile before and after a
candidate fix. A single JSON file is one measurement, not C-05 completion.
Capture latency additionally requires EnableDesktopCapture, a native CLI
executable, an artifact directory, and safe test content.

Run product process measurements under a separate Windows user/profile today.
The app uses a fixed per-user single-instance mutex/IPC namespace and normal
local-data root; without isolation a launch may forward to an unrelated running
instance. AllowNonOctadockProcessForToolingTest exists only to test measurement
code and labels the result tooling_test_only.

## C-06 WPF accessibility and visual acceptance

Run the static source gate:

    .\tools\acceptance\Test-WpfStaticAcceptance.ps1

The current source gate is clean with no baseline entries. Do not baseline parse
failures or unknown tokens, and do not interpret a clean static scan as WCAG or
rendered acceptance.

Combine static evidence with a real Windows matrix:

    .\tools\acceptance\Invoke-WpfAcceptance.ps1 -ManualEvidencePath C:\evidence\c06-manual.json

The checked-in matrix covers primary surfaces at fixed viewports, keyboard
order/activation/Escape/focus restoration, Narrator or NVDA name-role-state,
Windows high contrast, reduced transparency/animation, 100/150/200 percent
scaling, and mixed-DPI/portrait/negative-coordinate monitors. See
tools/acceptance/examples/manual-evidence.c06.example.json.

Static analysis cannot prove runtime automation names, composed contrast,
screen-reader announcements, focus restoration, or programmatic-control
semantics. Those rows remain manual even after the source scan is clean.

## C-07 long-run soak

Inspect the non-interactive plan:

    .\tools\acceptance\Invoke-SoakAcceptance.ps1 -PlanOnly

Defaults are 50 capture cycles, 10 manual-vertical scrolling sessions, and one
dictation session. Run the app, CLI, and probes under a dedicated Windows user
profile. A live run requires an explicit Octadock PID, both acknowledgement
switches, SQLite tooling, the profile database, and separate PowerShell probes:

    .\tools\acceptance\Invoke-SoakAcceptance.ps1 -ProcessId 1234 -CaptureProbePath C:\evidence\capture-probe.ps1 -ScrollingProbePath C:\evidence\scroll-probe.ps1 -DictationProbePath C:\evidence\dictation-probe.ps1 -DatabasePath "$env:LOCALAPPDATA\Octadock\octadock.db" -AcknowledgeDesktopAndMicrophoneInteraction -AcknowledgeSeparateWindowsProfileIsolation

Each probe receives Iteration, ResultPath, EvidenceDirectory, and ProcessId. It
must finish within the configured timeout, copy fresh portable artifacts into
the evidence directory, and write schema-1 JSON. Image artifacts must decode;
transcript evidence must contain expectedTranscript; passing rows require an
explicit hook/device reacquisition method. Example contracts live under
tools/acceptance/examples.

Capture and scrolling probes must invoke the CLI with `--json` and copy its
`captureId` into `databaseCorrelation.commandCaptureId`. The harness—not the
probe—queries exactly that live SQLite row, resolves its managed file inside the
profile data root, and requires its SHA-256 to match a decoded portable evidence
artifact. Cancellation creates no ID and is a command failure, preventing false
success.

There is deliberately no acceptance-only data-root override: licensing state is
rooted under the same storage tree, so an override would make a fresh trial
repeatable. Dedicated Windows-profile isolation prevents IPC/mutex and user-data
cross-talk without weakening the trial boundary. The resource limits remain
provisional regression tripwires, not leak budgets. `sqlite3` is required for a
complete live soak; missing tooling never silently passes correlation or database
integrity.

## Tooling verification

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\acceptance\AcceptanceTooling.Tests.ps1

This self-test exercises all plan schemas, a real process measurement labeled
tooling-only, synthetic exact-ID SQLite/file-hash correlation for capture and
scrolling, transcript/release validation, source-state fingerprinting, and the
clean WPF source gate. It does not drive Octadock, capture the screen, or open a
microphone.

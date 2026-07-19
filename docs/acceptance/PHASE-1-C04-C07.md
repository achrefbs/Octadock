# Phase 1 acceptance tooling: C-04 through C-07

Updated: 2026-07-19

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
| C-04 | Controller lifecycle, Core speech state machines, provider opt-in, and model-store tests | Real microphone/device/privacy/accent/language/insertion checks |
| C-05 | Machine-readable startup signal, idle CPU, memory, handles, GUI objects, GPU availability, and opt-in artifact latency | Isolated Octadock before/after runs and measured top-three fixes |
| C-06 | XAML parse, theme-token parity/reference, raw XAML/C# color, accessible-name, and pointer-only regression scan | Remediation plus keyboard, Narrator/NVDA, high contrast, preferences, and DPI matrix |
| C-07 | Explicit 50/10/1 plan, timeouts, decoded/hashed artifacts, resources/trends, release-state contract, and optional SQLite checks | Real probes/review and the correlation/isolation seams below |

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

The current source intentionally remains red until its reported token and
accessibility debt is fixed. Do not baseline parse failures or unknown tokens,
and do not interpret a future regression baseline as WCAG acceptance.

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
dictation session. A live run requires an explicit Octadock PID, the desktop /
microphone acknowledgement switch, and separate PowerShell probe scripts:

    .\tools\acceptance\Invoke-SoakAcceptance.ps1 -ProcessId 1234 -CaptureProbePath C:\evidence\capture-probe.ps1 -ScrollingProbePath C:\evidence\scroll-probe.ps1 -DictationProbePath C:\evidence\dictation-probe.ps1 -DatabasePath "$env:LOCALAPPDATA\Octadock\octadock.db" -AcknowledgeDesktopAndMicrophoneInteraction

Each probe receives Iteration, ResultPath, EvidenceDirectory, and ProcessId. It
must finish within the configured timeout, copy fresh portable artifacts into
the evidence directory, and write schema-1 JSON. Image artifacts must decode;
transcript evidence must contain expectedTranscript; passing rows require an
explicit hook/device reacquisition method. Example contracts live under
tools/acceptance/examples.

Two product seams prevent C-07 completion today:

1. Capture command replies do not expose a capture ID or safe artifact path, so
   command success, database row, and file cannot be cryptographically
   correlated without inference.
2. There is no acceptance-only data root plus isolated IPC/mutex namespace.
   Use a separate Windows user/profile to prevent cross-talk.

The harness therefore reports correlation_and_isolation_seams_pending even if
every supplied probe passes. Its resource limits are provisional regression
tripwires, not leak budgets. sqlite3 is optional; absent tooling is recorded as
unavailable instead of silently passing database integrity.

## Tooling verification

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\acceptance\AcceptanceTooling.Tests.ps1

This self-test exercises all plan schemas, a real process measurement labeled
tooling-only, and the current intentionally failing WPF source gate. It does
not drive Octadock, capture the screen, or open a microphone.

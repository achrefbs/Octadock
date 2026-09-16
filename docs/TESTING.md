# Testing

The 16 September 2026 local Release gate passed **1,180 public solution tests** (Core 651, Data 89, Windows Platform 118, App 299, CLI 23), **27 internal harness tests**, **six website static contracts** and **57 Chromium browser tests**. Self-contained app/CLI publishing and the public artifact boundary also passed. Existing non-blocking analyzer warnings remain. GitHub-hosted runs are currently blocked before startup by the account billing/spending restriction; these results are from the local canonical gate.

Run `./build/build.ps1 -Configuration Release` on Windows with the .NET 8 SDK. After the website split, this app gate has no Node.js, npm or Chromium dependency. Website checks now live in the private website repository. See [CONTRIBUTING.md](CONTRIBUTING.md) for fresh-checkout setup.

The first run on 16 September exposed a database-fixture race: disposing one fixture cleared every SQLite pool while other test classes were opening connections. Cleanup now clears only the fixture's pool. A regression test fails with the old cleanup and passes with the fix; the full gate then passed with parallel test classes still enabled.

The canonical app gate validates the local-only boundary, the public solution's tests, the isolated internal harness tests, and portable app/CLI publishing. Logs and TRX results are written under `artifacts`.

The earlier website test results above are retained as historical evidence. That browser coverage and the production newsletter/server tests have moved with the website source to the private marketing repository. App builds do not run those checks.

The native probe previously passed on 13 September in light and dark themes on 2560 × 1440 / 175% and 1920 × 1080 / 100% displays, including a negative-origin display. That hardware run was not repeated for the documentation and test-fixture cleanup. See [PROJECT-STATE.md](PROJECT-STATE.md) for current limitations.

Run the opt-in `tools/acceptance/DesktopProbe` as described in the root README to test real window construction, narrow and wide layouts on the attached displays, dock placement, own-window exclusion and native capture of a synthetic external fixture. The probe uses its own local data root. Add `--light` to verify the light theme; the default is dark. It also opens/closes the History Info drawer and visits Settings Voice models and Shortcuts. It writes PNG renderings and `results.json`; process exit code must be zero, including clean provider disposal.

Regression coverage includes per-display anchor persistence through reload/resolution changes; work-area clamping at negative origins; legacy provider migration; import integrity/cancellation; retention path containment; scroll direction, reversal, sticky headers, noisy frames, memory limits and local speech provider disposal.

Manual release acceptance remains: drag the dock through both displays and return; unplug/reconnect displays; change scale/orientation; open each utility window; capture real applications; pause/resume scrolling; exercise microphone/device loss and recording audio; test keyboard navigation, high contrast and reduced motion. Record actual hardware and results. Do not infer those outcomes from unit tests.

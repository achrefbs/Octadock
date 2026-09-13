# Testing

The 13 September 2026 Release gate passed 1,168 public solution tests, 27 internal harness tests, six website static contracts and 23 Chromium browser tests. The native probe passed in both light and dark themes on two displays with 175% and 100% scaling. See `PROJECT-STATE.md` for the measured scope and remaining acceptance work.

Run `./build/build.ps1 -Configuration Release` on Windows with .NET 8 and Node 20+.

The canonical gate validates the local-only boundary, website syntax and references, Chromium smoke/accessibility/reduced-motion/mobile fallback, the public solution's tests, the isolated internal harness tests, and portable app/CLI publishing. Logs and TRX results are written under `artifacts`.

Run the opt-in `tools/acceptance/DesktopProbe` as described in the root README to test real window construction, narrow and wide layouts on the attached displays, dock placement, own-window exclusion and native capture of a synthetic external fixture. The probe uses its own local data root. Add `--light` to verify the light theme; the default is dark. It also opens/closes the History Info drawer and visits Settings Voice models and Shortcuts. It writes PNG renderings and `results.json`; process exit code must be zero, including clean provider disposal.

Regression coverage includes per-display anchor persistence through reload/resolution changes; work-area clamping at negative origins; legacy provider migration; import integrity/cancellation; retention path containment; scroll direction, reversal, sticky headers, noisy frames, memory limits and local speech provider disposal.

Manual release acceptance remains: drag the dock through both displays and return; unplug/reconnect displays; change scale/orientation; open each utility window; capture real applications; pause/resume scrolling; exercise microphone/device loss and recording audio; test keyboard navigation, high contrast and reduced motion. Record actual hardware and results. Do not infer those outcomes from unit tests.

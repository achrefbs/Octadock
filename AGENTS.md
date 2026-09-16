# Octadock contributor instructions

Read `README.md`, `docs/LOCAL-SOFTWARE.md` and `docs/PROJECT-STATE.md` before making changes. Historical Phase-1 plans no longer define product scope.

- Keep desktop functionality free and local. Do not add accounts, activation, network clients, cloud providers, telemetry or automatic downloads.
- Preserve existing user captures and SQLite compatibility. Unknown legacy keys may be ignored.
- Use physical pixels for desktop positioning and per-display DPI only for dimensions. Test negative origins and mixed DPI.
- Use existing WPF theme/motion tokens. Keep every action keyboard-accessible and reachable on small windows.
- Run meaningful affected tests, then the canonical Release gate for a release candidate.
- Native hardware results must be measured and recorded, not inferred from unit tests. Do not publish or change repository visibility without user authorization.
- Public screenshots must use an isolated test profile, synthetic clipboard/capture data and a neutral background. Inspect the complete image for private text, account details and local paths, including content visible through translucent windows.

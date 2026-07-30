# Octadock launch-readiness report

**Branch:** `octadoc/launch-readiness`  
**Canonical repo:** `C:\Users\acera\Desktop\Octadock`  
**Remote:** `https://github.com/achrefbs/Octadock.git` (`origin/main`)  
**Audit date:** 2026-07-30  
**Base commit:** `27d1147` (`Refine capture shelf and editor experience`)

---

## Verdict

**NOT READY**

Passing tests do not make a product launch-ready. A stranger still cannot **download**, **buy**, or **activate** a trustworthy paid build from the public site. The local capture→Shelf loop is strong once a build is already installed; distribution, signing, commerce, CI signal, and legal clearance are not.

Invite-only hand installs of a local Release ZIP remain a reasonable founder experiment. That is not the same as private/public beta readiness for people who find Octadock on the web.

---

## Why this repo is canonical

| Signal | Evidence |
| --- | --- |
| Path | `C:\Users\acera\Desktop\Octadock` |
| Remote | `origin` → `https://github.com/achrefbs/Octadock.git` |
| Product identity | README + `PRODUCT.md` + `Octadock.sln` + `version.json` `0.2.0-alpha.0` |
| Authority docs | `docs/PROJECT-STATE.md`, `docs/ROADMAP.md`, `docs/CAPABILITIES.md` |
| Alternatives | Claude project caches / prunable agent worktrees under Desktop are derivatives, not the product root |

Note: the product brand is **Octadock** (not “Octadoc”). This branch uses the requested name `octadoc/launch-readiness`.

---

## Git / branch reality

| Item | State |
| --- | --- |
| Current branch | `octadoc/launch-readiness` (created from `main` @ `27d1147`) |
| Dirty at start | Clean |
| Tracking | Local only (no `origin/octadoc/launch-readiness` at audit start) |
| vs `main` | Branched from current `main`; **not** merged into `main` |
| Worktrees | Primary checkout + 1 detached Claude audit worktree + 8 **prunable** historical agent worktrees (do not delete without founder approval) |
| Remote CI | Still not a valid signal (`docs/ROADMAP.md` A-03: Actions fail before jobs; billing/quota) |

**Workspace note:** `move_agent_to_root` could not run from this subagent session; all work used absolute paths under `C:\Users\acera\Desktop\Octadock`.

---

## Before → after (this branch)

### Website / conversion honesty

| Before | After |
| --- | --- |
| Secondary pages labeled nav/footer **Download** while no installer existed | **Release status** → `index.html#get` |
| Pricing CTA **Download the free trial** → dead end | **Check release status** |
| Buy / waitlist were clickable self-hash loops | Buy is **disabled + labeled pending**; Pro waitlist is **mailto:support@octadock.com** |
| Hero CTAs only scrolled deeper | Primary hero CTA → `#get` release status |
| ≤760px hid all primary nav links | Keeps **How it works** + **Local-first** plus Release CTA |
| Scrolling capture marketed without Beta | **Scrolling page (Beta)** |
| Final CTA only disabled button | Adds **Email me when it ships** mailto |
| `web/README.md` banned truthful Beta audio copy | Aligned with shipping optional mic/system-audio **Beta** disclosure |

### App onboarding / empty states

| Before | After |
| --- | --- |
| First-run taught a nonexistent single “Capture” Dock button | Matches shipped Dock: Area / Window / Full screen + More |
| Welcome pitched toolbox + AI equally | Lead with Shelf value; AI card demoted to “Later” |
| Finish closed with no next step | Toast: **Try your first capture** (`Ctrl+Shift+4` / Dock Area) |
| History empty always said filters failed | Unfiltered empty coaches first capture |

### Security / automation

| Before | After |
| --- | --- |
| `StoragePaths.ToAbsolute` allowed `../` and absolute escapes | Rejects paths outside data root (+ unit tests) |
| `ProtocolEnabled` defaulted **true** | Defaults **off** for new profiles |
| Protocol blocked only Read/Dictation/Quit | Also blocks Record, OCR (`CaptureText`), AI, scrolling capture |
| Settings understated protocol risk | Copy explains off-by-default + blocked verbs |

---

## Test / build results (this pass)

| Command | Outcome |
| --- | --- |
| `dotnet test tests/Octadock.Core.Tests/... --filter StoragePathsTests\|SettingsServiceTests` | **Passed** (65) |
| `dotnet test tests/Octadock.App.Tests/... -c Release --filter AutomationLaunchSafetyTests\|FirstRun` | **Passed** (22) — Release used because a live `Octadock.exe` held Debug DLL locks |
| `powershell -File .\build\validate-web.ps1` | **Passed** (static contracts + 21 Chromium/Playwright checks) |
| `powershell -File .\build\copy-honesty-gate.ps1` | **Passed** |
| Full `.\build\build.ps1 -Configuration Release` | **Not re-run end-to-end** in this loop (Debug App lock + time); prior `main` evidence in `docs/PROJECT-STATE.md` recorded 1,259 desktop tests green on 2026-07-24 |

---

## Remaining blockers (must fix or disclose)

1. **No signed public artifact** — ZIP packaging exists; Authenticode / SmartScreen / published SHA / R2 download do not (`docs/ROADMAP.md` Gate D; `web/index.html` still honestly disabled).
2. **No live checkout** — Stripe Checkout URL founder-gated; Buy remains disabled on this branch by design.
3. **Client trust ring is DEV-only** — production pubkey absent (`ClientTrustAnchors.cs`); paid entitlements cannot verify for real release builds.
4. **GitHub Actions not a signal** — account/billing startup failures (Gate A-03).
5. **Legal drafts** — privacy / refunds / EULA / terms still “pending legal review.”
6. **Real-Windows acceptance incomplete** — mic/AT/mixed-DPI/clean-VM/soak (`docs/PROJECT-STATE.md`, Gate C/D).
7. **Update host not shipped** — advisory/null signature path; do not enable a public update URL without a verifier.
8. **Offline-trial deletion/replay policy** — still needs an explicit founder decision (`docs/ROADMAP.md`).

---

## Prioritized next steps

### Blockers for any paid/public beta
1. Founder: fix GitHub Actions billing → green CI on `main`.
2. Produce Authenticode-signed installer/ZIP + published SHA-256; wire `index.html` download CTA only then.
3. Rehearse money→entitlement: live Stripe Checkout + prod license-service keys (secrets out of repo) + production trust anchor in client.
4. Lawyer-clear privacy/EULA/terms/refunds; remove Draft banners.
5. Clean-VM install matrix + one D-07 onboarding rehearsal (capture + dictate in &lt;10 minutes).

### High leverage (safe follow-ons)
6. Shelf OCR one-click (landing markets OCR harder than Shelf UI exposes).
7. Expand protocol allowlist documentation; keep capture verbs opt-in.
8. Clipboard-at-rest disclosure / optional DPAPI for monitored text.
9. Fail-closed update checks when a manifest URL is configured without a signature verifier.
10. Prune/archive prunable worktrees after founder inventory approval (no destructive cleanup yet).

### Nice-to-haves (defer)
- Pro waitlist form endpoint (mailto is an honest interim)
- Pin-from-shelf (Snipaste-class differentiator)
- Memory spine (Gate E) — strategy recommends it before a stronger paid story, but ROADMAP allows earlier founder call after A–D

---

## Competitive / market notes (launch bar)

Octadock’s credible wedge is **Capture Shelf** (CleanShot-class post-capture staging on Windows), not “another ShareX.” Windows Snipping Tool already ships OCR + recording; ShareX owns free power-user depth. Table stakes for strangers: instant shelf item, copy/drag/annotate/OCR latency, code signing, local-first honesty, quiet tray.

**Launch bar used for the verdict**
- **Private beta:** core loop daily-driver for invited users; gaps labeled; distribution can be out-of-band.
- **Public beta:** signed download, privacy page cleared, drag matrix, feedback channel, claim locked to shelf UX.
- **Public launch:** production shelf+OCR+annotate (+ recording only if solid), update path, honest pricing live.

This branch improves honesty and activation coaching but does **not** clear private/public distribution gates for strangers.

Sources used in research: [ShareX](https://getsharex.com/), [CleanShot](https://cleanshot.com/features), [Microsoft Snipping Tool](https://www.microsoft.com/en-us/windows/learning-center/how-to-use-snipping-tool-on-windows-screenshots-shortcuts-and-screen-recordings), [ScreenSnap Windows roundup](https://www.screensnap.pro/blog/best-screenshot-tools-windows), [Supademo tools 2026](https://supademo.com/blog/screenshot-tools).

---

## Commits on this branch

| SHA | Message |
| --- | --- |
| `b56fb3c915ba6b077b719f405c53a53fdc153b17` | Harden storage path resolution and shrink protocol attack surface. |
| `57256cadcf14ebfd0491223cd0f651bf535dafb9` | Align first-run and empty states with the Capture Shelf activation path. |
| `f53c4afa11d36a15645e97da6f271842c7b282fd` | Make website CTAs honest about pending download and checkout. |
| `5971cc575b1402e21ef021515767e146194898b1` | docs: add launch-readiness audit report with NOT READY verdict |
| `FIXUP_SHA` | docs: restore UTF-8 encoding in launch-readiness report |

Parent of branch tip before these commits: `27d1147125f6b13c20ecb081037eff164cea147f` on `main`.

---

## Explicit judgment note

Do **not** call Octadock launch-ready because unit/web tests pass. Launch readiness requires that a real user can **understand** the product, **trust** the privacy/commercial claims, **complete** download→install→first Shelf action, and **get value**. Today: understand/trust-on-copy are strong; complete/get-value from the website are blocked.

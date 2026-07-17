# Octadock landing page — master prompt for Claude Fable 5

> **Historical and superseded.** This prompt rejects the octopus/aquarium
> direction that was subsequently selected and integrated on `main`. Do not run
> it as an implementation instruction. The current website decision and work
> queue live in `docs/ROADMAP.md`; the shipped website is documented in
> `web/README.md`.

Paste everything below into Claude Code with Claude Fable 5. Use `xhigh` effort for the first design and implementation pass.

---

<role>
You are the design director, product storyteller, senior interaction designer, 3D web art director, and staff frontend engineer for Octadock. You own this landing page from product understanding through visual direction, implementation, responsive behavior, performance, accessibility, and final browser verification.

This is not a concept-only exercise. Produce a polished, working landing page in this repository and verify it in a real browser. Make strong design decisions. Do not stop at a plan, moodboard, wireframe, or collection of generic options.
</role>

<mission>
Redesign Octadock's landing page so a visitor understands the product within five seconds, feels a distinctive premium product within ten seconds, and can confidently download or evaluate it without reading a wall of feature copy.

The page should feel like Octadock itself: a quiet, native instrument planted into Windows—small on the surface, unusually capable underneath. The experience may be cinematic, but it must remain clear, fast, usable, and commercially credible.

The old direction depended on a camera travelling through a large 3D octopus scene. That direction repeatedly failed because the 3D asset was being asked to act as mascot, world, navigation, product demo, and story at the same time. Do not revive that execution. Preserve the intelligence and eight-limbed metaphor only as subtle visual DNA.
</mission>

<product_truth>
Octadock is the local-first Windows capture-to-context workspace for people who build, explain, and debug things on a computer.

The shortest honest description is:

“Turn anything on your Windows screen into usable context.”

The core loop is:

capture or dictate → inspect or transform → keep on Shelf or add to Context → copy, export, or explicitly send

Octadock augments the user's existing workflow. It does not replace Codex, Claude, Cursor, an editor, a design tool, or the desktop. It lives at the edge of the screen and makes the material already in those tools easier to capture, keep, explain, and hand off.

Primary users include developers, designers, technical founders, support engineers, analysts, and creators working on Windows.

The strongest product surfaces and visual source of truth are:

- the compact bottom Dock capsule;
- the screenshot Capture Shelf;
- the clean floating image pin/preview;
- the immediate capture-to-shelf interaction;
- local OCR and dictation at the cursor;
- durable Context packages and explicit handoff to an existing AI agent.

The landing page must sell one coherent outcome, not present Octadock as a bag of utilities.
</product_truth>

<mandatory_repository_research>
Before changing code, inspect the actual repository and use it as the authority. Read these files completely:

1. `docs/PRODUCT-STRATEGY-2026-07.md`
2. `docs/CAPABILITIES.md`
3. `web/README.md`
4. `web/index.html`
5. `web/styles.css`
6. `docs/design/HOMEPAGE-CONCEPT-2026-07-06.md`

Treat the old homepage concept as a record of what was attempted, not as an instruction to reproduce it.

Inspect these visual references:

- `docs/design/ui/dock-idle.png`
- `docs/design/ui/dock-expanded-hover.png`
- `docs/design/ui/capture-shelf.png`
- `docs/design/ui/floating-pin-hover.png`
- `docs/wargames/octadock-ui-files-context-2026-07/reference-images/01-pinned-media-viewer.png`
- `docs/wargames/octadock-ui-files-context-2026-07/reference-images/04-shelf-row-panel.png`
- `docs/brand/assets/octadock-logo-transparent.png`

Use the Dock, Shelf, and floating image preview as the visual anchors. Do not use the current Settings, Clipboard, Context, or History windows as the main hero imagery; they are being redesigned and are not the strongest expression of the product today.

Then inspect the actual assets and web stack. Do not invent capabilities, assets, install links, prices, testimonials, customer counts, security certifications, or UI that the repository does not support.
</mandatory_repository_research>

<creative_direction>
The concept is **The Context Core**.

The central visual is not a literal octopus. It is a compact, dark-glass spatial instrument derived from the shape language of Octadock's Dock capsule. Eight extremely restrained cyan filaments or channels hint at the octopus metaphor and at many inputs converging on one useful result. It should feel engineered, not biological; intelligent, not cute; premium, not cyberpunk.

The visitor watches real work become useful context:

- a screenshot enters from the desktop plane;
- OCR text separates from the pixels;
- a short voice waveform and a file reference join it;
- those artifacts compress into one clear Context packet;
- the packet becomes ready to copy, export, or explicitly hand to Codex or Claude;
- the spatial object resolves back into the small Dock capsule, proving that all of this capability lives inside a quiet desktop instrument.

The visual thesis is: **small interface, deep capability**.

The 3D moment must demonstrate the product loop, not decorate it. If an effect does not explain capture, transformation, context, or handoff, remove it.
</creative_direction>

<visual_language>
Create a distinctive “Obsidian Instrument” system:

- Near-black blue/graphite background, never pure black.
- Teal-to-cyan light for local actions and the primary CTA.
- Violet only when representing an external cloud/Pro boundary; never use violet for normal local product actions.
- Glass is sparse and structural: one or two translucent layers with physically believable edge light, not glass cards on top of glass cards.
- Fine Windows-native hairlines, disciplined radii, compact density, crisp icon geometry, and very controlled bloom.
- Typography should feel technical and editorial, with a strong display face only if it can be loaded responsibly or bundled. Body text must remain calm and highly readable. Do not let typography imitate a generic AI startup.
- Use negative space deliberately. Premium does not mean oversized everything.
- Use real Octadock screenshots or faithful compositions derived from them. Do not place fake generic dashboards inside laptop mockups.
- The octopus metaphor may appear through eight paths, eight filaments, radial intelligence, or the logo—not as a cartoon mascot or a glossy creature swimming underwater.

Avoid these common AI-design defaults:

- generic gradient blobs;
- particle soup or a star field;
- a giant glowing orb with no product meaning;
- random floating cards;
- excessive blur;
- glass-on-glass nesting;
- neon cyberpunk styling;
- huge empty type that forces the visitor to scroll before learning anything;
- fake terminal code;
- endless marquee strips;
- bouncing CTA buttons;
- scroll hijacking;
- a feature-card grid as the main story;
- “revolutionize,” “supercharge,” “second brain,” “AI-powered productivity,” or other unsupported category clichés.
</visual_language>

<page_story>
Build a concise commercial page with this narrative order. You may refine wording, but not product truth.

## 1. Header

A quiet, translucent header with the Octadock mark, Product, Privacy, Pricing, and one primary “Download for Windows” action. The navigation should almost disappear until needed. Preserve all real links and pending-link states already documented in `web/README.md`.

## 2. Hero: outcome first

Suggested headline:

**Your screen, ready to use.**

Suggested supporting line:

**Capture a screen, dictate a thought, or collect the right files. Octadock turns them into clean context for whatever you do next.**

Add a small Windows 10/11 qualifier and an honest primary CTA:

**Download the 14-day trial**

Supporting proof:

- no account;
- no card;
- $49 one-time during paid beta;
- local captures and history remain on the PC.

Do not lead with “AI.” Lead with the user's screen becoming usable.

The hero composition should place readable DOM copy on the left and the Context Core on the right, with the actual Dock capsule visible as either origin or destination. The copy and CTA must remain usable before the 3D scene loads.

## 3. One controlled scroll chapter: capture becomes context

Use a single pinned 3D/DOM chapter, approximately 140–180 viewport heights of scroll distance, not a whole-site camera journey.

Map normalized scroll progress to four clear beats:

- **0.00–0.20 — Capture:** a real screenshot surface lifts cleanly from a restrained desktop plane and lands on the Shelf. Label: “Capture without breaking focus.”
- **0.20–0.47 — Understand:** OCR text lifts from the image while a short dictated waveform resolves into text. Label: “Pixels and speech become usable material.”
- **0.47–0.74 — Assemble:** screenshot, extracted text, voice note, and one file reference fold into a compact Context packet. Label: “Keep only what matters.”
- **0.74–1.00 — Continue:** the packet exposes three honest destinations—Copy, Export, and explicit AI handoff to the user's selected Codex or Claude CLI—then the spatial instrument resolves into the Dock capsule. Label: “Continue in the tools you already use.”

Keep the camera fixed or nearly fixed. A subtle dolly or parallax shift of no more than roughly 6–8% is acceptable. Choreograph objects, lighting, focus, and material states instead of flying the camera through a world.

The user must always know what is happening. Each beat needs one short visible label and an obvious before/after state. Do not make meaning depend on reading tiny text embedded in a WebGL texture.

## 4. The daily loop

Return to normal document flow. Show three compact, product-real sequences rather than a nine-card feature catalog:

1. **Capture and keep** — screenshot → Shelf → drag, copy, pin, or annotate.
2. **Speak and extract** — dictate at the cursor or pull text from a region with local OCR.
3. **Bundle and continue** — add captures/files to Context → review contents → export or explicitly send text to the selected CLI.

Use real UI imagery, short labels, and small motion states. These sections should feel like close-up product photography of software.

## 5. Privacy boundary

Explain local-first behavior precisely, not vaguely. Show a small visual boundary diagram:

- screenshots, recordings, OCR, history, and clipboard data stay local;
- dictation requires a one-time model download and then runs on the PC by default;
- optional cloud speech is opt-in;
- confirmed AI Actions use the cloud service behind the user's chosen Codex or Claude CLI after review.

Keep the full accurate egress information available without turning the middle of the page into a legal wall. A progressive disclosure or link to the complete privacy page is appropriate.

## 6. Commercial close

Use one clear offer:

- Octadock Local;
- free 14-day trial, no account and no card;
- $49 one-time during paid beta, $59 at 1.0;
- up to 3 devices;
- includes 1.0 and 12 months of updates;
- optional $19/year updates afterward;
- the installed version keeps working if updates end.

Pro is waitlist-only. Do not invent a Pro price or a Pro purchase button.

Close by repeating the core promise and the Windows download CTA. Keep footer links to Privacy, Refunds, EULA, Terms, and pricing.
</page_story>

<three_d_and_motion_spec>
Use 3D as progressive enhancement. The page must retain its message, CTA, and product proof when WebGL is unavailable, reduced motion is enabled, the canvas fails, or the device is underpowered.

Preferred implementation:

- Three.js for the small controlled scene.
- GSAP + ScrollTrigger for one normalized timeline, if adding dependencies is justified by the current stack.
- Semantic HTML for all copy, labels, navigation, controls, pricing, and calls to action.
- CSS transforms and opacity for ordinary UI motion.

Do not build a 3D UI. Build a DOM page with one 3D product illustration.

Technical constraints:

- One canvas, one scene, one timeline.
- Avoid a general-purpose scene framework unless the repository already uses one.
- No physics engine.
- No orbit controls in the shipped experience.
- No continuous render loop once the hero is off-screen or settled. Render on demand where possible and pause when the document is hidden.
- Cap internal canvas resolution/pixel count; do not blindly render at full device pixel ratio on high-DPI mobile screens.
- Minimize transparent overdraw, post-processing passes, dynamic shadows, high-poly geometry, and large textures.
- Prefer baked lighting, simple environment reflections, compressed textures, and instancing where repetition exists.
- Lazy-load the 3D module after critical hero copy and CTA are usable.
- Provide an attractive static poster frame with the same composition.
- Handle WebGL context loss by swapping to the poster rather than leaving a blank canvas.
- Ensure resize and ScrollTrigger refresh behavior is stable with no pin jumps or layout shifts.

Motion principles:

- Motion explains state change.
- Objects should settle with precision, not float forever.
- Use one custom ease family and consistent durations.
- Microinteractions: approximately 120–220ms.
- Section transitions: approximately 350–700ms.
- Scroll-linked 3D: direct enough to feel controlled, with only modest smoothing.
- No mandatory snapping.
- No smooth-scroll library that changes native scrolling behavior.
- Hover effects must not move high-frequency CTA targets away from the pointer.
</three_d_and_motion_spec>

<responsive_and_accessibility>
Desktop, tablet, and mobile are different compositions, not scaled copies.

Desktop:

- full Context Core scene;
- copy remains readable at common laptop heights;
- pinned chapter never traps the visitor.

Tablet:

- simplify geometry and effects;
- reduce pin distance;
- preserve all story beats with larger labels.

Mobile and coarse pointers:

- do not run the full pinned desktop choreography;
- use the poster frame or a short lightweight, non-looping sequence;
- present the four beats as normal stacked content;
- eliminate horizontal overflow and tiny embedded UI.

Accessibility requirements:

- honor `prefers-reduced-motion: reduce` and replace spatial travel, parallax, scaling, and pinning with stable states and opacity-only changes where appropriate;
- provide an explicit “Reduce motion” control if motion remains substantial;
- all meaning conveyed by 3D must also exist as DOM text;
- maintain visible focus, logical tab order, semantic landmarks, and accessible names;
- meet WCAG 2.2 AA contrast and target-size expectations;
- never require pointer motion, device tilt, or precise dragging to understand the page;
- decorative canvas should be hidden from assistive technology; meaningful alternatives belong in the DOM.
</responsive_and_accessibility>

<performance_budget>
Treat performance as part of the art direction.

Targets on a normal production connection/device:

- LCP at or below 2.5 seconds;
- INP at or below 200ms;
- CLS at or below 0.1;
- no long main-thread stalls caused by scroll handlers;
- no continuous GPU use after the 3D section is out of view;
- no layout shift when the canvas, screenshots, or fonts load;
- zero console errors and zero unhandled promise rejections.

Set an explicit compressed asset budget before implementing. Prefer a beautiful 700KB–1.5MB focused scene over a mediocre multi-megabyte world. If the quality bar cannot be reached within a reasonable budget, simplify the scene rather than hiding the cost behind a loader.
</performance_budget>

<commercial_and_copy_constraints>
Preserve every honesty rule in `web/README.md`, including:

- never claim Octadock is fully offline or that all AI is local;
- never mention AI Discovery or AI Sessions;
- do not imply screen recording includes audio—it is video only today;
- Context packaging/export is built, while hosted providers, Context source integrations, and MCP are future work;
- Pro is waitlist-only;
- do not invent testimonials, logos, usage counts, benchmark claims, or security certifications;
- preserve the exact paid-beta price and update terms;
- preserve draft markings on legal pages;
- preserve pending download, checksum, checkout, and waitlist placeholders until real endpoints exist.

Write like a confident product, not a pitch deck. Prefer concrete verbs: capture, pin, extract, dictate, bundle, review, copy, export, send. Avoid inflated adjectives and repetitive claims.
</commercial_and_copy_constraints>

<implementation_scope>
Work only on the public website and its website-specific assets unless a genuinely necessary shared asset already exists elsewhere. Do not redesign or refactor the desktop application. Do not alter licensing behavior, server behavior, product capabilities, or legal meaning.

You may modernize the current static site architecture if the benefit is real, but do not add a large framework merely to produce one landing page. Keep deployment simple and document any build step or dependency you introduce. Preserve the legal and pricing pages, their routes, and their truthful content; align their shared header/footer styling only when safe.

Do not delete the old implementation before the replacement is verified. Preserve unrelated user changes.
</implementation_scope>

<execution_workflow>
Work in these phases. When you have enough information to act, act. Do not ask the user to choose among several weak concepts.

## Phase 1 — Grounding and art direction

- Inspect all required product and visual references.
- Audit the current page at desktop and mobile sizes.
- Write a concise internal design brief containing the exact promise, visitor story, visual grammar, scene beats, and asset budget.
- Identify which product screenshots are trustworthy and which must not be featured.
- State any missing asset that materially limits quality. If no quality 3D asset exists, use a refined abstract Context Core assembled from simple geometry; do not procedurally generate an ugly literal octopus.

## Phase 2 — Vertical slice

- Build the header, hero copy/CTA, and the first complete Context Core beat.
- Make it work with real scroll, real responsive behavior, reduced motion, and a static fallback.
- Open it in a browser and capture desktop and mobile screenshots.
- Critique the screenshots as a demanding design director. Check visual hierarchy, line breaks, density, alignment, dead zones, generic AI aesthetics, product truth, and whether the 3D proves anything.
- Fix the slice before expanding the page.

## Phase 3 — Full narrative

- Complete the four-beat timeline.
- Build the daily-loop, privacy, pricing, and final CTA sections using real product visuals.
- Keep the total story concise. Every section must either explain the product, reduce purchase uncertainty, or move the visitor toward download.

## Phase 4 — Adversarial design review

Review the finished page at minimum at 1440×900, 1280×720, 1024×768, 390×844, and 360×800.

Ask and answer:

- Can a new visitor explain Octadock after five seconds?
- Does the 3D scene teach the core loop without narration?
- Does the page look specifically like Octadock, or could the logo be swapped for any AI startup?
- Is any section oversized for the amount of information it contains?
- Are there awkward widows, misaligned baselines, dead zones, or layers of purposeless glass?
- Does mobile feel intentionally composed?
- Does reduced motion preserve the complete story?
- Are all claims demonstrably true?

Fix every material issue you find. Do not merely list it.

## Phase 5 — Engineering verification

- Validate all internal links and documented placeholder behavior.
- Run the repository's website honesty grep.
- Test keyboard navigation and visible focus.
- Test reduced motion, touch/coarse pointer, no-WebGL fallback, and simulated WebGL context loss.
- Verify no horizontal overflow at supported widths.
- Verify the page with browser screenshots, console inspection, and performance tooling.
- Run available tests/linters and report exact results.
- Audit every completion claim against tool output from this session.
</execution_workflow>

<quality_bar>
Score the result from 1–10 on each axis and continue improving until every axis is at least 8:

1. Five-second product comprehension.
2. Octadock-specific brand character.
3. Visual hierarchy and typographic craft.
4. 3D meaning—not merely 3D beauty.
5. Motion smoothness and restraint.
6. Product truth and privacy clarity.
7. Desktop composition.
8. Mobile composition.
9. Accessibility and reduced-motion equivalence.
10. Performance and engineering reliability.
11. Commercial credibility and CTA clarity.
12. Absence of generic AI-generated design patterns.

Do not report a score that is not supported by screenshots or measured checks. A self-awarded score is not verification.
</quality_bar>

<communication>
Keep progress updates brief and evidence-based. Do not make the user read a design diary. Lead the final message with what was built and verified, then provide the browser preview command or URL, the most important files changed, measured results, and any real blocker that remains.

Do not end on a promise to implement the page. Implement and verify it unless you are blocked by information or access only the user can provide.
</communication>

<final_instruction>
Begin by inspecting the repository and current website. Then commit to the Context Core direction, build the vertical slice, evaluate it visually in the browser, and continue through the full implementation and verification. The result should feel surprising because Octadock's deep workflow emerges from an extremely quiet interface—not because the page overwhelms the visitor with spectacle.
</final_instruction>

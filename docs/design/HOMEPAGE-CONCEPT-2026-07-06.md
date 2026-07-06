# Octadock Homepage Concept

Date: 2026-07-06  
Status: concept direction, no generated mockups or image assets included

## Product Story

Octadock should not be positioned as another screenshot tool.

The homepage should frame Octadock as a Windows command surface for turning what
is on screen into useful context:

> Capture what matters. Understand it. Act without leaving your flow.

The product promise:

- capture anything on screen;
- keep it close as a desktop object;
- copy, save, annotate, pin, drag, or recover it later;
- extract text with OCR;
- dictate into any app;
- read text aloud;
- preview files quickly;
- collect screenshots, clips, files, OCR, and prompts into future context
  packages.

## Primary Audience

- developers;
- founders;
- designers;
- support teams;
- technical writers;
- Windows power users;
- people working with AI tools who constantly need to capture, explain, paste,
  or package context.

## Homepage Positioning

Primary headline:

> Octadock

Primary supporting copy:

> Capture, read, dictate, preview, and package context from your Windows desktop.

Alternative supporting copy:

> The local-first Windows dock for screenshots, voice, files, OCR, and AI-ready
> context.

CTA:

- Join Waitlist
- See Plans

Trust strip:

- Windows 10/11
- Local by default
- Bring your own keys
- No silent uploads

## Hero Direction

Use a premium 3D translucent blue/cyan octopus as the product symbol, but build
it as a serious technical object, not as a cartoon mascot.

Visual qualities:

- glass/acrylic material;
- electronic cyan/teal internal light paths;
- dark obsidian background;
- calm, intelligent, premium tone;
- tentacles as a metaphor for the product's many actions;
- no underwater scenery;
- no coral, bubbles, ocean props, or cute mascot treatment;
- no generated mockup UI baked into the image.

The hero should be buildable:

- live HTML for headline, copy, CTAs, nav, trust strip;
- product UI fragments built as HTML/CSS, not embedded in generated art;
- octopus rendered as a real asset path:
  - v1: static high-quality render with CSS/JS parallax;
  - v2: Blender/Spline GLB rendered through Three.js;
  - v3: full WebGL material and tentacle animation if worth the budget.

## Hero Animation Concepts

### 1. Scroll Swim

The octopus starts partly off-screen on the right. As the user scrolls, it moves
into the page, tentacles trailing down into the next section.

Interaction:

- scroll progress controls position, scale, and slight rotation;
- tentacle paths reveal feature cards;
- feature cards are real DOM elements.

Build path:

- GSAP ScrollTrigger or native scroll timeline;
- Three.js for particles/light field;
- CSS transforms for the first version;
- later upgrade to GLB animation.

### 2. Tentacle Actions

Each tentacle maps to one Octadock action:

- capture;
- OCR;
- dictate;
- read;
- preview;
- annotate;
- pin;
- context shelf.

Interaction:

- hover or scroll highlights one tentacle;
- a small product card appears near the tentacle;
- cards show actual UI snippets using HTML/CSS.

Build path:

- SVG motion paths for tentacle light trails;
- DOM cards anchored to path points;
- accessible fallback card list below the hero.

### 3. Dock To Octopus

The permanent Octadock dock capsule is the starting visual. On load or scroll, it
expands into the octopus form, suggesting that the dock is the product's face and
the tentacles are its workflows.

Interaction:

- dock capsule appears first;
- capsule glow expands;
- octopus resolves behind it;
- dock actions become feature anchors.

Build path:

- CSS/Canvas glow;
- Three.js particle reveal;
- real dock UI drawn in HTML/CSS.

### 4. Context Packet Finale

At the bottom of the homepage, the tentacles gather several objects into a single
Context Shelf package:

- screenshot;
- OCR text;
- clipboard clip;
- file preview;
- prompt snippet;
- short recording.

This should preview the future AI layer without overpromising that every piece
is already built.

## First-Viewport Structure

Recommended hierarchy:

1. Top nav:
   - Octadock wordmark;
   - Product;
   - Pricing;
   - Roadmap;
   - Docs;
   - Join Waitlist.

2. Hero text:
   - eyebrow: "Windows-first. Local by default.";
   - H1: "Octadock";
   - supporting copy;
   - primary and secondary CTAs.

3. Hero visual:
   - translucent blue octopus, centered-right;
   - dark negative space on left for text;
   - subtle product cards around tentacles.

4. Trust row:
   - Offline capture;
   - BYO keys;
   - No silent uploads.

5. Hint of next section:
   - first band visible below the fold, not a hard stop.

## Page Sections After Hero

The full landing page can follow this order:

1. Capture Shelf
   - "Capture anything. Decide later."
   - Explain bottom-left shelf, copy/save/annotate/pin/drag/history.

2. Voice and Read
   - "Speak into any app. Read anything back."
   - Local Parakeet/Whisper, optional cloud STT, Windows TTS, optional premium
     voices.

3. Preview and Transform
   - File preview, clipboard history, text transforms.

4. Context Shelf
   - Future-facing but honest: bundle captures, OCR, clips, files, prompts, and
     logs.

5. Privacy
   - local-first defaults;
   - explicit cloud sends only;
   - BYO keys;
   - redaction before AI later.

6. Pricing
   - Local license;
   - Pro subscription with credits;
   - Team later.

7. Waitlist / beta signup
   - short form;
   - ask for Windows version, use case, and AI interest.

## Copy Direction

Tone:

- direct;
- premium;
- practical;
- privacy-aware;
- builder-focused.

Avoid:

- generic AI hype;
- vague "boost productivity" language;
- suggesting cloud upload happens automatically;
- overpromising future Context Shelf/Ask AI as already shipped.

Good copy examples:

- "Your screen becomes working context."
- "Everything starts local. Cloud only when you ask."
- "Capture first. Decide later."
- "Dictate into the app you already use."
- "Turn screenshots, files, clips, and OCR into a clean context packet."

## Build Notes

Preferred production stack:

- Next.js or Vite for the marketing site;
- Three.js or React Three Fiber for the 3D hero layer;
- GSAP ScrollTrigger for scroll choreography if acceptable;
- CSS/SVG for light trails and feature card motion;
- real text and controls in HTML for accessibility and performance.

Asset plan:

1. Commission or create a real GLB octopus asset.
2. Keep material simple: transparent acrylic/glass, cyan internal emissive lines.
3. Create 2-3 animation clips:
   - idle drift;
   - swim-in;
   - tentacle gather.
4. Use generated images only as mood references, not final site assets.
5. Keep a static fallback image for low-power devices and reduced motion.

Performance guardrails:

- lazy-load heavy 3D after first paint;
- respect `prefers-reduced-motion`;
- provide static fallback on mobile;
- keep hero text readable without the 3D layer;
- avoid autoplay audio;
- never block signup CTA on WebGL support.

## Homepage Pricing Message

The homepage should introduce the business model simply:

> Start with the local app. Add Pro when you want cloud accuracy and AI context.

Plan framing:

- Local: one-time Windows license, local-first features.
- Pro: subscription, cloud credits, premium speech/AI, early AI context features.
- Team: later.

Avoid exact prices in the hero. Put pricing details lower on the page after the
user understands the product.

## Design Rules

- The octopus is a symbol of many tools in one dock, not a joke.
- Use dark obsidian glass, teal/cyan accents, and high contrast.
- Product surfaces should look like real utility UI, not marketing cards.
- Do not put cards inside cards.
- Do not bake copy into hero images.
- Do not use stock people.
- Do not use underwater scenes.
- Do not use generated homepage mockups as implementation references.

## Open Decisions

- Should Octadock.com launch as waitlist-only or with paid beta checkout?
- Should the first hero use a static render or wait for a real GLB asset?
- Should Pro be mentioned in the first viewport or only lower on the page?
- Should the octopus inherit the current app logo shape or become a new premium
  3D character/object?
- Should Context Shelf be described as "coming soon" until a working slice lands?

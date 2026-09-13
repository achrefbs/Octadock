import { expect, test } from '@playwright/test';

// This Windows host intermittently loses headless Chromium's hardware context
// before its first frame. Use deterministic software WebGL for this test file
// only; real canvas rendering, pixel stability and fallback checks stay enabled.
test.use({
  launchOptions: { args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] },
});

const directions = ['air', 'studio', 'nocturne'];
const modelPath = '/assets/octopus-v10/octopus-rigged-v12.glb';

function watchRequests(page, baseURL) {
  const origin = new URL(baseURL).origin;
  const external = [];
  const errors = [];
  const loadedModels = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => {
    const url = new URL(request.url());
    if (['http:', 'https:'].includes(url.protocol) && url.origin !== origin) {
      external.push(request.url());
    }
  });
  page.on('response', response => {
    const url = new URL(response.url());
    if (response.status() >= 400) errors.push(`HTTP ${response.status()}: ${url.pathname}`);
    if (url.pathname === modelPath && response.ok()) loadedModels.push(response.url());
  });
  return { external, errors, loadedModels };
}

function mascotFor(page, direction) {
  const host = page.locator(`[data-mascot][data-direction="${direction}"]`);
  return {
    host,
    canvas: host.locator('canvas[data-mascot-canvas]'),
    poster: host.locator('img[data-mascot-poster]'),
    toggle: host.locator('button[data-mascot-toggle]'),
  };
}

async function frameCount(host) {
  return Number(await host.getAttribute('data-mascot-frames'));
}

async function expectStillFrame(page, mascot) {
  await expect(mascot.canvas).toHaveCSS('opacity', '1');
  // Wait for a queued paint before sampling. The checks cover both the renderer
  // loop and visible pixels, rather than relying only on an advertised state.
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const frames = await frameCount(mascot.host);
  // Read the preserved WebGL drawing buffer directly. Studio rotates its canvas
  // in CSS; an element screenshot also captures subpixel page compositing and
  // overlaid controls, which can change while the actual mascot pixels are still.
  const readPixels = () => mascot.canvas.evaluate(canvas => canvas.toDataURL('image/png'));
  const pixels = await readPixels();
  await page.waitForTimeout(350);
  expect(await frameCount(mascot.host), 'a still mascot must stop its render loop').toBe(frames);
  expect((await readPixels()) === pixels, 'a still mascot must not drift visually').toBe(true);
}

for (const direction of directions) {
  test(`${direction} loads the local octopus and supports keyboard pause and resume`, async ({ page, baseURL }) => {
    const observed = watchRequests(page, baseURL);
    await page.goto(`/concepts/${direction}.html`);
    const mascot = mascotFor(page, direction);
    await mascot.host.scrollIntoViewIfNeeded();
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'ready', { timeout: 15_000 });
    await expect(mascot.canvas).toBeVisible();
    await expect.poll(() => frameCount(mascot.host)).toBeGreaterThan(0);
    expect(observed.loadedModels).toHaveLength(1);

    await expect(mascot.toggle).toHaveAccessibleName('Pause octopus animation');
    await expect(mascot.toggle).toHaveAttribute('aria-pressed', 'true');
    await mascot.toggle.focus();
    await page.keyboard.press('Space');
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'still');
    await expect(mascot.host).toHaveAttribute('data-mascot-playing', 'false');
    await expect(mascot.toggle).toHaveAttribute('aria-pressed', 'false');
    await expect(mascot.toggle).toHaveAccessibleName('Play octopus animation');
    await expectStillFrame(page, mascot);

    const pausedFrames = await frameCount(mascot.host);
    await page.keyboard.press('Enter');
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'ready');
    await expect(mascot.host).toHaveAttribute('data-mascot-playing', 'true');
    await expect.poll(() => frameCount(mascot.host)).toBeGreaterThan(pausedFrames);

    await page.locator('footer').scrollIntoViewIfNeeded();
    await expect(mascot.host).not.toBeInViewport();
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'still');
    await expect(mascot.host).toHaveAttribute('data-mascot-playing', 'false');
    const offscreenFrames = await frameCount(mascot.host);
    // Do not screenshot the hidden canvas: locator screenshots scroll it back
    // into view and would restart rendering while measuring the pause.
    await page.waitForTimeout(350);
    expect(await frameCount(mascot.host), 'an offscreen mascot must stop rendering').toBe(offscreenFrames);
    await mascot.host.scrollIntoViewIfNeeded();
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'ready');
    await expect(mascot.host).toHaveAttribute('data-mascot-playing', 'true');
    await expect.poll(() => frameCount(mascot.host)).toBeGreaterThan(offscreenFrames);

    expect(observed.external).toEqual([]);
    expect(observed.errors).toEqual([]);
  });

  test(`${direction} respects reduced motion and fits desktop and phone widths`, async ({ page, baseURL }) => {
    const observed = watchRequests(page, baseURL);
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(`/concepts/${direction}.html`);
    const mascot = mascotFor(page, direction);
    await mascot.host.scrollIntoViewIfNeeded();
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'still', { timeout: 15_000 });
    await expect(mascot.toggle).toHaveAttribute('aria-pressed', 'false');
    await expect(mascot.host).toHaveAttribute('data-mascot-playing', 'false');
    await expect.poll(() => frameCount(mascot.host)).toBeGreaterThan(0);
    await expectStillFrame(page, mascot);

    for (const viewport of [{ width: 1280, height: 900 }, { width: 320, height: 480 }]) {
      await page.setViewportSize(viewport);
      await mascot.host.scrollIntoViewIfNeeded();
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      const bounds = await mascot.host.boundingBox();
      expect(bounds).not.toBeNull();
      expect(bounds.width).toBeGreaterThan(0);
      expect(bounds.x).toBeGreaterThanOrEqual(-1);
      expect(bounds.x + bounds.width).toBeLessThanOrEqual(viewport.width + 1);
      await expect(mascot.toggle).toBeVisible();
      await expect(mascot.toggle).toBeEnabled();
    }
    expect(observed.external).toEqual([]);
    expect(observed.errors).toEqual([]);
  });

  test(`${direction} shows its poster when WebGL is unavailable`, async ({ page }) => {
    const pageErrors = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    await page.addInitScript(() => {
      const getContext = HTMLCanvasElement.prototype.getContext;
      HTMLCanvasElement.prototype.getContext = function unavailableWebGl(type, ...args) {
        if (/^(webgl2?|experimental-webgl)$/.test(String(type))) return null;
        return getContext.call(this, type, ...args);
      };
    });
    await page.goto(`/concepts/${direction}.html`);
    const mascot = mascotFor(page, direction);
    await mascot.host.scrollIntoViewIfNeeded();
    await expect(mascot.host).toHaveAttribute('data-mascot-state', 'fallback');
    await expect(mascot.poster).toBeVisible();
    await expect(mascot.poster).toHaveCSS('opacity', '1');
    await expect.poll(() => mascot.poster.evaluate(image => image.complete && image.naturalWidth > 0)).toBe(true);
    await expect(mascot.toggle).toBeDisabled();
    await expect(mascot.host).toHaveAttribute('data-mascot-playing', 'false');
    expect(pageErrors).toEqual([]);
  });
}

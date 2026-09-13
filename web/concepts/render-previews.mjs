// Development-only rendering helper. Serve web/ locally before running:
// node concepts/render-previews.mjs [http://127.0.0.1:4174] [--posters-only]
import { chromium } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const base = new URL(process.argv.find(argument => /^https?:/.test(argument)) || 'http://127.0.0.1:4174');
if (!['127.0.0.1', 'localhost', '[::1]'].includes(base.hostname)) {
  throw new Error('Preview rendering requires a local static server.');
}
const postersOnly = process.argv.includes('--posters-only');
const output = name => fileURLToPath(new URL(name, import.meta.url));
await mkdir(output('posters/'), { recursive: true });
await mkdir(output('previews/'), { recursive: true });

// Software WebGL gives repeatable images on headless Windows machines whose
// background GPU contexts may otherwise be lost. This is only a QA setting.
const browser = await chromium.launch({
  headless: true,
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
});

try {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 1200 },
    deviceScaleFactor: 1,
    reducedMotion: 'reduce',
  });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));

  async function waitForAssets() {
    await page.evaluate(async () => {
      await document.fonts.ready;
      await Promise.all([...document.images].map(image => image.decode().catch(() => {})));
    });
  }

  for (const direction of ['air', 'studio', 'nocturne']) {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await page.goto(new URL(`/concepts/${direction}.html`, base).href);
    await page.waitForFunction(() => {
      const host = document.querySelector('[data-mascot]');
      return host?.dataset.mascotState === 'still' && Number(host.dataset.mascotFrames) > 0;
    }, null, { timeout: 25_000 });
    await waitForAssets();

    // Capture the actual transparent render target, not an edited illustration
    // or a screenshot containing the surrounding page background.
    const png = await page.locator('[data-mascot-canvas]').evaluate(canvas => canvas.toDataURL('image/png'));
    await writeFile(output(`posters/${direction}.png`), Buffer.from(png.split(',')[1], 'base64'));
    if (!postersOnly) {
      await page.screenshot({ path: output(`previews/${direction}.png`) });
      await page.setViewportSize({ width: 390, height: 844 });
      await page.locator('[data-mascot]').scrollIntoViewIfNeeded();
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      await page.evaluate(() => scrollTo(0, 0));
      await page.screenshot({ path: output(`previews/${direction}-mobile.png`), fullPage: true });
    }
    console.log(`${direction}: original rig rendered; ${postersOnly ? 'poster' : 'poster and previews'} saved.`);
  }

  if (!postersOnly) {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await page.goto(new URL('/concepts/', base).href);
    await waitForAssets();
    await page.screenshot({ path: output('previews/chooser.png') });
  }
  if (errors.length) throw new Error(errors.join('\n'));
} finally {
  await browser.close();
}

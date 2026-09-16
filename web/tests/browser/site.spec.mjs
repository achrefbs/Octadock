import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const pages = [
  ['landing', '/index.html'],
  ['privacy', '/privacy.html'],
  ['license', '/license.html'],
  ['terms', '/terms.html'],
  ['download', '/download.html'],
  ['release notes', '/release.html'],
  ['unsubscribe', '/unsubscribe.html'],
];

function formatViolations(violations) {
  return violations.map((violation) => {
    const nodes = violation.nodes
      .map((node) => '  ' + node.target.join(' ') + ': ' + node.failureSummary)
      .join('\n');
    return violation.id + ' (' + (violation.impact ?? 'unknown') + '): ' + violation.help + '\n' + nodes;
  }).join('\n\n');
}

function watchRuntime(page) {
  const problems = [];
  page.on('pageerror', (error) => problems.push('pageerror: ' + error.message));
  page.on('console', (message) => {
    if (message.type() === 'error') problems.push('console: ' + message.text());
  });
  page.on('requestfailed', (request) => {
    problems.push('request failed: ' + request.url() + ' (' + request.failure()?.errorText + ')');
  });
  page.on('response', (response) => {
    if (response.status() >= 400) problems.push('HTTP ' + response.status() + ': ' + response.url());
  });
  return problems;
}

test.describe('static fallback without JavaScript', () => {
  test.use({ javaScriptEnabled: false });

  for (const [name, route] of pages) {
    test(name + ' keeps its primary content visible', async ({ page }) => {
      const response = await page.goto(route, { waitUntil: 'load' });
      expect(response?.ok()).toBeTruthy();
      await page.waitForTimeout(600);

      await expect(page.locator('main')).toBeVisible();
      await expect(page.locator('h1')).toBeVisible();
      await expect(page.locator('.skip-link')).toHaveAttribute('href', '#main');

      const hiddenEnhancements = await page.locator('.rev, .reveal, [data-reveal]').evaluateAll((elements) => elements
        .filter((element) => {
          const style = getComputedStyle(element);
          return style.display === 'none'
            || style.visibility === 'hidden'
            || Number.parseFloat(style.opacity) < 0.99;
        })
        .map((element) => element.outerHTML.slice(0, 120)));
      expect(hiddenEnhancements).toEqual([]);

      const overflows = await page.evaluate(
        () => document.documentElement.scrollWidth > window.innerWidth + 1,
      );
      expect(overflows).toBe(false);
    });
  }
});

test.describe('WCAG AA automation', () => {
  test.use({ reducedMotion: 'reduce' });

  for (const [name, route] of pages) {
    test(name + ' has no automated WCAG A/AA violations', async ({ page }) => {
      const problems = watchRuntime(page);
      await page.goto(route, { waitUntil: 'load' });
      await page.waitForTimeout(150);
      const results = await new AxeBuilder({ page })
        // Large low-contrast atmosphere words are explicitly aria-hidden and
        // intentionally decorative; WCAG 1.4.3 does not apply to them.
        .exclude('.workflow-ghost')
        .exclude('.final-ghost')
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        .analyze();

      expect(results.violations, formatViolations(results.violations)).toEqual([]);
      expect(problems).toEqual([]);
    });
  }
});

test('latest product landing loads screenshots and its interactive capture demo', async ({
  page,
  baseURL,
}) => {
  const problems = watchRuntime(page);
  const externalRequests = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (
      ['http:', 'https:'].includes(url.protocol)
      && url.origin !== new URL(baseURL).origin
    ) {
      externalRequests.push(request.url());
    }
  });

  await page.goto('/index.html', { waitUntil: 'load' });
  await expect(page.locator('h1')).toContainText('Grab anything on your screen.');
  await expect(page.locator('h1 em')).toHaveText('It stays yours.');
  const brokenImages = await page.locator('img').evaluateAll(images => images
    .filter(image => !image.complete || image.naturalWidth === 0).map(image => image.src));
  expect(brokenImages).toEqual([]);
  await expect(page.locator('#dock .dock-actions')).toBeHidden();
  await page.locator('#dock').hover();
  await expect(page.locator('#dock .dock-actions')).toBeVisible();
  await page.mouse.move(10, 10);
  await expect(page.locator('#dock .dock-actions')).toBeHidden();
  await page.locator('#dock').hover();
  await page.getByRole('button', { name: 'Capture window', exact: true }).click();
  await expect(page.locator('#shelfList .tile')).toHaveCount(1);
  await expect(page.locator('#hint')).toContainText('Window captured');
  await expect(page.locator('#shelf')).toHaveCSS('transform', 'none');
  const tile = await page.locator('#shelfList .tile').boundingBox();
  const edge = await page.locator('.shelf-edge').boundingBox();
  expect(tile.x + tile.width).toBeLessThan(220);
  expect(tile.x - (edge.x + edge.width)).toBeGreaterThanOrEqual(16);
  await page.locator('#dock').hover();
  await page.locator('#moreBtn').click();
  await expect(page.getByRole('menu', { name: 'More' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('menu', { name: 'More' })).toBeHidden();
  await page.waitForLoadState('networkidle');
  await page.getByRole('link', { name: 'Read the MIT license' }).click();
  await expect(page).toHaveURL(/\/license\.html$/);
  await expect(page.getByRole('heading', { name: 'MIT license', exact: true })).toBeVisible();

  expect(externalRequests).toEqual([]);
  expect(problems).toEqual([]);
});

test('download navigation reaches installation instructions and a versioned release', async ({ page }) => {
  await page.route('**/downloads/*-Setup.exe', route => route.fulfill({
    status: 200, contentType: 'application/octet-stream',
    headers: { 'Content-Disposition': 'attachment; filename="Octadock-test-Setup.exe"' },
    body: 'installer download fixture',
  }));
  await page.goto('/index.html');
  await page.locator('.head-r').getByRole('link', { name: 'Download', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.locator('[name="consent"]')).not.toBeChecked();
  const downloaded = page.waitForEvent('download');
  await page.getByRole('link', { name: 'Download without email', exact: true }).click();
  await downloaded;
  await expect(page).toHaveURL(/\/download\.html$/);
  await expect(page.getByRole('heading', { name: 'Download Octadock', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Download Windows installer' })).toHaveAttribute('href', 'https://octadock.com/downloads/Octadock-0.3.0-alpha.2-Setup.exe');
  await expect(page.getByRole('heading', { name: 'Verify your download' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'SHA256SUMS.txt' })).toBeVisible();
});

test('collapsed dock opens from the keyboard and stays open while its controls have focus', async ({ page }) => {
  await page.goto('/index.html');
  await page.locator('.hero-source').focus();
  await page.keyboard.press('Tab');
  await expect(page.locator('#dock')).toBeFocused();
  await expect(page.locator('#dock .dock-actions')).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('button', { name: 'Capture area', exact: true })).toBeFocused();
  await page.mouse.move(10, 10);
  await page.waitForTimeout(500);
  await expect(page.locator('#dock .dock-actions')).toBeVisible();
});

test('touch users can expand the collapsed dock and capture a sample', async ({ browser, baseURL }) => {
  const context = await browser.newContext({ baseURL, hasTouch: true, isMobile: true, viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto('/index.html');
  await expect(page.locator('#dock .dock-actions')).toBeHidden();
  await page.locator('#dock').tap();
  await expect(page.locator('#dock .dock-actions')).toBeVisible();
  await page.getByRole('button', { name: 'Capture full screen', exact: true }).tap();
  await expect(page.locator('#shelfList .tile')).toHaveCount(1);
  const tile = await page.locator('#shelfList .tile').boundingBox();
  expect(tile.x).toBeLessThan(24);
  await context.close();
});

test('reduced motion resolves the landing content immediately', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/index.html', { waitUntil: 'load' });
  const hiddenReveals = await page.locator('[data-reveal]').evaluateAll((elements) => elements
    .filter((element) => {
      const style = getComputedStyle(element);
      return style.visibility === 'hidden'
        || Number.parseFloat(style.opacity) < 0.99
        || style.transform !== 'none';
    })
    .length);
  expect(hiddenReveals).toBe(0);
});

test('the product page and capture demo work without WebGL', async ({ page }) => {
  const problems = watchRuntime(page);
  await page.addInitScript(() => {
    const getContext = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function fallbackContext(type, ...args) {
      if (type === 'webgl' || type === 'webgl2') return null;
      return getContext.call(this, type, ...args);
    };
  });

  await page.goto('/index.html', { waitUntil: 'load' });
  await expect(page.locator('h1')).toContainText('Grab anything on your screen.');
  await page.locator('#dock').hover();
  await page.getByRole('button', { name: 'Capture full screen', exact: true }).click();
  await expect(page.locator('#shelfList .tile')).toHaveCount(1);
  expect(problems).toEqual([]);
});

test('dragging a sample area adds a capture to the illustrated shelf', async ({ page }) => {
  await page.goto('/index.html');
  await page.locator('#demo').scrollIntoViewIfNeeded();
  const bounds = await page.locator('#demo').boundingBox();
  await page.mouse.move(bounds.x + 160, bounds.y + 100);
  await page.mouse.down();
  await page.mouse.move(bounds.x + 410, bounds.y + 280, { steps: 12 });
  await page.mouse.up();
  await expect(page.locator('#shelfList .tile')).toHaveCount(1);
  await expect(page.locator('#hint')).toContainText('Area captured');
});

test.describe('narrow viewport fallback', () => {
  test.use({ javaScriptEnabled: false, viewport: { width: 390, height: 844 } });

  for (const [name, route] of pages) {
    test(name + ' avoids horizontal overflow at 390px', async ({ page }) => {
      await page.goto(route, { waitUntil: 'load' });
      await page.waitForTimeout(600);
      await expect(page.locator('h1')).toBeVisible();
      const geometry = await page.evaluate(() => ({
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
      }));
      expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth + 1);
    });
  }
});

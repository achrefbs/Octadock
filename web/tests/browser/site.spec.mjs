import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const pages = [
  ['landing', '/index.html'],
  ['privacy', '/privacy.html'],
  ['license', '/license.html'],
  ['terms', '/terms.html'],
  ['download', '/download.html'],
  ['release notes', '/release.html'],
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

      const hiddenEnhancements = await page.locator('.rev, .reveal').evaluateAll((elements) => elements
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
    });
  }
});

test('landing page loads its local runtime and copy interaction in Chromium', async ({
  page,
  context,
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

  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: baseURL });
  await page.goto('/index.html', { waitUntil: 'load' });

  await expect(page.locator('h1')).toContainText('Grab anything.');
  await page.waitForFunction(
    () => document.body.classList.contains('no-octo') || window.__octoLive === true,
    null,
    { timeout: 15_000 },
  );
  await page.locator('#copy-commands').click();
  await expect(page.locator('#copy-status')).toHaveText('Commands copied to the clipboard.');

  // Let the decorative module requests settle before navigating away. A normal
  // navigation aborts pending loads on slower hosted connections.
  await page.waitForLoadState('networkidle');

  await page.getByRole('link', { name: 'Open-source license' }).click();
  await expect(page).toHaveURL(/\/license\.html$/);
  await expect(page.locator('h1')).toContainText('MIT license');

  expect(externalRequests).toEqual([]);
  expect(problems).toEqual([]);
});

test('download navigation reaches installation instructions and a versioned release', async ({ page }) => {
  await page.goto('/index.html');
  await page.locator('.nav__cta').click();
  await expect(page).toHaveURL(/\/download\.html$/);
  await expect(page.getByRole('heading', { name: 'Download Octadock', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Download Windows ZIP' })).toHaveAttribute('href', 'https://octadock-production.up.railway.app/downloads/Octadock-0.3.0-alpha.2-windows.zip');
  await expect(page.getByRole('heading', { name: 'Verify your download' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'SHA256SUMS.txt' })).toBeVisible();
});

test('reduced motion resolves the landing content immediately', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/index.html?freeze=0.5', { waitUntil: 'load' });

  await expect(page.locator('.scrollcue')).toBeHidden();
  const hiddenReveals = await page.locator('.rev').evaluateAll((elements) => elements
    .filter((element) => {
      const style = getComputedStyle(element);
      return style.visibility === 'hidden'
        || Number.parseFloat(style.opacity) < 0.99
        || style.transform !== 'none';
    })
    .length);
  expect(hiddenReveals).toBe(0);
});

test('WebGL-unavailable browsers receive the static water fallback', async ({ page }) => {
  const problems = watchRuntime(page);
  await page.addInitScript(() => {
    const getContext = HTMLCanvasElement.prototype.getContext;
    HTMLCanvasElement.prototype.getContext = function fallbackContext(type, ...args) {
      if (type === 'webgl' || type === 'webgl2') return null;
      return getContext.call(this, type, ...args);
    };
  });

  await page.goto('/index.html', { waitUntil: 'load' });
  await expect(page.locator('body')).toHaveClass(/no-octo/);
  await expect(page.locator('.stage')).toBeHidden();
  await expect(page.locator('h1')).toContainText('Grab anything.');
  expect(problems).toEqual([]);
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

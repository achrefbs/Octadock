import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

for (const name of ['air', 'studio', 'nocturne']) {
  test(`${name} has working capture, history, markup and preview information`, async ({ page, context }) => {
    const errors = [];
    const external = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => {
      if (!request.url().startsWith('http://127.0.0.1:4173/')) external.push(request.url());
    });
    await page.goto(`/concepts/${name}.html`);
    await expect(page.locator('h1')).toBeVisible();
    await page.getByRole('button', { name: 'Window', exact: true }).click();
    await expect(page.locator('[data-demo]')).toHaveAttribute('data-capture-mode', 'window');
    await page.getByRole('button', { name: 'Capture sample', exact: true }).click();
    await expect(page.locator('[data-history] button')).toHaveCount(4);
    await expect(page.locator('[data-demo-status]')).toContainText('Window captured');
    await page.getByRole('button', { name: 'Toggle markup', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Toggle markup', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('.markup')).toHaveCSS('opacity', '1');
    await page.getByRole('button', { name: 'Select forest capture', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Select forest capture', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await context.grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.getByRole('button', { name: 'Copy sample text', exact: true }).click();
    await expect(page.locator('[data-demo-status]')).toContainText('copied to your clipboard');
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('A moment worth keeping. Captured with Octadock.');
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: 'Save sample image', exact: true }).click(),
    ]);
    expect(download.suggestedFilename()).toBe('octadock-sample-landscape.svg');
    await page.getByRole('button', { name: 'Windows preview ↗', exact: true }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await expect(page.getByRole('dialog')).toContainText('unsigned');
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).not.toBeVisible();
    expect(errors).toEqual([]);
    expect(external).toEqual([]);
  });

  test(`${name} fits narrow screens and passes automated accessibility checks`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(`/concepts/${name}.html`);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await expect(page.getByRole('button', { name: 'Capture sample', exact: true })).toBeVisible();
    const scan = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(scan.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
  });
}

test('chooser opens all directions and records a local preference', async ({ page }) => {
  await page.goto('/concepts/');
  await expect(page.getByRole('link', { name: 'Open Air design' })).toHaveAttribute('href', 'air.html');
  await expect(page.getByRole('link', { name: 'Open Studio design' })).toHaveAttribute('href', 'studio.html');
  await expect(page.getByRole('link', { name: 'Open Nocturne design' })).toHaveAttribute('href', 'nocturne.html');
  await page.getByRole('button', { name: 'Choose Air', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Choose Air', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.reload();
  await expect(page.getByRole('button', { name: 'Choose Air', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  const scan = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(scan.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
});

test('all design options fit a 320px short phone and keep dialogs reachable', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 480 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  for (const name of ['index', 'air', 'studio', 'nocturne']) {
    await page.goto(`/concepts/${name}.html`);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name} must not overflow horizontally`).toBe(true);
    if (name === 'index') {
      await page.getByRole('button', { name: 'Choose Nocturne', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Choose Nocturne', exact: true })).toHaveAttribute('aria-pressed', 'true');
      continue;
    }
    await page.getByRole('button', { name: 'Capture sample', exact: true }).click();
    await expect(page.locator('[data-history] button')).toHaveCount(4);
    await page.getByRole('button', { name: 'Windows preview ↗', exact: true }).click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('button', { name: 'Close preview information', exact: true }).click();
    await expect(page.getByRole('dialog')).not.toBeVisible();
  }
});

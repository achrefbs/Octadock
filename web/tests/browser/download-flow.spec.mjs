import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

async function openDialog(page) {
  await page.goto('/index.html');
  await page.locator('.head-r [data-download]').click();
  await expect(page.getByRole('dialog')).toBeVisible();
}
async function stubInstaller(page) {
  await page.route('**/downloads/*-Setup.exe', route => route.fulfill({ status: 200,
    contentType: 'application/octet-stream', headers: { 'Content-Disposition': 'attachment; filename="Octadock-test-Setup.exe"' }, body: 'fixture' }));
}

test('the hero is the demo and the second section uses one real screenshot', async ({ page }) => {
  await page.goto('/index.html');
  await expect(page.locator('#demo h1')).toBeVisible();
  await expect(page.locator('#demo [data-download]')).toBeVisible();
  await expect(page.locator('#capture img')).toHaveCount(1);
  await expect(page.locator('#capture')).not.toContainText(/Field notes/i);
  await expect(page.locator('.demo-disclosure')).toContainText('only captures this page');
});

test('signup needs explicit consent, supports Escape and restores focus', async ({ page }) => {
  let submissions = 0;
  await page.route('**/api/subscribe', route => { submissions++; return route.fulfill({ status: 202, json: {} }); });
  await openDialog(page);
  await page.getByLabel('Email address', { exact: true }).fill('reader@example.com');
  await expect(page.locator('[name=consent]')).not.toBeChecked();
  await page.getByRole('button', { name: 'Subscribe & download', exact: true }).click();
  expect(submissions).toBe(0);
  await expect(page.getByRole('dialog')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.locator('.head-r [data-download]')).toBeFocused();
});

test('skipping email never submits the form and downloads the installer', async ({ page }) => {
  await stubInstaller(page);
  let submissions = 0;
  await page.route('**/api/subscribe', route => { submissions++; return route.fulfill({ status: 503, json: {} }); });
  await openDialog(page);
  await page.getByLabel('Email address', { exact: true }).fill('not-submitted@example.com');
  const download = page.waitForEvent('download');
  await page.getByRole('link', { name: 'Download without email', exact: true }).click();
  expect((await download).suggestedFilename()).toBe('Octadock-test-Setup.exe');
  expect(submissions).toBe(0);
  await expect(page).toHaveURL(/download\.html$/);
});

test('signup failure stays honest and leaves the no-email download available', async ({ page }) => {
  await page.route('**/api/subscribe', route => route.fulfill({ status: 503, json: {} }));
  await openDialog(page);
  await page.getByLabel('Email address', { exact: true }).fill('reader@example.com');
  await page.locator('[name=consent]').check();
  await page.getByRole('button', { name: 'Subscribe & download', exact: true }).click();
  await expect(page.locator('#signup-status')).toContainText('could not save');
  await expect(page.getByRole('link', { name: 'Download without email', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Subscribe & download', exact: true })).toBeEnabled();
});

test('successful consent posts only the signup data and starts the same installer', async ({ page }) => {
  await stubInstaller(page);
  let payload;
  await page.route('**/api/subscribe', route => {
    payload = route.request().postDataJSON();
    return route.fulfill({ status: 202, json: { message: 'Your signup request was received.' } });
  });
  await openDialog(page);
  await page.getByLabel('Email address', { exact: true }).fill('reader@example.com');
  await page.locator('[name=consent]').check();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Subscribe & download', exact: true }).click();
  await download;
  expect(payload).toEqual({ email: 'reader@example.com', consent: true, website: '', consentVersion: 'octadock-marketing-v1' });
  await expect(page).toHaveURL(/download\.html$/);
  await expect(page.locator('#download-status')).toContainText('request was received');
});

test('the download dialog fits a short phone and passes accessibility checks', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 });
  await openDialog(page);
  await expect(page.getByRole('link', { name: 'Download without email', exact: true })).toBeVisible();
  const box = await page.getByRole('dialog').boundingBox();
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.width).toBeLessThanOrEqual(320);
  expect(box.height).toBeLessThanOrEqual(568);
  const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  expect(audit.violations).toEqual([]);
});

test('without JavaScript the ordinary installer and license links still work', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto((process.env.OCTADOCK_SITE_URL ?? 'http://127.0.0.1:4173') + '/download.html');
  await expect(page.locator('#installer-download')).toHaveAttribute('href', /-Setup\.exe$/);
  await expect(page.getByRole('dialog')).toBeHidden();
  await page.getByRole('navigation', { name: 'Legal', exact: true }).getByRole('link', { name: 'MIT license' }).click();
  await expect(page.locator('.license-text')).toContainText('Copyright (c) 2026 Achref Boularess');
  await context.close();
});

test('unsubscribe requires an intentional click and removes the token from the URL', async ({ page }) => {
  const token = 'a'.repeat(64) + '.' + 'b'.repeat(64);
  let submissions = 0;
  await page.route('**/api/unsubscribe', route => {
    submissions++;
    expect(route.request().postDataJSON()).toEqual({ token });
    return route.fulfill({ status: 200, json: { message: 'You are unsubscribed.' } });
  });
  await page.goto('/unsubscribe.html#' + token);
  expect(submissions).toBe(0);
  await expect(page).toHaveURL(/unsubscribe\.html$/);
  await page.getByRole('button', { name: 'Unsubscribe from Octadock emails' }).click();
  await expect(page.locator('#unsubscribe-status')).toContainText('You are unsubscribed');
  expect(submissions).toBe(1);
});

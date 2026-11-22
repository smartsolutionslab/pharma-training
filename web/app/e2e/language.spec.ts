import { expect, type Page, test } from '@playwright/test';

// The app starts in the browser's language if it is German or English, and in German otherwise; the user menu switches
// it, and the choice holds across a reload. playwright.config.ts gives the browser German.
async function signIn(page: Page) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
}

test('the user switches to English, keeps it across a reload, and switches back', async ({ page }) => {
  await signIn(page);
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
  const html = page.locator('html');
  await expect(html).toHaveAttribute('lang', 'de-DE');

  await page.getByRole('group', { name: 'Sprache' }).getByRole('button', { name: 'English' }).click();

  await expect(page.getByText('Signed in as developer')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();
  await expect(html).toHaveAttribute('lang', 'en-GB');
  await expect(page.getByRole('group', { name: 'Language' }).getByRole('button', { name: 'English' })).toHaveAttribute(
    'aria-pressed',
    'true',
  );

  await page.reload();

  await expect(page.getByText('Signed in as developer')).toBeVisible();
  await expect(html).toHaveAttribute('lang', 'en-GB');

  await page.getByRole('group', { name: 'Language' }).getByRole('button', { name: 'Deutsch' }).click();

  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Abmelden' })).toBeVisible();
  await expect(html).toHaveAttribute('lang', 'de-DE');
});

test.describe('in a browser that prefers American English', () => {
  test.use({ locale: 'en-US' });

  test('the app starts in English', async ({ page }) => {
    await signIn(page);

    await expect(page.getByText('Signed in as developer')).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'en-GB');
  });
});

test.describe('in a browser that prefers French', () => {
  test.use({ locale: 'fr-FR' });

  test('the app starts in German', async ({ page }) => {
    await signIn(page);

    await expect(page.getByText('Angemeldet als developer')).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', 'de-DE');
  });
});

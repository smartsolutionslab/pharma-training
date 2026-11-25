import { expect, test } from '@playwright/test';
import { signIn } from './signIn';

test('a realm user signs in and sees the application', async ({ page }) => {
  await page.goto('/');

  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();

  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'pharma-training' })).toBeVisible();
});

test('a first answer of /me that fails is asked again, and the user is signed in', async ({ page }) => {
  let failed = 0;
  await page.route(/\/api\/me(\?|$)/, async (route) => {
    if (failed === 0) {
      failed += 1;
      await route.fulfill({ status: 503, json: { title: 'Service Unavailable' } });
      return;
    }
    await route.continue();
  });

  await signIn(page);

  expect(failed).toBe(1);
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
});

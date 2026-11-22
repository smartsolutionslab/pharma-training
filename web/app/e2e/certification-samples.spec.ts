import { expect, type Page, test } from '@playwright/test';
import { memberOfOnePharmacy } from './onePharmacy';

/** The Certification page, as a signed-in member of a pharmacy, who may manage it, opens it. */
async function openCertification(page: Page) {
  await memberOfOnePharmacy(page);
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
  await page.goto('/certification');
  await expect(page.getByRole('heading', { name: 'Samples' })).toBeVisible();
}

test('a user creates a certification sample and finds it in the list, after a reload too', async ({ page }) => {
  const name = `E2E ${Date.now()}`;
  await openCertification(page);

  await page.getByRole('textbox', { name: 'Name' }).fill(name);
  await page.getByRole('button', { name: 'Create sample' }).click();

  await expect(page.getByRole('status').filter({ hasText: `The sample ${name} was created.` })).toBeVisible();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
});

test('a blank name is refused on the certification page and nothing is created', async ({ page }) => {
  await openCertification(page);

  await page.getByRole('button', { name: 'Create sample' }).click();

  await expect(page.getByRole('alert').filter({ hasText: 'Give the sample a name.' })).toBeVisible();
});

test('without samples the certification page says so and its action leads to the form', async ({ page }) => {
  // The host's list answers empty, whatever samples other tests created.
  await page.route(/\/api\/certification\/samples\?/, (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }),
  );
  await openCertification(page);

  await expect(page.getByText('No samples yet.')).toBeVisible();
  await page.getByRole('button', { name: 'Create a sample' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toBeFocused();
});

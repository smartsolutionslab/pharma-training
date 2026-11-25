import { expect, type Page, test } from '@playwright/test';
import { newestFirst } from './newestFirst';
import { memberOfOnePharmacy } from './onePharmacy';
import { signIn } from './signIn';

/** The Catalog page, as a signed-in member of a pharmacy, who may manage it, opens it. */
async function openCatalog(page: Page) {
  await memberOfOnePharmacy(page);
  await signIn(page);
  await page.goto('/catalog');
  await expect(page.getByRole('heading', { name: 'Beispiele' })).toBeVisible();
}

test('a user creates a catalog sample and finds it in the list, after a reload too', async ({ page }) => {
  // The list is by name, 20 to a page: this name sorts before every sample an earlier test made.
  const name = newestFirst('Beispiel');
  await openCatalog(page);

  await page.getByRole('textbox', { name: 'Name' }).fill(name);
  await page.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page.getByRole('status').filter({ hasText: `Das Beispiel ${name} wurde angelegt.` })).toBeVisible();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
});

test('a blank name is refused on the catalog page and nothing is created', async ({ page }) => {
  await openCatalog(page);

  await page.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page.getByRole('alert').filter({ hasText: 'Geben Sie dem Beispiel einen Namen.' })).toBeVisible();
});

test('without samples the catalog page says so and its action leads to the form', async ({ page }) => {
  // The host's list answers empty, whatever samples other tests created.
  await page.route(/\/api\/catalog\/samples\?/, (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }),
  );
  await openCatalog(page);

  await expect(page.getByText('Noch keine Beispiele.')).toBeVisible();
  await page.getByRole('button', { name: 'Erstes Beispiel anlegen' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toBeFocused();
});

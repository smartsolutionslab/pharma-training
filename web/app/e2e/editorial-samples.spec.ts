import { expect, type Page, test } from '@playwright/test';

/** The Editorial page, as a signed-in editor (the realm's local user `editor`, of the Redaktion) opens it. */
async function openEditorial(page: Page) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('editor');
  await page.getByLabel('Password', { exact: true }).fill('editor');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als editor')).toBeVisible();
  await page.goto('/editorial');
  await expect(page.getByRole('heading', { name: 'Beispiele' })).toBeVisible();
}

test('a user creates a editorial sample and finds it in the list, after a reload too', async ({ page }) => {
  const name = `E2E ${Date.now()}`;
  await openEditorial(page);

  await page.getByRole('textbox', { name: 'Name' }).fill(name);
  await page.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page.getByRole('status').filter({ hasText: `Das Beispiel ${name} wurde angelegt.` })).toBeVisible();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText(name, { exact: true })).toBeVisible();
});

test('a blank name is refused on the editorial page and nothing is created', async ({ page }) => {
  await openEditorial(page);

  await page.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page.getByRole('alert').filter({ hasText: 'Geben Sie dem Beispiel einen Namen.' })).toBeVisible();
});

test('without samples the editorial page says so and its action leads to the form', async ({ page }) => {
  // The host's list answers empty, whatever samples other tests created.
  await page.route(/\/api\/editorial\/samples\?/, (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }),
  );
  await openEditorial(page);

  await expect(page.getByText('Noch keine Beispiele.')).toBeVisible();
  await page.getByRole('button', { name: 'Erstes Beispiel anlegen' }).click();
  await expect(page.getByRole('textbox', { name: 'Name' })).toBeFocused();
});

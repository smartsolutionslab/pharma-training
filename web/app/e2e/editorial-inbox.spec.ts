import { expect, type Page, test } from '@playwright/test';

/** The editors' inbox, as the realm's local user `editor` (realm role editor) opens it. Keycloak's login is English. */
async function openInbox(page: Page) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('editor');
  await page.getByLabel('Password', { exact: true }).fill('editor');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als editor')).toBeVisible();
  await page.goto('/editorial');
  await expect(page.getByRole('heading', { name: 'Redaktion', level: 1 })).toBeVisible();
}

test('an editor finds the inbox with its three statuses, the drafts chosen', async ({ page }) => {
  await openInbox(page);

  await expect(page.getByRole('heading', { name: 'Eingang', level: 2 })).toBeVisible();
  const statuses = page.getByRole('tablist', { name: 'Status' });
  await expect(statuses.getByRole('tab')).toHaveText(['Entwurf', 'In Prüfung', 'Veröffentlicht']);
  await expect(statuses.getByRole('tab', { name: 'Entwurf' })).toHaveAttribute('aria-selected', 'true');
  // The generated sample is gone from the editorial page.
  await expect(page.getByRole('heading', { name: 'Beispiele' })).toHaveCount(0);
});

test('an empty Published tab says there are no published versions', async ({ page }) => {
  // The host's published list answers empty, whatever other runs published.
  await page.route(/\/api\/editorial\/course-versions\?(.*&)?status=Published(&|$)/, (route) =>
    route.fulfill({ json: { items: [], totalCount: 0, page: 1, pageSize: 20 } }),
  );
  await openInbox(page);

  await page.getByRole('tablist', { name: 'Status' }).getByRole('tab', { name: 'Veröffentlicht' }).click();

  await expect(page).toHaveURL(/[?&]status=Published(&|$)/);
  await expect(page.getByText('Keine veröffentlichten Versionen.')).toBeVisible();
});

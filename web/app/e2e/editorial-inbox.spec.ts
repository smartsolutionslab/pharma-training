import { expect, type Page, test } from '@playwright/test';
import { signIn } from './signIn';

/** The editors' inbox, as the realm's local user `editor` (realm role editor) opens it. Keycloak's login is English. */
async function openInbox(page: Page) {
  await signIn(page, 'editor');
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

/**
 * Expects a version in the inbox's current tab, on whichever page the host's title order puts it: earlier runs left
 * drafts of their own, so it need not be on the first.
 */
async function expectInInbox(page: Page, title: string) {
  const link = page.getByRole('link', { name: title, exact: true });
  const next = page.getByRole('button', { name: 'Nächste Seite' });
  for (let shown = 1; shown <= 50; shown++) {
    // The page's rows are there once the table says which of how many it shows.
    await expect(page.getByText(new RegExp(`^Einträge ${(shown - 1) * 20 + 1}–`))).toBeVisible();
    if (await link.isVisible()) {
      return;
    }
    if ((await next.count()) === 0 || (await next.isDisabled())) {
      break;
    }
    await next.click();
  }
  await expect(link).toBeVisible();
}

test('an editor starts a course, lands on its first version, and finds it under Entwurf', async ({ page }) => {
  const title = `E2E ${Date.now()}`;
  await openInbox(page);

  await page.getByRole('button', { name: 'Neue Schulung' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neue Schulung anlegen' });
  await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(page.getByRole('status').filter({ hasText: `Die Schulung ${title} wurde angelegt.` })).toBeVisible();
  await expect(page).toHaveURL(/\/editorial\/versions\/[0-9a-f-]{36}$/);
  // The version's page (T6): its title, its number and its status.
  await expect(page.getByRole('heading', { name: title, level: 1 })).toBeVisible();
  await expect(page.getByText('Version 1', { exact: true })).toBeVisible();
  await expect(page.getByText('Entwurf', { exact: true })).toBeVisible();

  await page.goBack();

  await expect(page).toHaveURL(/\/editorial(\?.*)?$/);
  await expect(page.getByRole('tablist', { name: 'Status' }).getByRole('tab', { name: 'Entwurf' })).toHaveAttribute(
    'aria-selected',
    'true',
  );
  await expectInInbox(page, title);
});

test('a blank title is refused in the dialog and no course is started', async ({ page }) => {
  await openInbox(page);

  await page.getByRole('button', { name: 'Neue Schulung' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neue Schulung anlegen' });
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();

  await expect(dialog.getByRole('alert').filter({ hasText: 'Geben Sie der Schulung einen Titel.' })).toBeVisible();
  await expect(page).toHaveURL(/\/editorial(\?.*)?$/);
});

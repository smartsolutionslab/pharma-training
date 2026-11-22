import { expect, type Page, test } from '@playwright/test';

// No route is mocked: the host registers the pharmacy and answers the developer's memberships. Each run adds one, so
// the developer may already be a member of none, one or several pharmacies; the test finds its own by a unique name.

/** Signs in as the local user developer, on the start page. */
async function signIn(page: Page) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
}

test('a signed-in user registers a pharmacy and works in it, after a reload too', async ({ page }) => {
  const name = `E2E Apotheke ${Date.now()}`;
  await signIn(page);
  // Exactly: the start page's region "Noch keine Apotheke" holds the word too, and a link of the same name.
  const pharmacy = page.getByRole('region', { name: 'Apotheke', exact: true });

  await pharmacy.getByRole('link', { name: 'Apotheke registrieren' }).click();
  await expect(page).toHaveURL(/\/organisation\/register$/);
  await expect(page.getByRole('heading', { name: 'Register a pharmacy', level: 1 })).toBeVisible();
  await page.getByRole('textbox', { name: 'Pharmacy name' }).fill(name);
  await page.getByRole('combobox', { name: 'Your job function' }).selectOption({ label: 'Pharmacist' });
  await page.getByRole('button', { name: 'Register pharmacy' }).click();

  await expect(page.getByRole('status').filter({ hasText: `The pharmacy ${name} was registered.` })).toBeVisible();
  await expect(page).toHaveURL(/\/organisation$/);
  await expect(pharmacy.getByText(name, { exact: true })).toBeVisible();
  await page.reload();
  await expect(pharmacy.getByText(name, { exact: true })).toBeVisible();
});

test('an empty registration is refused at both fields and the user stays on the page', async ({ page }) => {
  await signIn(page);
  await page.goto('/organisation/register');

  await page.getByRole('button', { name: 'Register pharmacy' }).click();

  await expect(page.getByRole('alert').filter({ hasText: 'Give the pharmacy a name.' })).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Choose what you do in the pharmacy.' })).toBeVisible();
  await expect(page).toHaveURL(/\/organisation\/register$/);
});

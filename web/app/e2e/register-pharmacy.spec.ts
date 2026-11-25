import { expect, test } from '@playwright/test';
import { signIn } from './signIn';

// No route is mocked: the host registers the pharmacy and answers the developer's memberships. Each run adds one, so
// the developer may already be a member of none, one or several pharmacies; the test finds its own by a unique name.

/** Signs in as the local user developer, on the start page. */
test('a signed-in user registers a pharmacy and works in it, after a reload too', async ({ page }) => {
  const name = `E2E Apotheke ${Date.now()}`;
  await signIn(page);
  // Exactly: the start page's region "Noch keine Apotheke" holds the word too, and a link of the same name.
  const pharmacy = page.getByRole('region', { name: 'Apotheke', exact: true });

  await pharmacy.getByRole('link', { name: 'Apotheke registrieren' }).click();
  await expect(page).toHaveURL(/\/organisation\/register$/);
  await expect(page.getByRole('heading', { name: 'Apotheke registrieren', level: 1 })).toBeVisible();
  await page.getByRole('textbox', { name: 'Name der Apotheke' }).fill(name);
  await page.getByRole('combobox', { name: 'Ihre Tätigkeit' }).selectOption({ label: 'Apotheker' });
  await page.getByRole('button', { name: 'Registrieren', exact: true }).click();

  await expect(page.getByRole('status').filter({ hasText: `Die Apotheke ${name} wurde registriert.` })).toBeVisible();
  await expect(page).toHaveURL(/\/organisation$/);
  await expect(pharmacy.getByText(name, { exact: true })).toBeVisible();
  await page.reload();
  await expect(pharmacy.getByText(name, { exact: true })).toBeVisible();
});

test('an empty registration is refused at both fields and the user stays on the page', async ({ page }) => {
  await signIn(page);
  await page.goto('/organisation/register');

  await page.getByRole('button', { name: 'Registrieren', exact: true }).click();

  await expect(page.getByRole('alert').filter({ hasText: 'Geben Sie den Namen der Apotheke an.' })).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Wählen Sie Ihre Tätigkeit in der Apotheke.' })).toBeVisible();
  await expect(page).toHaveURL(/\/organisation\/register$/);
});

// A refusal is shown by its code in the catalog's words (#147, ADR 0007), not in the host's English fallback: the host
// refuses a name holding a direction override (U+202E), which the form lets through, and the toast says why in the
// language of the moment.
test('a name with a direction override is refused by its code, in German and then in English', async ({ page }) => {
  const name = `E2E ‮ Apotheke ${Date.now()}`;
  const hostFallback = 'Der Name einer Apotheke hat 1 bis 120 Zeichen und ist nicht leer.';
  await signIn(page);
  await page.goto('/organisation/register');
  await page.getByRole('textbox', { name: 'Name der Apotheke' }).fill(name);
  await page.getByRole('combobox', { name: 'Ihre Tätigkeit' }).selectOption({ label: 'Apotheker' });

  await page.getByRole('button', { name: 'Registrieren', exact: true }).click();

  await expect(
    page.getByRole('alert').filter({
      hasText: 'Der Name einer Apotheke hat 1 bis 120 Zeichen und enthält keine Steuer- oder Richtungszeichen.',
    }),
  ).toBeVisible();
  await expect(page.getByText(hostFallback)).toHaveCount(0);
  await expect(page).toHaveURL(/\/organisation\/register$/);

  await page.getByRole('group', { name: 'Sprache' }).getByRole('button', { name: 'English' }).click();
  await expect(page.getByRole('heading', { name: 'Register a pharmacy', level: 1 })).toBeVisible();
  await page.getByRole('button', { name: 'Register pharmacy', exact: true }).click();

  await expect(
    page.getByRole('alert').filter({
      hasText: "A pharmacy's name has 1 to 120 characters and contains no control or direction characters.",
    }),
  ).toBeVisible();
  await expect(page.getByText(hostFallback)).toHaveCount(0);
  await expect(page).toHaveURL(/\/organisation\/register$/);
});

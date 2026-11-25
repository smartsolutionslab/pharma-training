import { expect, type Page, test } from '@playwright/test';
import { accessToken, pharmacyName, registerPharmacy, type RegisteredPharmacy, signInTo } from './compliance';

// The pharmacy's retention period on the managers' export page (#419; ADR 0034, amended by ADR 0039) against the
// AppHost, with no route mocked. The developer registers a pharmacy, and so is its Manager, whose role holds the
// capability to manage the period; Certification learns of the membership from an event, so the period is asked through
// the API until it answers.
//
// Not covered: an employee's 403 (the realm has no second member of a fresh pharmacy; the component tests and #418's
// integration tests cover it), the export's content under a changed period (#418's tests), and two managers at once
// (the last write wins, #418).

const invalidText = 'Die Aufbewahrungsfrist ist eine ganze Zahl von 2 bis 30 Jahren.';

/** Asks the period as the developer until Certification knows the membership and answers 200: until then it answers 404. */
async function waitUntilCertificationKnows(page: Page, pharmacy: RegisteredPharmacy) {
  const authorization = await accessToken(page.request, 'developer');
  await expect
    .poll(
      async () => {
        const response = await page.request.get(`/api/certification/pharmacies/${pharmacy.id}/retention-period`, {
          headers: { Authorization: authorization },
        });
        return response.ok() ? 200 : `${response.status()}: ${await response.text()}`;
      },
      { message: `Certification knows the developer manages ${pharmacy.id}`, timeout: 30_000 },
    )
    .toBe(200);
}

/** Signs in to the pharmacy and opens the export from the team matrix's header. */
async function openExport(page: Page, name: string) {
  await signInTo(page, name);
  await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('link', { name: 'Team-Matrix' }).click();
  await page.getByRole('main').getByRole('link', { name: 'Nachweise exportieren' }).click();
  await expect(page).toHaveURL(/\/certification\/export$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Nachweise exportieren' })).toBeVisible();
}

const retention = (page: Page) => page.getByRole('region', { name: 'Aufbewahrungsfrist' });

test('a manager sees the default of five years, sets seven and still sees seven after a reload', async ({ page }) => {
  const name = pharmacyName('Aufbewahrung');
  const pharmacy = await registerPharmacy(page.request, name);
  await waitUntilCertificationKnows(page, pharmacy);

  await openExport(page, name);
  await expect(retention(page).getByRole('heading', { level: 2, name: 'Aufbewahrungsfrist' })).toBeVisible();
  await expect(page.getByLabel('Aufbewahrungsfrist in Jahren')).toHaveValue('5');
  await expect(retention(page)).toContainText('Derzeit 5 Jahre (Standard)');

  await page.getByLabel('Aufbewahrungsfrist in Jahren').fill('7');
  await page.getByRole('button', { name: 'Frist speichern' }).click();

  await expect(page.getByText('Die Aufbewahrungsfrist beträgt jetzt 7 Jahre.')).toBeVisible();
  await expect(retention(page)).toContainText('Derzeit 7 Jahre');
  await expect(retention(page)).not.toContainText('(Standard)');

  // The pharmacy stays current through localStorage.
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Nachweise exportieren' })).toBeVisible();
  await expect(page.getByLabel('Aufbewahrungsfrist in Jahren')).toHaveValue('7');
  await expect(retention(page)).toContainText('Derzeit 7 Jahre');
  await expect(retention(page)).not.toContainText('(Standard)');
});

test('a period of 31 years shows the field’s error and sends nothing', async ({ page }) => {
  const name = pharmacyName('Aufbewahrung Grenze');
  const pharmacy = await registerPharmacy(page.request, name);
  await waitUntilCertificationKnows(page, pharmacy);

  const saves: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'PUT' && request.url().includes('/retention-period')) {
      saves.push(request.url());
    }
  });

  await openExport(page, name);
  const field = page.getByLabel('Aufbewahrungsfrist in Jahren');
  await expect(field).toHaveValue('5');
  await field.fill('31');
  await page.getByRole('button', { name: 'Frist speichern' }).click();

  await expect(retention(page).getByRole('alert').filter({ hasText: invalidText })).toBeVisible();
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  await expect(field).toHaveAccessibleDescription(new RegExp(invalidText.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  await expect(field).toBeFocused();
  // The button is ready again: the form's check is done, and it sent nothing.
  await expect(page.getByRole('button', { name: 'Frist speichern' })).toBeEnabled();
  expect(saves).toEqual([]);
});

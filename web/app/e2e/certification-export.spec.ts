import { readFile } from 'node:fs/promises';
import { expect, type Page, test } from '@playwright/test';
import { seedCertificate } from './certificationDatabase';
import {
  accessToken,
  courseTitle,
  developer,
  pharmacyName,
  registerPharmacy,
  type RegisteredPharmacy,
  signInTo,
} from './compliance';
import { zipEntries } from './zipEntries';

// The managers' certificate export (#297, ADR 0032) against the AppHost, with no route mocked. The developer registers
// a pharmacy, and so is its Manager; Certification learns of the membership from an event, so the export is asked
// through the API until it answers. A certificate is seeded for the developer in Certification's database, issued after
// the membership began, with its PDF still pending: the export lists it in index.csv and holds no PDF for it. On the
// page the developer opens the export from the matrix, downloads the default range and reads the ZIP.
//
// Not covered: the PDF entries (#52's integration tests), export-too-large (the component test), and a run within a
// second of midnight in Germany, when the seeded issue day can fall outside the default range.

const csvHeader = 'Bestätigungs-Id;Name;Schulung;Version;Ausgestellt am;Gültig bis;Ergebnis (%);Prüfcode;PDF';
const periodOrder = 'Das Ende darf nicht vor dem Beginn liegen.';

/** The day in Germany (yyyy-mm-dd) of the instant. */
function dayInGermany(instant: Date) {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Berlin' }).format(instant);
}

/** The day (yyyy-mm-dd) the given number of days from the day, as a calendar date. */
function addDays(day: string, days: number) {
  const date = new Date(`${day}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

/** The page's default range: to is today in Germany, from a year before plus one day (29 Feb moving to 28 Feb). */
function defaultPeriod() {
  const to = dayInGermany(new Date());
  const [year = 0, month = 0, date = 0] = to.split('-').map(Number);
  const yearBefore = `${year - 1}-${String(month).padStart(2, '0')}-${String(month === 2 && date === 29 ? 28 : date).padStart(2, '0')}`;
  return { from: addDays(yearBefore, 1), to };
}

/**
 * Asks the export as the developer until Certification knows the membership and answers 200: until then it answers 404
 * pharmacy-not-found.
 */
async function waitUntilCertificationKnows(page: Page, pharmacy: RegisteredPharmacy) {
  const authorization = await accessToken(page.request, 'developer');
  const { from, to } = defaultPeriod();
  await expect
    .poll(
      async () => {
        const response = await page.request.get(`/api/certification/pharmacies/${pharmacy.id}/certificates/export`, {
          headers: { Authorization: authorization },
          params: { from, to },
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

test('a manager exports a range that holds a certificate and gets its row in index.csv', async ({ page }) => {
  const name = pharmacyName('Export');
  const course = courseTitle('Export');
  const pharmacy = await registerPharmacy(page.request, name);
  await waitUntilCertificationKnows(page, pharmacy);

  // After the membership began: Certification knows it by now, and the instant is a second ahead of this one.
  const issuedAt = new Date(Math.floor(Date.now() / 1000) * 1000 + 1000);
  const validUntil = addDays(dayInGermany(issuedAt), 365);
  const certificate = seedCertificate({
    holderId: pharmacy.developerPersonId,
    holderName: developer,
    courseTitle: course,
    issuedAt: issuedAt.toISOString().replace(/\.\d+Z$/, 'Z'),
    validUntil,
  });

  await openExport(page, name);
  const { from, to } = defaultPeriod();
  await expect(page.getByLabel('Ausgestellt ab')).toHaveValue(from);
  await expect(page.getByLabel('Ausgestellt bis')).toHaveValue(to);

  const [download] = await Promise.all([
    page.waitForEvent('download'),
    page.getByRole('button', { name: 'ZIP herunterladen' }).click(),
  ]);

  expect(download.suggestedFilename()).toBe(`teilnahmebestaetigungen_${from}_${to}.zip`);
  const archive = await readFile(await download.path());
  expect(archive.subarray(0, 4)).toEqual(Buffer.from([0x50, 0x4b, 0x03, 0x04]));

  const entries = zipEntries(archive);
  const [first, ...rest] = [...entries.keys()];
  expect(first).toBe('index.csv');
  for (const entry of rest) {
    expect(entry).toMatch(/^pdf\/[0-9a-f-]{36}\.pdf$/);
  }
  const csv = (entries.get('index.csv') ?? Buffer.alloc(0)).toString('utf8').replace(/^\uFEFF/, '');
  const rows = csv.split('\r\n');
  expect(rows[0]).toBe(csvHeader);
  const row = rows.find((line) => line.startsWith(`${certificate};`)) ?? '';
  expect(row.split(';')).toEqual([
    certificate,
    developer,
    course,
    '1',
    dayInGermany(issuedAt),
    validUntil,
    '100',
    expect.any(String),
    'noch nicht erstellt',
  ]);
  expect(entries.has(`pdf/${certificate}.pdf`)).toBe(false);

  await expect(page.getByText('Der Export wurde heruntergeladen.')).toBeVisible();
});

test('an end before the start shows the error at Bis, and nothing is asked of the host', async ({ page }) => {
  const name = pharmacyName('Export Zeitraum');
  await registerPharmacy(page.request, name);

  const exports: string[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/certificates/export')) {
      exports.push(request.url());
    }
  });
  let downloads = 0;
  page.on('download', () => {
    downloads += 1;
  });

  await openExport(page, name);
  const today = dayInGermany(new Date());
  await page.getByLabel('Ausgestellt ab').fill(today);
  await page.getByLabel('Ausgestellt bis').fill(addDays(today, -1));
  await page.getByRole('button', { name: 'ZIP herunterladen' }).click();

  const until = page.getByLabel('Ausgestellt bis');
  await expect(page.getByRole('alert').filter({ hasText: periodOrder })).toBeVisible();
  await expect(until).toHaveAttribute('aria-invalid', 'true');
  await expect(until).toHaveAccessibleDescription(periodOrder);
  // The button is ready again: the form's check is done, and it sent nothing.
  await expect(page.getByRole('button', { name: 'ZIP herunterladen' })).toBeEnabled();
  expect(exports).toEqual([]);
  expect(downloads).toBe(0);
});

import AxeBuilder from '@axe-core/playwright';
import { devices, expect, type Page, test } from '@playwright/test';
import { newVerificationCode, seedCertificate, setValidUntil, verificationCodeOf } from './certificationDatabase';

// The public verification page a certificate's QR code opens (#54, Z2), signed out in a fresh context, against the
// AppHost with no mocks. Until #46 merges, the certificates are seeded in Certification's database (T9 switches to
// issueCertificate then); the code is read from there, since no API the holder reads carries it.
//
// Budget: every verification request spends the per-address limit (30 a minute, 500 a day, ADR 0026), and locally every
// caller shares one partition, so the four tests send five requests in all: two in the first (light, then dark after a
// reload), one in each other. The page itself never retries and never refetches on focus. A local AppHost that has
// served 500 verifications in a day answers 429 until it restarts.

const holderName = 'Local Developer';
const runId = newVerificationCode().slice(0, 6);
const validCourse = `Hygiene in der Apotheke ${runId}`;
const expiredCourse = `Datenschutz ${runId}`;

const thisYear = new Date().getUTCFullYear();
const issuedOn = `${thisYear}-01-15`;
const validUntil = `${thisYear + 2}-01-15`;
const yesterday = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

let validCode = '';
let expiredCode = '';

/** A yyyy-mm-dd day as the German page shows it, 15.01.2026. */
function german(day: string) {
  const [year, month, date] = day.split('-');
  return `${date ?? ''}.${month ?? ''}.${year ?? ''}`;
}

/** A device's page options without its browser type, which a describe block cannot set. */
function pageOptionsOf(device: (typeof devices)[string]) {
  return {
    viewport: device.viewport,
    userAgent: device.userAgent,
    deviceScaleFactor: device.deviceScaleFactor,
    isMobile: device.isMobile,
    hasTouch: device.hasTouch,
  };
}

async function contrastViolations(page: Page) {
  const results = await new AxeBuilder({ page }).include('main').withRules(['color-contrast']).analyze();
  return results.violations;
}

test.beforeAll(() => {
  const valid = seedCertificate({
    holderId: crypto.randomUUID(),
    holderName,
    courseTitle: validCourse,
    issuedAt: `${issuedOn}T12:00:00Z`,
    validUntil,
  });
  validCode = verificationCodeOf(valid);

  const expired = seedCertificate({
    holderId: crypto.randomUUID(),
    holderName,
    courseTitle: expiredCourse,
    issuedAt: `${thisYear - 2}-01-15T12:00:00Z`,
    validUntil,
  });
  setValidUntil(expired, yesterday);
  expiredCode = verificationCodeOf(expired);
});

test.describe('on a phone', () => {
  test.use(pageOptionsOf(devices['Pixel 7']));

  test('a signed-out visitor sees the certificate is valid, on the app, without scrolling sideways, in both themes', async ({
    page,
    baseURL,
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(`/verify/${validCode}`);

    const main = page.getByRole('main');
    await expect(page.getByRole('heading', { level: 1, name: 'Bestätigung prüfen' })).toBeVisible();
    await expect(main.getByRole('heading', { level: 2, name: 'Gültig' })).toBeVisible();
    expect(new URL(page.url()).origin).toBe(new URL(baseURL ?? '').origin);
    expect(new URL(page.url()).pathname).toBe(`/verify/${validCode}`);
    await expect(main.getByText(holderName, { exact: true })).toBeVisible();
    await expect(main.getByText(validCourse, { exact: true })).toBeVisible();
    await expect(main.getByText(german(issuedOn), { exact: true })).toBeVisible();
    await expect(main.getByText(german(validUntil), { exact: true })).toBeVisible();

    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(
      false,
    );

    const html = page.locator('html');
    await expect(html).not.toHaveClass(/\bdark\b/);
    expect(await contrastViolations(page)).toEqual([]);

    await page.evaluate(() => {
      localStorage.setItem('theme', 'dark');
    });
    await page.reload();
    await expect(html).toHaveClass(/\bdark\b/);
    await expect(main.getByRole('heading', { level: 2, name: 'Gültig' })).toBeVisible();
    expect(new URL(page.url()).origin).toBe(new URL(baseURL ?? '').origin);
    expect(await contrastViolations(page)).toEqual([]);
  });
});

test('an expired certificate reads Abgelaufen', async ({ page }) => {
  await page.goto(`/verify/${expiredCode}`);

  const main = page.getByRole('main');
  await expect(main.getByRole('heading', { level: 2, name: 'Abgelaufen' })).toBeVisible();
  await expect(main.getByText(expiredCourse, { exact: true })).toBeVisible();
  await expect(main.getByText(german(yesterday), { exact: true })).toBeVisible();
});

test('an unknown code reads Nicht prüfbar', async ({ page }) => {
  await page.goto(`/verify/${newVerificationCode()}`);

  await expect(page.getByRole('main').getByRole('heading', { level: 2, name: 'Nicht prüfbar' })).toBeVisible();
});

test('English in the header switches the page to English', async ({ page }) => {
  await page.goto(`/verify/${validCode}`);
  await expect(page.getByRole('main').getByRole('heading', { level: 2, name: 'Gültig' })).toBeVisible();

  await page.getByRole('banner').getByRole('button', { name: 'English' }).click();

  await expect(page.getByRole('heading', { level: 1, name: 'Verify a certificate' })).toBeVisible();
  await expect(page.getByRole('main').getByRole('heading', { level: 2, name: 'Valid' })).toBeVisible();
});

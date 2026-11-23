import AxeBuilder from '@axe-core/playwright';
import { expect, type Locator, type Page, test } from '@playwright/test';
import {
  assignThroughApi,
  courseTitle,
  daysAhead,
  developer,
  germanDate,
  pharmacyName,
  publishCourse,
  registerPharmacy,
  type RegisteredPharmacy,
  signInTo,
} from './compliance';

// The team matrix (#36) against the AppHost, with no route mocked. Through the host's API, the editor publishes
// courses, the developer registers a pharmacy of which they are the only member, and assigns the courses to themself
// there; on the page the developer reads their row, filters by course and by job function, opens their sheet, and the
// matrix scrolls inside its own region on a tablet, with the names and the header pinned. Keycloak's login is English.

const matrixLabel = 'Team-Matrix: Mitglieder nach Schulungen';

/** Publishes a course per title and assigns each to the developer in the pharmacy, due on the day; answers the ids. */
async function assignCourses(page: Page, pharmacy: RegisteredPharmacy, titles: string[], dueDate: string) {
  const ids: string[] = [];
  for (const title of titles) {
    const courseId = await publishCourse(page.request, title);
    await assignThroughApi(page.request, pharmacy, courseId, pharmacy.developerPersonId, dueDate);
    ids.push(courseId);
  }
  return ids;
}

/** The matrix's scrolling region. */
const matrixOf = (page: Page) => page.getByRole('region', { name: matrixLabel });

/** The developer's row in the matrix, by its row header. */
const developerRow = (matrix: Locator) =>
  matrix.getByRole('row').filter({ has: matrix.page().getByRole('rowheader', { name: new RegExp(`^${developer}`) }) });

/** Axe's colour-contrast violations inside the element the selector names, as e2e/glass.spec.ts reads them. */
async function contrastViolations(page: Page, selector: string) {
  const results = await new AxeBuilder({ page }).include(selector).withRules(['color-contrast']).analyze();
  return results.violations;
}

test('the developer reads their row in the team matrix, filters it, opens their sheet, in both themes', async ({
  page,
}) => {
  // Without motion the sheet opens and closes at once, and axe never reads anything halfway through an animation.
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const first = courseTitle('Matrix');
  const second = courseTitle('Matrix');
  const name = pharmacyName('Matrix Apotheke');
  const due = daysAhead(30);
  const pharmacy = await registerPharmacy(page.request, name);
  const [firstId] = await assignCourses(page, pharmacy, [first, second], due);

  await signInTo(page, name);
  await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('link', { name: 'Team-Matrix' }).click();
  await expect(page).toHaveURL(/\/compliance$/);
  await expect(page.getByRole('heading', { name: 'Team-Matrix', level: 1 })).toBeVisible();

  // The rate over every assignment: both are open, so both are on track.
  const rate = page.getByRole('region', { name: 'Compliance-Quote' });
  await expect(rate.getByText(/^100\s%$/)).toBeVisible();
  await expect(rate.getByText('Fristgerecht: 2 von 2', { exact: true })).toBeVisible();
  await expect(rate.getByText('Bezogen auf: Alle Schulungen; Alle Tätigkeiten', { exact: true })).toBeVisible();

  // A column per course, by title, with its rate; the developer's cell says its status with an icon and a word.
  const matrix = matrixOf(page);
  const firstColumn = matrix.getByRole('columnheader').filter({ hasText: first });
  await expect(firstColumn).toContainText(/Quote 100\s%/);
  await expect(matrix.getByRole('columnheader')).toHaveCount(3);
  const row = developerRow(matrix);
  const cells = row.getByRole('cell');
  await expect(cells).toHaveCount(2);
  for (const cell of await cells.all()) {
    await expect(cell).toContainText('Offen');
    await expect(cell).toContainText(`fällig ${germanDate(due)}`);
    await expect(cell.locator('svg[aria-hidden="true"]')).toBeVisible();
  }

  // The job function narrows the rows on the host: nobody here is a PTA.
  const filters = page.getByRole('group', { name: 'Filter' });
  await filters.getByLabel('Tätigkeit').selectOption({ label: 'PTA (pharmazeutisch-technischer Assistent)' });
  await expect(page).toHaveURL(/[?&]jobFunction=Pta(&|$)/);
  await expect(
    page.getByText('Niemand in dieser Apotheke hat die Tätigkeit PTA (pharmazeutisch-technischer Assistent).'),
  ).toBeVisible();
  await expect(developerRow(matrix)).toHaveCount(0);
  await filters.getByRole('button', { name: 'Filter zurücksetzen' }).click();
  await expect(page).toHaveURL(/\/compliance$/);
  await expect(filters.getByLabel('Tätigkeit')).toBeFocused();
  await expect(row).toBeVisible();

  // The course is the page's own filter: one column remains, and the rate is that course's.
  await filters.getByLabel('Schulung', { exact: true }).selectOption({ label: first });
  await expect(page).toHaveURL(new RegExp(`[?&]course=${firstId ?? ''}(&|$)`));
  await expect(matrix.getByRole('columnheader')).toHaveCount(2);
  await expect(firstColumn).toBeVisible();
  await expect(matrix.getByRole('columnheader').filter({ hasText: second })).toHaveCount(0);
  await expect(rate.getByText('Fristgerecht: 1 von 1', { exact: true })).toBeVisible();
  await expect(rate.getByText(`Bezogen auf: ${first}; Alle Tätigkeiten`, { exact: true })).toBeVisible();
  await expect(rate.getByText(/^100\s%$/)).toBeVisible();

  // The name opens the person's sheet with every course assigned to them, whatever the filter; Escape closes it and
  // gives the focus back to the name.
  const opener = row.getByRole('button', { name: `${developer}, Schulungsstand anzeigen` });
  await opener.click();
  const sheet = page.getByRole('dialog', { name: developer });
  await expect(sheet).toBeVisible();
  await expect(sheet.getByText('Apotheker · 2 Schulungen zugewiesen', { exact: true })).toBeVisible();
  for (const title of [first, second]) {
    const section = sheet.locator('section').filter({ has: page.getByRole('heading', { name: title, level: 3 }) });
    await expect(section.getByText('Offen', { exact: true })).toBeVisible();
    await expect(section.getByRole('definition').first()).toHaveText(germanDate(due));
  }
  await page.keyboard.press('Escape');
  await expect(sheet).toBeHidden();
  await expect(opener).toBeFocused();

  // Axe's colour contrast on the rate and the matrix, in the light theme and then in the dark one.
  const rateSelector = `section[aria-labelledby="${(await rate.getAttribute('aria-labelledby')) ?? ''}"]`;
  const matrixSelector = `[role="region"][aria-label="${matrixLabel}"]`;
  await expect(page.locator('html')).not.toHaveClass(/\bdark\b/);
  expect(await contrastViolations(page, rateSelector)).toEqual([]);
  expect(await contrastViolations(page, matrixSelector)).toEqual([]);

  await page.evaluate(() => {
    localStorage.setItem('theme', 'dark');
  });
  await page.reload();
  await expect(page.locator('html')).toHaveClass(/\bdark\b/);
  await expect(row.getByRole('cell').first()).toContainText('Offen');
  const darkRateSelector = `section[aria-labelledby="${(await rate.getAttribute('aria-labelledby')) ?? ''}"]`;
  expect(await contrastViolations(page, darkRateSelector)).toEqual([]);
  expect(await contrastViolations(page, matrixSelector)).toEqual([]);
});

test.describe('on a tablet held upright', () => {
  // The desktop sidebar's breakpoint (docs/domain/README.md, "Geräte"): the content is about 480 px wide.
  test.use({ viewport: { width: 768, height: 1024 } });

  test('the matrix scrolls inside its region, with the names and the header pinned', async ({ page }) => {
    const titles = [courseTitle('Matrix Tablet'), courseTitle('Matrix Tablet'), courseTitle('Matrix Tablet')];
    const name = pharmacyName('Matrix Tablet');
    const pharmacy = await registerPharmacy(page.request, name);
    await assignCourses(page, pharmacy, titles, daysAhead(30));

    await signInTo(page, name);
    await page.goto('/compliance');
    await expect(page.getByRole('heading', { name: 'Team-Matrix', level: 1 })).toBeVisible();

    const matrix = matrixOf(page);
    const cells = developerRow(matrix).getByRole('cell');
    await expect(cells).toHaveCount(3);
    for (const cell of await cells.all()) {
      await expect(cell).toContainText('Offen');
    }

    // The page never scrolls sideways; the region does.
    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(
      false,
    );
    expect(await matrix.evaluate((region) => region.scrollWidth > region.clientWidth)).toBe(true);

    // Scrolled fully right, the member column (its header and the developer's name) is still inside the region.
    const nameHeader = matrix.getByRole('rowheader', { name: new RegExp(`^${developer}`) });
    const corner = matrix.getByRole('columnheader', { name: 'Mitglied' });
    const before = await nameHeader.boundingBox();
    await matrix.evaluate((region) => {
      region.scrollLeft = region.scrollWidth;
    });
    await expect.poll(() => matrix.evaluate((region) => region.scrollLeft)).toBeGreaterThan(0);
    const box = await matrix.boundingBox();
    expect(box).not.toBeNull();
    for (const pinned of [nameHeader, corner]) {
      const shown = await pinned.boundingBox();
      expect(shown).not.toBeNull();
      if (box !== null && shown !== null) {
        expect(shown.x).toBeGreaterThanOrEqual(box.x);
        expect(shown.x + shown.width).toBeLessThanOrEqual(box.x + box.width);
      }
    }
    expect((await nameHeader.boundingBox())?.x).toBe(before?.x);

    // Scrolled fully down as well, the header row (the corner and the last course's header) is still inside the
    // region, at its top.
    await matrix.evaluate((region) => {
      region.scrollTop = region.scrollHeight;
    });
    const lastCourse = matrix.getByRole('columnheader').last();
    await expect(lastCourse).toBeInViewport();
    for (const pinned of [corner, lastCourse]) {
      const shown = await pinned.boundingBox();
      expect(shown).not.toBeNull();
      if (box !== null && shown !== null) {
        expect(shown.y).toBeGreaterThanOrEqual(box.y);
        expect(shown.y + shown.height).toBeLessThanOrEqual(box.y + box.height);
      }
    }
  });
});

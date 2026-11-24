import AxeBuilder from '@axe-core/playwright';
import { expect, type Page, test } from '@playwright/test';
import {
  accessToken,
  assignThroughApi,
  courseTitle,
  daysAhead,
  germanDate,
  type MyAssignment,
  myAssignmentsThroughApi,
  pharmacyName,
  publishCourse,
  registerPharmacy,
  type RegisteredPharmacy,
  signInTo,
} from './compliance';
import { setDueDate } from './complianceDatabase';

// My courses (#38) against the AppHost, with no route mocked. Through the host's API, the editor publishes courses,
// the developer registers a pharmacy of which they are the only member and assigns the courses to themself there; on
// /learning the developer reads them in the host's order, each with Start or Continue leading to the chapter player's
// address. The player itself is #44's. An overdue course is made by moving a stored due date into the past
// (e2e/complianceDatabase.ts), since the API refuses one. Keycloak's login is English.

const listHeading = 'Zugewiesene Schulungen';

/** Publishes a course per title and assigns each to the developer in a new pharmacy, due on its day. */
async function assignedCourses(page: Page, label: string, courses: { title: string; dueDate: string }[]) {
  const name = pharmacyName(label);
  const pharmacy = await registerPharmacy(page.request, name);
  for (const course of courses) {
    const courseId = await publishCourse(page.request, course.title);
    await assignThroughApi(page.request, pharmacy, courseId, pharmacy.developerPersonId, course.dueDate);
  }
  return { name, pharmacy };
}

/** The developer's assignment of the course in the pharmacy, as Compliance answers it. */
async function assignmentOf(page: Page, pharmacy: RegisteredPharmacy, title: string) {
  const found = (await myAssignmentsThroughApi(page.request, pharmacy)).find((item) => item.courseTitle === title);
  expect(found, `Compliance lists the developer's assignment of ${title}`).toBeDefined();
  return found as MyAssignment;
}

/** The version a course's action opens; every course these tests publish has one. */
function versionOf(assignment: MyAssignment) {
  expect(assignment.startCourseVersionId, `${assignment.courseTitle} has a version to start`).not.toBeNull();
  return assignment.startCourseVersionId ?? '';
}

/** The list of assigned courses on /learning. */
const listOf = (page: Page) => page.getByRole('region', { name: listHeading }).getByRole('list');

/** A course's item in the list, by its heading. */
const itemOf = (page: Page, title: string) =>
  listOf(page)
    .getByRole('listitem')
    .filter({ has: page.getByRole('heading', { name: title, level: 3 }) });

/** Axe's colour-contrast violations in the list's section, as e2e/glass.spec.ts reads them. */
async function contrastViolations(page: Page) {
  const section = page.getByRole('region', { name: listHeading });
  const selector = `section[aria-labelledby="${(await section.getAttribute('aria-labelledby')) ?? ''}"]`;
  const results = await new AxeBuilder({ page }).include(selector).withRules(['color-contrast']).analyze();
  return results.violations;
}

/** No colour-contrast violation in the list, in the light theme and then, after a reload, in the dark one. */
async function expectContrastInBothThemes(page: Page, firstTitle: string) {
  await expect(page.locator('html')).not.toHaveClass(/\bdark\b/);
  expect(await contrastViolations(page)).toEqual([]);

  await page.evaluate(() => {
    localStorage.setItem('theme', 'dark');
  });
  await page.reload();
  await expect(page.locator('html')).toHaveClass(/\bdark\b/);
  await expect(itemOf(page, firstTitle)).toBeVisible();
  expect(await contrastViolations(page)).toEqual([]);
}

test('the developer reads their courses by due date, each starting at its version, in both themes', async ({
  page,
}) => {
  const later = { title: courseTitle('Meine Schulungen'), dueDate: daysAhead(30) };
  const sooner = { title: courseTitle('Meine Schulungen'), dueDate: daysAhead(10) };
  const { name, pharmacy } = await assignedCourses(page, 'Meine Schulungen', [later, sooner]);
  const soonerVersion = versionOf(await assignmentOf(page, pharmacy, sooner.title));
  const laterVersion = versionOf(await assignmentOf(page, pharmacy, later.title));

  await signInTo(page, name);
  await page.getByRole('navigation', { name: 'Bereiche' }).getByRole('link', { name: 'Meine Schulungen' }).click();
  await expect(page).toHaveURL(/\/learning$/);
  await expect(page.getByRole('heading', { name: 'Meine Schulungen', level: 1 })).toBeVisible();

  // The host's order: the course due sooner first, though it was assigned second.
  await expect(listOf(page).getByRole('heading', { level: 3 })).toHaveText([sooner.title, later.title]);
  for (const [course, version] of [
    [sooner, soonerVersion],
    [later, laterVersion],
  ] as const) {
    const item = itemOf(page, course.title);
    await expect(item.getByText('Offen', { exact: true })).toBeVisible();
    await expect(item.getByText(`Fällig am ${germanDate(course.dueDate)}`, { exact: true })).toBeVisible();
    const start = item.getByRole('link', { name: `Starten: ${course.title}` });
    await expect(start).toHaveText('Starten');
    await expect(start).toHaveAttribute('href', `/learning/courses/${version}`);
  }

  await expectContrastInBothThemes(page, sooner.title);

  // Start leads to the player's address on that version; the player itself is #44's.
  await itemOf(page, sooner.title)
    .getByRole('link', { name: `Starten: ${sooner.title}` })
    .click();
  await expect(page).toHaveURL(new RegExp(`/learning/courses/${soonerVersion}$`));
});

test('a course whose session is running reads Continue', async ({ page }) => {
  const course = { title: courseTitle('Fortsetzen'), dueDate: daysAhead(10) };
  const { name, pharmacy } = await assignedCourses(page, 'Fortsetzen', [course]);
  const version = versionOf(await assignmentOf(page, pharmacy, course.title));

  // Learning learns of the version and of the membership through events: until it has both, it refuses the start.
  const authorization = await accessToken(page.request, 'developer');
  await expect
    .poll(
      async () => {
        const response = await page.request.post('/api/learning/sessions', {
          headers: { Authorization: authorization },
          data: { courseVersionId: version },
        });
        return response.ok() ? 'started' : `${response.status()}: ${await response.text()}`;
      },
      { message: `Learning starts a session on ${version}`, timeout: 30_000 },
    )
    .toBe('started');
  // Compliance learns of the start from LearningSessionStartedV1.
  await expect
    .poll(async () => (await assignmentOf(page, pharmacy, course.title)).continuesSession, {
      message: `Compliance knows the session on ${course.title} runs`,
      timeout: 30_000,
    })
    .toBe(true);

  await signInTo(page, name);
  await page.goto('/learning');
  const item = itemOf(page, course.title);
  await expect(item.getByText('In Bearbeitung', { exact: true })).toBeVisible();
  const resume = item.getByRole('link', { name: `Fortsetzen: ${course.title}` });
  await expect(resume).toHaveText('Fortsetzen');
  await expect(resume).toHaveAttribute('href', `/learning/courses/${version}`);
});

test('an overdue course comes first, highlighted, in both themes', async ({ page }) => {
  const sooner = { title: courseTitle('Überfällig'), dueDate: daysAhead(10) };
  const later = { title: courseTitle('Überfällig'), dueDate: daysAhead(30) };
  const { name, pharmacy } = await assignedCourses(page, 'Überfällig', [sooner, later]);

  // The course due last becomes overdue: Compliance derives the state from the due date on every read.
  const overdueOn = daysAhead(-3);
  setDueDate((await assignmentOf(page, pharmacy, later.title)).assignmentId, overdueOn);
  await expect
    .poll(async () => (await myAssignmentsThroughApi(page.request, pharmacy)).map((item) => item.state))
    .toEqual(['Overdue', 'Open']);

  await signInTo(page, name);
  await page.goto('/learning');
  await expect(listOf(page).getByRole('heading', { level: 3 })).toHaveText([later.title, sooner.title]);
  const overdue = itemOf(page, later.title);
  await expect(overdue.getByText('Überfällig', { exact: true })).toBeVisible();
  await expect(overdue.getByText(`War fällig am ${germanDate(overdueOn)}`, { exact: true })).toBeVisible();
  await expect(overdue.getByRole('link', { name: `Starten: ${later.title}` })).toBeVisible();
  const open = itemOf(page, sooner.title);
  await expect(open.getByText('Offen', { exact: true })).toBeVisible();
  await expect(open.getByText(`Fällig am ${germanDate(sooner.dueDate)}`, { exact: true })).toBeVisible();

  await expectContrastInBothThemes(page, later.title);
});

test('on a phone, the page never scrolls sideways, and Start stays in reach', async ({ page }) => {
  const course = { title: courseTitle('Meine Schulungen Telefon'), dueDate: daysAhead(10) };
  const { name } = await assignedCourses(page, 'Meine Schulungen Telefon', [course]);

  // Signed in on the desktop, where signInTo reads the sidebar (a phone folds it away), then read on a phone.
  await signInTo(page, name);
  await page.setViewportSize({ width: 375, height: 812 });
  await page.goto('/learning');
  await expect(page.getByRole('heading', { name: 'Meine Schulungen', level: 1 })).toBeVisible();
  const start = itemOf(page, course.title).getByRole('link', { name: `Starten: ${course.title}` });
  await expect(start).toBeVisible();
  await expect(start).toBeInViewport();
  expect(await page.evaluate(() => document.documentElement.clientWidth)).toBe(375);
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(
    false,
  );
});

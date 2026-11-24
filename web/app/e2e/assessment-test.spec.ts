import { type APIRequestContext, expect, type Page, test } from '@playwright/test';
import { accessToken, courseTitle, pharmacyName, publishCourse, registerPharmacy, signInTo } from './compliance';

// The learner's test (#48) against the AppHost, with no route mocked. Through the host's API, the developer registers a
// pharmacy and so becomes an active member (ADR 0017), and the realm's local user editor publishes a course of one
// short text chapter, "Einleitung", and one approved single-choice question ("30 Sekunden" is right, "5 Sekunden"
// wrong) at a pass mark of 80 %, with the default of 3 attempts. The developer then works through the chapter in the
// player, follows "Zum Test", and takes the test:
// - a failed attempt, an answer kept over a reload, then a passed attempt;
// - three failed attempts, after which the test is locked and the result links to the chapter to work through again.
// Until #47 the lock does not reopen the chapter in Learning; this spec asserts only the result page. Keycloak's login
// is English.

interface PublishedCourse {
  title: string;
  versionId: string;
  chapterId: string;
}

/** Publishes the course and reads its version (GET /catalog/courses) and its one chapter (the version's read). */
async function publishTestCourse(request: APIRequestContext, label: string): Promise<PublishedCourse> {
  const title = courseTitle(label);
  await publishCourse(request, title);
  const developer = await accessToken(request, 'developer');
  const catalog = (await (
    await request.get('/api/catalog/courses', {
      headers: { Authorization: developer },
      params: { page: 1, pageSize: 20 },
    })
  ).json()) as { items: { title: string; courseVersionId: string }[] };
  const versionId = catalog.items.find((course) => course.title === title)?.courseVersionId ?? '';
  expect(versionId, `the catalog names the version of ${title}`).not.toBe('');
  const version = (await (
    await request.get(`/api/catalog/course-versions/${versionId}`, { headers: { Authorization: developer } })
  ).json()) as { chapters: { chapterId: string; title: string }[] };
  const chapterId = version.chapters.find((chapter) => chapter.title === 'Einleitung')?.chapterId ?? '';
  expect(chapterId, 'the version has the chapter Einleitung').not.toBe('');
  return { title, versionId, chapterId };
}

/** Waits as the developer until Learning knows the version: the start answers 2xx (404 until it does). */
async function waitUntilLearnable(request: APIRequestContext, course: PublishedCourse) {
  const developer = await accessToken(request, 'developer');
  await expect
    .poll(
      async () =>
        (
          await request.post('/api/learning/sessions', {
            headers: { Authorization: developer },
            data: { courseVersionId: course.versionId },
          })
        ).ok(),
      { message: 'Learning knows the published version (the start answers 2xx)', timeout: 30_000 },
    )
    .toBe(true);
}

/** Works through the course in the player (its short text completes in view) and follows "Zum Test". */
async function completeContentAndOpenTest(page: Page, course: PublishedCourse) {
  await page.goto(`/learning/courses/${course.versionId}`);
  await expect(page.getByRole('heading', { level: 1, name: course.title })).toBeVisible();
  const toTest = page.getByRole('link', { name: 'Zum Test' });
  await expect(toTest).toBeVisible({ timeout: 30_000 });
  await toTest.click();
  await expect(page).toHaveURL(/\/assessment\/sessions\/[^/]+$/);
  // The examination opens a few seconds after the content is completed: the page asks again every 3 s until it is.
  await expect(page.getByRole('button', { name: 'Test beginnen' })).toBeVisible({ timeout: 30_000 });
}

const question = (page: Page) => page.getByRole('group', { name: /Frage 1 von 1/ });

/** Starts the attempt shown as "Versuch n von 3" and chooses the option, which is saved at once. */
async function startAndChoose(page: Page, attempt: number, option: string) {
  await expect(page.getByText(`Versuch ${attempt} von 3.`, { exact: false })).toBeVisible();
  await page.getByRole('button', { name: 'Test beginnen' }).click();
  await expect(page.getByRole('heading', { level: 2, name: 'Fragen' })).toBeFocused();
  await question(page).getByRole('radio', { name: option }).check();
  await expect(question(page).getByText('Gespeichert', { exact: true })).toBeVisible();
}

/** Submits through the confirmation and lands on the result. */
async function submit(page: Page) {
  await page.getByRole('button', { name: 'Test abgeben' }).click();
  const dialog = page.getByRole('dialog', { name: 'Test abgeben?' });
  await expect(dialog.getByRole('button', { name: 'Weiter bearbeiten' })).toBeFocused();
  await dialog.getByRole('button', { name: 'Abgeben', exact: true }).click();
  await expect(page).toHaveURL(/\/assessment\/sessions\/[^/]+\/result$/);
}

test('a learner fails the test, keeps an answer over a reload, and passes on the next attempt', async ({ page }) => {
  test.setTimeout(180_000);
  const pharmacy = pharmacyName('Test');
  await registerPharmacy(page.request, pharmacy);
  const course = await publishTestCourse(page.request, 'Test bestehen');
  await waitUntilLearnable(page.request, course);
  await signInTo(page, pharmacy);

  await completeContentAndOpenTest(page, course);

  // A wrong answer, saved, is still chosen after a reload: the server keeps it.
  await startAndChoose(page, 1, '5 Sekunden');
  await page.reload();
  await expect(question(page).getByRole('radio', { name: '5 Sekunden' })).toBeChecked();

  await submit(page);
  await expect(page.getByRole('heading', { level: 1, name: `Ergebnis: ${course.title}` })).toBeVisible();
  await expect(page.getByText('Nicht bestanden', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Einleitung' })).toHaveAttribute(
    'href',
    `/learning/courses/${course.versionId}?chapter=${course.chapterId}`,
  );
  await expect(page.getByText('Noch 2 Versuche', { exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Nächsten Versuch beginnen' }).click();
  await startAndChoose(page, 2, '30 Sekunden');
  await submit(page);
  await expect(page.getByText('Bestanden', { exact: true })).toBeVisible();
  await expect(page.getByText('Ihre Teilnahmebestätigung wird ausgestellt.')).toBeVisible();
});

test('after three failed attempts the test is locked and the result links to the chapter to work through again', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const pharmacy = pharmacyName('Sperre');
  await registerPharmacy(page.request, pharmacy);
  const course = await publishTestCourse(page.request, 'Test gesperrt');
  await waitUntilLearnable(page.request, course);
  await signInTo(page, pharmacy);

  await completeContentAndOpenTest(page, course);

  for (const attempt of [1, 2, 3]) {
    await startAndChoose(page, attempt, '5 Sekunden');
    await submit(page);
    await expect(page.getByText('Nicht bestanden', { exact: true })).toBeVisible();
    if (attempt < 3) {
      await page.getByRole('link', { name: 'Nächsten Versuch beginnen' }).click();
    }
  }

  await expect(
    page.getByText(
      'Sie haben alle Versuche aufgebraucht; der Test ist gesperrt. Bearbeiten Sie diese Kapitel erneut, dann wird er wieder freigeschaltet:',
    ),
  ).toBeVisible();
  const chapter = page.getByRole('link', { name: 'Einleitung' });
  await expect(chapter).toHaveAttribute('href', new RegExp(`\\?chapter=${course.chapterId}$`));
  await expect(page.getByRole('link', { name: 'Nächsten Versuch beginnen' })).toHaveCount(0);
});

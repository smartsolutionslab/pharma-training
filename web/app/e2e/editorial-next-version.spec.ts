import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

// A new version of a published course (#155), as the realm's local user `editor` (realm role editor) drafts it against
// the AppHost. The course is started in the inbox and published through the API (#18's workflow: a chapter, a
// blueprint of one question, the question approved, submitted, published), since the pages that publish are not built
// yet. The realm has one editor and four eyes is off by default (ADR 0007), so the editor publishes alone.
// Keycloak's login is English.

/** Signs in as editor and starts a course from the inbox; resolves to its first version's id and the editor's token. */
async function startCourse(page: Page, title: string) {
  const signedIn = page.waitForRequest(
    (request) => request.url().includes('/api/') && request.headers()['authorization'] !== undefined,
  );
  await page.goto('/');
  await page.getByLabel('Username or email').fill('editor');
  await page.getByLabel('Password', { exact: true }).fill('editor');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als editor')).toBeVisible();
  const authorization = (await signedIn).headers()['authorization'] ?? '';
  await page.goto('/editorial');
  await page.getByRole('button', { name: 'Neue Schulung' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Neue Schulung anlegen' });
  await dialog.getByRole('textbox', { name: 'Titel' }).fill(title);
  await dialog.getByRole('button', { name: 'Anlegen', exact: true }).click();
  await expect(page).toHaveURL(/\/editorial\/versions\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('heading', { name: title, level: 1 })).toBeVisible();
  const id = new URL(page.url()).pathname.split('/').at(-1) ?? '';
  return { id, authorization };
}

/** Calls the editorial API as the editor; fails the test on anything outside 2xx, naming the call. */
async function call(
  request: APIRequestContext,
  authorization: string,
  method: 'POST' | 'PUT',
  path: string,
  data?: unknown,
) {
  const response = await request.fetch(`/api/editorial/course-versions/${path}`, {
    method,
    headers: { Authorization: authorization },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}: ${await response.text()}`).toBe(true);
  return response;
}

/** Publishes the draft: a text chapter, a blueprint of one question, that question approved, submitted, published. */
async function publish(request: APIRequestContext, authorization: string, id: string) {
  const chapter = (await (
    await call(request, authorization, 'POST', `${id}/chapters`, {
      title: 'Einleitung',
      kind: 'Text',
      text: '# Willkommen\n\nDas ist **wichtig**.',
    })
  ).json()) as string;
  await call(request, authorization, 'PUT', `${id}/blueprint`, {
    questions: 1,
    passingScorePercent: 80,
    validityMonths: 12,
  });
  const question = (await (
    await call(request, authorization, 'POST', `${id}/questions`, {
      chapterId: chapter,
      text: 'Wie lange waschen Sie die Hände?',
      type: 'SingleChoice',
      options: [
        { text: '30 Sekunden', correct: true },
        { text: '5 Sekunden', correct: false },
      ],
    })
  ).json()) as string;
  await call(request, authorization, 'POST', `${id}/questions/${question}/approve`);
  await call(request, authorization, 'POST', `${id}/submit`);
  await call(request, authorization, 'POST', `${id}/publish`);
}

test('an editor starts a new version of a published course and edits it as a draft with the chapters carried over', async ({
  page,
}) => {
  const title = `E2E Neue Version ${Date.now()}`;
  const { id, authorization } = await startCourse(page, title);
  await publish(page.request, authorization, id);
  await page.reload();
  await expect(page.getByText('Veröffentlicht', { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Neue Version anlegen' }).click();

  await expect(page).toHaveURL(/\/editorial\/versions\/[0-9a-f-]{36}$/);
  await expect(page).not.toHaveURL(new RegExp(`${id}$`));
  await expect(page.getByRole('status').filter({ hasText: 'Version 2 wurde als Entwurf angelegt.' })).toBeVisible();
  await expect(page.getByRole('heading', { name: title, level: 1 })).toBeVisible();
  await expect(page.getByText('Version 2', { exact: true })).toBeVisible();
  await expect(page.getByText('Entwurf', { exact: true })).toBeVisible();
  const chapters = page.getByRole('list', { name: 'Kapitel' }).getByRole('listitem');
  await expect(chapters).toHaveCount(1);
  await expect(chapters.first()).toContainText('Einleitung');
  await expect(page.getByRole('button', { name: 'Kapitel hinzufügen' })).toBeEnabled();
  await expect(page.getByRole('button', { name: 'Neue Version anlegen' })).toHaveCount(0);
  await page.reload();
  await expect(page.getByText('Version 2', { exact: true })).toBeVisible();

  // The published version has its successor now: asked again, the host refuses, and the page says so and stays.
  await page.goto(`/editorial/versions/${id}`);
  await expect(page.getByText('Veröffentlicht', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Neue Version anlegen' }).click();
  await expect(page.getByRole('alert')).toBeVisible();
  await expect(page).toHaveURL(new RegExp(`/editorial/versions/${id}$`));
});

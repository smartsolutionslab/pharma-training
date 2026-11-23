import { type APIRequestContext, expect, type Page, test } from '@playwright/test';

// A version through the whole workflow on its page (#26), as the realm's local user `editor` (realm role editor)
// against the AppHost: the course is started in the inbox and given a chapter, a blueprint of one question and that
// question approved through the API; then, on the page, the checklist shows the submission open, the editor submits,
// returns it with a reason (the hint not to name people beside the field), submits again, publishes and retires it.
// The realm has one editor and four eyes is off by default (ADR 0007): the four-eyes refusal is covered by the page's
// component tests only. Keycloak's login is English.

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
  method: 'GET' | 'POST' | 'PUT',
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

/** Makes the draft ready to publish: a text chapter, a blueprint of one question, that question approved. */
async function prepare(request: APIRequestContext, authorization: string, id: string) {
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
}

test('an editor submits, returns with a reason, submits again, publishes and retires a version on its page', async ({
  page,
}) => {
  const title = `E2E Workflow ${Date.now()}`;
  const { id, authorization } = await startCourse(page, title);
  await prepare(page.request, authorization, id);
  await page.reload();

  const workflow = page.getByRole('region', { name: 'Prüfung und Veröffentlichung' });
  await expect(workflow).toBeVisible();
  const checklist = workflow.getByRole('list', { name: 'Checkliste zur Veröffentlichung' });
  await expect(checklist.getByRole('listitem')).toHaveCount(4);
  await expect(checklist.getByRole('listitem').filter({ hasText: 'Die Version hat Kapitel.' })).toContainText(
    'Erfüllt:',
  );
  await expect(checklist.getByRole('listitem').filter({ hasText: 'Der Test hat alle 1 Fragen.' })).toContainText(
    'Erfüllt:',
  );
  await expect(checklist.getByRole('listitem').filter({ hasText: 'Alle Fragen sind freigegeben.' })).toContainText(
    'Erfüllt:',
  );
  await expect(
    checklist.getByRole('listitem').filter({ hasText: 'Die Version muss noch zur Prüfung eingereicht werden.' }),
  ).toContainText('Offen:');

  // Submit: no confirmation.
  await workflow.getByRole('button', { name: 'Zur Prüfung einreichen' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Version 1 ist zur Prüfung eingereicht.' })).toBeVisible();
  await expect(page.getByText('In Prüfung', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: title, level: 1 })).toBeFocused();
  await expect(
    checklist.getByRole('listitem').filter({ hasText: 'Die Version ist zur Prüfung eingereicht.' }),
  ).toContainText('Erfüllt:');

  // Return, with a reason.
  const reason = 'Kapitel 1 braucht eine Quelle.';
  await workflow.getByRole('button', { name: 'Zur Überarbeitung zurückgeben' }).click();
  const returning = page.getByRole('dialog', { name: 'Version zurückgeben' });
  await expect(
    returning.getByText(
      'Bitte nennen Sie im Grund keine Personen, sondern beschreiben Sie, was an der Version zu ändern ist.',
    ),
  ).toBeVisible();
  await expect(returning.getByRole('textbox', { name: 'Grund' })).toBeFocused();
  await returning.getByRole('textbox', { name: 'Grund' }).fill(`  ${reason}  `);
  await returning.getByRole('button', { name: 'Zurückgeben', exact: true }).click();
  await expect(returning).toHaveCount(0);
  await expect(page.getByRole('status').filter({ hasText: 'Version 1 ist wieder ein Entwurf.' })).toBeVisible();
  await expect(page.getByText('Entwurf', { exact: true })).toBeVisible();
  const returns = (await (await call(page.request, authorization, 'GET', `${id}/returns`)).json()) as {
    items: { reason: string }[];
  };
  expect(returns.items.map((each) => each.reason)).toEqual([reason]);

  // Submit again, and publish after confirming.
  await workflow.getByRole('button', { name: 'Zur Prüfung einreichen' }).click();
  await expect(page.getByText('In Prüfung', { exact: true })).toBeVisible();
  await workflow.getByRole('button', { name: 'Veröffentlichen' }).click();
  const publishing = page.getByRole('dialog', { name: 'Version veröffentlichen?' });
  await publishing.getByRole('button', { name: 'Veröffentlichen' }).click();
  await expect(publishing).toHaveCount(0);
  await expect(page.getByRole('status').filter({ hasText: 'Version 1 ist veröffentlicht.' })).toBeVisible();
  await expect(page.getByText('Veröffentlicht', { exact: true })).toBeVisible();
  await expect(checklist).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Neue Version anlegen' })).toBeVisible();

  // Retire after confirming; the page read again still says so.
  await workflow.getByRole('button', { name: 'Zurückziehen' }).click();
  const retiring = page.getByRole('dialog', { name: 'Version zurückziehen?' });
  await retiring.getByRole('button', { name: 'Zurückziehen' }).click();
  await expect(retiring).toHaveCount(0);
  await expect(page.getByRole('status').filter({ hasText: 'Version 1 ist zurückgezogen.' })).toBeVisible();
  await page.reload();
  await expect(page.getByText('Zurückgezogen', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Zurückziehen' })).toHaveCount(0);
});

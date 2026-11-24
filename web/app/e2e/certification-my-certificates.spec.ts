import { type APIRequestContext, expect, type Page, test } from '@playwright/test';
import { accessToken, call, courseTitle, pharmacyName, publishCourse, registerPharmacy } from './compliance';

// The learner's certificates (#296, Z1) against the AppHost, with no route mocked. Through the host's API, the realm's
// local user editor publishes a course of one text chapter and one question (publishCourse), the developer becomes a
// member of a pharmacy of their own, works through the chapter and passes the test; Certification issues the
// certificate and renders its PDF. The developer then finds it on /certification and opens its PDF: the tab goes to
// the URL the host signs on storage, which serves the PDF inline (as #54 decided). Each run publishes a course of its own, whose certificate is the newest, so it is on the first
// page. Keycloak's login is English.

/** The answer publishCourse's one question takes. */
const rightAnswer = '30 Sekunden';

type Session = { id: string; chapters: { chapterId: string }[] };
type Examination = { id: string; status: string };
type Attempt = { questions: { id: string; options: { id: string; text: string }[] }[] };
type Certificates = { items: { certificateId: string; courseTitle: string; documentReady: boolean }[] };

/** The published version of the course, as the catalog answers it to the developer. */
async function versionOf(request: APIRequestContext, developer: string, title: string) {
  const response = await request.get('/api/catalog/courses', {
    headers: { Authorization: developer },
    params: { page: 1, pageSize: 20 },
  });
  expect(response.ok(), await response.text()).toBe(true);
  const page = (await response.json()) as { items: { courseVersionId: string; title: string }[] };
  const version = page.items.find((course) => course.title === title)?.courseVersionId;
  expect(version, `the catalog's first page lists ${title}`).toBeDefined();
  return version ?? '';
}

/**
 * The developer passes the course's test, through the host's API: starts a session once Learning knows the version,
 * completes every chapter, waits until Assessment unlocks the test, answers it right and submits.
 */
async function passCourse(request: APIRequestContext, title: string) {
  const developer = await accessToken(request, 'developer');
  const version = await versionOf(request, developer, title);

  let session: Session | undefined;
  await expect
    .poll(
      async () => {
        const response = await request.post('/api/learning/sessions', {
          headers: { Authorization: developer },
          data: { courseVersionId: version },
        });
        session = response.ok() ? ((await response.json()) as Session) : undefined;
        return response.ok() ? 'started' : `${response.status()}: ${await response.text()}`;
      },
      { message: 'Learning knows the published version (the start answers 2xx)', timeout: 30_000 },
    )
    .toBe('started');
  if (session === undefined) {
    throw new Error('The session did not start.');
  }
  for (const chapter of session.chapters) {
    await call(request, developer, 'POST', `learning/sessions/${session.id}/progress`, {
      chapterId: chapter.chapterId,
      percent: 100,
    });
  }

  let examination: Examination | undefined;
  await expect
    .poll(
      async () => {
        const response = await request.get(`/api/assessment/examinations/by-session/${session?.id ?? ''}`, {
          headers: { Authorization: developer },
        });
        examination = response.ok() ? ((await response.json()) as Examination) : undefined;
        return examination?.status ?? `${response.status()}`;
      },
      { message: 'Assessment unlocks the test once every chapter is done', timeout: 30_000 },
    )
    .toBe('Unlocked');
  const examinationId = examination?.id ?? '';

  const attempt = (await (
    await call(request, developer, 'POST', `assessment/examinations/${examinationId}/attempts`)
  ).json()) as Attempt;
  for (const question of attempt.questions) {
    const option = question.options.find((each) => each.text === rightAnswer);
    expect(option, `question ${question.id} offers ${rightAnswer}`).toBeDefined();
    await call(
      request,
      developer,
      'PUT',
      `assessment/examinations/${examinationId}/attempts/current/answers/${question.id}`,
      { optionIds: [option?.id ?? ''] },
    );
  }
  const result = (await (
    await call(request, developer, 'POST', `assessment/examinations/${examinationId}/attempts/current/submit`)
  ).json()) as { outcome: string };
  expect(result.outcome).toBe('Passed');
}

/** Waits until Certification lists the course's certificate to the developer with its PDF stored; answers its id. */
async function waitUntilDocumentReady(request: APIRequestContext, title: string) {
  const developer = await accessToken(request, 'developer');
  let certificateId = '';
  await expect
    .poll(
      async () => {
        const response = await request.get('/api/certification/me/certificates', {
          headers: { Authorization: developer },
          params: { page: 1, pageSize: 20 },
        });
        if (!response.ok()) {
          return `${response.status()}: ${await response.text()}`;
        }
        const page = (await response.json()) as Certificates;
        const certificate = page.items.find((each) => each.courseTitle === title);
        if (certificate === undefined) {
          return 'not issued';
        }
        certificateId = certificate.certificateId;
        return certificate.documentReady ? 'ready' : 'being prepared';
      },
      // Issuing waits on the broker and rendering on a local queue; a first render loads the fonts.
      { message: `the certificate for ${title} is issued and its PDF stored`, timeout: 90_000 },
    )
    .toBe('ready');
  return certificateId;
}

async function signInAsDeveloper(page: Page) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();
}

test('a learner opens the PDF of a certificate whose document is ready, in the same tab', async ({ page, request }) => {
  test.setTimeout(240_000);
  const title = courseTitle('Bestätigung');
  await registerPharmacy(request, pharmacyName('Bestätigungen'));
  await publishCourse(request, title);
  await passCourse(request, title);
  const certificateId = await waitUntilDocumentReady(request, title);

  await signInAsDeveloper(page);
  await page.goto('/certification');
  await expect(page.getByRole('heading', { level: 1, name: 'Meine Teilnahmebestätigungen' })).toBeVisible();
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: title, exact: true }) });
  await expect(row.getByRole('cell', { name: 'Gültig', exact: true })).toBeVisible();

  // The tab's own navigation to the stored document, certificates/{id}.pdf in Certification's container.
  const navigation = page.waitForRequest(
    (each) =>
      each.isNavigationRequest() &&
      each.frame() === page.mainFrame() &&
      new URL(each.url()).pathname.endsWith(`/certificates/${certificateId}.pdf`),
  );
  await row.getByRole('button', { name: `PDF öffnen für ${title}` }).click();
  const signed = (await navigation).url();

  // What the tab was sent to serves the PDF inline: read again from the same signed URL, valid for five minutes.
  const stored = await request.get(signed);
  expect(stored.status()).toBe(200);
  expect(stored.headers()['content-type']).toBe('application/pdf');
  expect(stored.headers()['content-disposition']).toMatch(/^inline/);
  expect((await stored.body()).subarray(0, 5).toString('ascii')).toBe('%PDF-');
});

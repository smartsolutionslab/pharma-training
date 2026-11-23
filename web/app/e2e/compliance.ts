import { type APIRequestContext, expect, type Page } from '@playwright/test';

// The setup the Compliance end-to-end tests share, all through the host's API with no route mocked: the realm's local
// user editor publishes a course, the developer registers a pharmacy they manage, of which they are the only member,
// and assigns courses there. Compliance learns of courses and members through integration events, so each step waits
// until Compliance (or the catalog) answers with what it made. Keycloak's login is English.

const keycloak = process.env.E2E_KEYCLOAK_URL ?? 'http://localhost:8080';

/** The developer's name as Compliance's member list answers it: the realm's first and last name. */
export const developer = 'Local Developer';

/** A number that shrinks as time goes on, padded so that it sorts as text: a newer run's name sorts first. */
function countdown() {
  return String(9_999_999_999_999 - Date.now()).padStart(13, '0');
}

/** A short random suffix, so two tests that start in the same millisecond (several workers) still differ. */
function suffix() {
  return String(Math.floor(Math.random() * 1e4)).padStart(4, '0');
}

/**
 * A course title the catalog (by title, 20 to a page) lists before every course an earlier run published: it starts
 * with a digit, and its number shrinks as time goes on, so the newest run's course is on the first page.
 */
export function courseTitle(label = 'Zuweisung') {
  return `0 E2E ${label} ${countdown()}${suffix()}`;
}

/** A pharmacy name no earlier run used, counting down like the course titles. */
export function pharmacyName(label: string) {
  return `0 E2E ${label} ${countdown()}${suffix()}`;
}

/** The access token Keycloak issues to a local user, whose password is their name (direct access grants are on). */
export async function accessToken(request: APIRequestContext, user: string) {
  const response = await request.post(`${keycloak}/realms/pharma-training/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'pharma-training-web', username: user, password: user },
  });
  expect(response.ok(), await response.text()).toBe(true);
  const body = (await response.json()) as { access_token: string };
  return `Bearer ${body.access_token}`;
}

/** Calls the host's API; fails the test on anything outside 2xx, naming the call. */
export async function call(
  request: APIRequestContext,
  authorization: string,
  method: 'POST' | 'PUT',
  path: string,
  data?: unknown,
) {
  const response = await request.fetch(`/api/${path}`, {
    method,
    headers: { Authorization: authorization },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}: ${await response.text()}`).toBe(true);
  return response;
}

/**
 * Publishes a course with the title as editor: a chapter, a blueprint of one question, that question approved, then
 * submitted and published; waits until the catalog's first page lists it for the developer, and answers its course id.
 */
export async function publishCourse(request: APIRequestContext, title: string) {
  const editor = await accessToken(request, 'editor');
  const version = (await (await call(request, editor, 'POST', 'editorial/courses', { title })).json()) as string;
  const versions = `editorial/course-versions/${version}`;
  const chapter = (await (
    await call(request, editor, 'POST', `${versions}/chapters`, { title: 'Einleitung', kind: 'Text', text: 'Hallo.' })
  ).json()) as string;
  await call(request, editor, 'PUT', `${versions}/blueprint`, {
    questions: 1,
    passingScorePercent: 80,
    validityMonths: 12,
  });
  const question = (await (
    await call(request, editor, 'POST', `${versions}/questions`, {
      chapterId: chapter,
      text: 'Wie lange waschen Sie die Hände?',
      type: 'SingleChoice',
      options: [
        { text: '30 Sekunden', correct: true },
        { text: '5 Sekunden', correct: false },
      ],
    })
  ).json()) as string;
  await call(request, editor, 'POST', `${versions}/questions/${question}/approve`);
  await call(request, editor, 'POST', `${versions}/submit`);
  await call(request, editor, 'POST', `${versions}/publish`);

  const reader = await accessToken(request, 'developer');
  let courseId = '';
  await expect
    .poll(
      async () => {
        const response = await request.get('/api/catalog/courses', {
          headers: { Authorization: reader },
          params: { page: 1, pageSize: 20 },
        });
        const page = (await response.json()) as { items: { courseId: string; title: string }[] };
        courseId = page.items.find((course) => course.title === title)?.courseId ?? '';
        return page.items.map((course) => course.title);
      },
      { message: `the catalog's first page lists ${title}`, timeout: 30_000 },
    )
    .toContain(title);
  return courseId;
}

/** A pharmacy the developer registered, and the developer's person id there, as Compliance answers it. */
export type RegisteredPharmacy = { id: string; developerPersonId: string };

/**
 * Registers a pharmacy with the name, which the developer manages, and waits until Compliance lists the developer as
 * its member (which also means it knows they may assign courses there).
 */
export async function registerPharmacy(request: APIRequestContext, name: string): Promise<RegisteredPharmacy> {
  const authorization = await accessToken(request, 'developer');
  const pharmacy = (await (
    await call(request, authorization, 'POST', 'organisation/pharmacies', { name, jobFunction: 'Pharmacist' })
  ).json()) as string;
  let developerPersonId = '';
  await expect
    .poll(
      async () => {
        const response = await request.get(`/api/compliance/pharmacies/${pharmacy}/members`, {
          headers: { Authorization: authorization },
        });
        if (!response.ok()) {
          return [`${response.status()}`];
        }
        const page = (await response.json()) as { items: { personId: string; name: string }[] };
        developerPersonId = page.items.find((member) => member.name === developer)?.personId ?? '';
        return page.items.map((member) => member.name);
      },
      { message: `Compliance lists the developer as a member of ${name}`, timeout: 30_000 },
    )
    .toEqual([developer]);
  return { id: pharmacy, developerPersonId };
}

/**
 * Assigns the course to the person in the pharmacy as the developer, due on the day (yyyy-mm-dd), once. Compliance
 * learns of a published course from an event, so until it holds its copy it answers 404 course-not-found: the
 * assignment is asked again until one person is assigned.
 */
export async function assignThroughApi(
  request: APIRequestContext,
  pharmacy: RegisteredPharmacy,
  courseId: string,
  personId: string,
  dueDate: string,
) {
  const authorization = await accessToken(request, 'developer');
  await expect
    .poll(
      async () => {
        const response = await request.post(`/api/compliance/pharmacies/${pharmacy.id}/assignments`, {
          headers: { Authorization: authorization },
          data: { courseId, personIds: [personId], dueDate, recurrenceMonths: null },
        });
        if (!response.ok()) {
          return `${response.status()}: ${await response.text()}`;
        }
        const body = (await response.json()) as { assigned: unknown[]; refused: unknown[] };
        return body.assigned.length === 1 ? 'assigned' : JSON.stringify(body);
      },
      { message: `the course ${courseId} is assigned to ${personId}`, timeout: 30_000 },
    )
    .toBe('assigned');
}

/**
 * Signs in as the developer and makes the pharmacy current in the shell: chosen in the switcher when the developer
 * belongs to several (earlier runs add some), or current by itself when it is their only one.
 */
export async function signInTo(page: Page, pharmacy: string) {
  await page.goto('/');
  await page.getByLabel('Username or email').fill('developer');
  await page.getByLabel('Password', { exact: true }).fill('developer');
  await page.getByRole('button', { name: 'Sign In' }).click();
  await expect(page.getByText('Angemeldet als developer')).toBeVisible();

  // Exactly: the start page has regions whose names hold the word too.
  const region = page.getByRole('region', { name: 'Apotheke', exact: true });
  const switcher = region.getByRole('button', { name: /^Apotheke( wählen$|: )/ });
  const name = region.getByText(pharmacy, { exact: true });
  await expect(switcher.or(name)).toBeVisible();
  if (await switcher.isVisible()) {
    await switcher.click();
    await page
      .getByRole('menu', { name: 'Apotheken' })
      .getByRole('menuitemradio', { name: new RegExp(`^${pharmacy}`) })
      .click();
    await expect(region.getByRole('button', { name: `Apotheke: ${pharmacy}` })).toBeVisible();
  }
  await expect(name).toBeVisible();
}

/** A day the given number of days from today, in the browser's (and the test's) local time, as yyyy-mm-dd. */
export function daysAhead(days: number) {
  const day = new Date();
  day.setDate(day.getDate() + days);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`;
}

/** A calendar date (yyyy-mm-dd) as the German app shows it: dd.mm.yyyy. */
export function germanDate(day: string) {
  const [year, month, date] = day.split('-');
  return `${date ?? ''}.${month ?? ''}.${year ?? ''}`;
}

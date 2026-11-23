import { type APIRequestContext, expect, type Locator, type Page, test } from '@playwright/test';

// Assigning a course (#37) against the AppHost, with no route mocked. Through the host's API, the realm's local user
// editor publishes a course and the developer registers a pharmacy they manage, of which they are the only member;
// Compliance learns of both through integration events, so the test waits until the catalog lists the course and
// Compliance lists the developer as a member. On the page, the developer chooses that pharmacy in the shell and assigns
// the course to themself; assigning it again names them as already assigned. Keycloak's login is English.

const keycloak = process.env.E2E_KEYCLOAK_URL ?? 'http://localhost:8080';

/** The developer's name as Compliance's member list answers it: the realm's first and last name. */
const developer = 'Local Developer';

/**
 * A course title the catalog (by title, 20 to a page) lists before every course an earlier run published: it starts
 * with a digit, and its number shrinks as time goes on, so the newest run's course is on the first page.
 */
function courseTitle() {
  const countdown = String(9_999_999_999_999 - Date.now()).padStart(13, '0');
  return `0 E2E Zuweisung ${countdown}`;
}

/**
 * A pharmacy name the shell's switcher (one page of 100 memberships, by name) lists before every pharmacy an earlier
 * run registered: it starts with a digit, and its number shrinks as time goes on, as the course title's does.
 */
function pharmacyName(kind: string) {
  const countdown = String(9_999_999_999_999 - Date.now()).padStart(13, '0');
  // The countdown before the kind: by name, the newest of every kind comes first.
  return `0 E2E Zuweisung ${countdown} ${kind}`;
}

/** The access token Keycloak issues to a local user, whose password is their name (direct access grants are on). */
async function accessToken(request: APIRequestContext, user: string) {
  const response = await request.post(`${keycloak}/realms/pharma-training/protocol/openid-connect/token`, {
    form: { grant_type: 'password', client_id: 'pharma-training-web', username: user, password: user },
  });
  expect(response.ok(), await response.text()).toBe(true);
  const body = (await response.json()) as { access_token: string };
  return `Bearer ${body.access_token}`;
}

/** Calls the host's API; fails the test on anything outside 2xx, naming the call. */
async function call(
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
 * submitted and published; waits until the catalog's first page lists it for the developer.
 */
async function publishCourse(request: APIRequestContext, title: string) {
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
  await expect
    .poll(
      async () => {
        const response = await request.get('/api/catalog/courses', {
          headers: { Authorization: reader },
          params: { page: 1, pageSize: 20 },
        });
        const page = (await response.json()) as { items: { title: string }[] };
        return page.items.map((course) => course.title);
      },
      { message: `the catalog's first page lists ${title}`, timeout: 30_000 },
    )
    .toContain(title);
}

/**
 * Registers a pharmacy with the name, which the developer manages, and waits until Compliance lists the developer as
 * its member (which also means it knows they may assign courses there).
 */
async function registerPharmacy(request: APIRequestContext, name: string) {
  const authorization = await accessToken(request, 'developer');
  const pharmacy = (await (
    await call(request, authorization, 'POST', 'organisation/pharmacies', { name, jobFunction: 'Pharmacist' })
  ).json()) as string;
  await expect
    .poll(
      async () => {
        const response = await request.get(`/api/compliance/pharmacies/${pharmacy}/members`, {
          headers: { Authorization: authorization },
        });
        if (!response.ok()) {
          return [`${response.status()}`];
        }
        const page = (await response.json()) as { items: { name: string }[] };
        return page.items.map((member) => member.name);
      },
      { message: `Compliance lists the developer as a member of ${name}`, timeout: 30_000 },
    )
    .toEqual([developer]);
}

/**
 * Signs in as the developer and makes the pharmacy current in the shell: chosen in the switcher when the developer
 * belongs to several (earlier runs add some), or current by itself when it is their only one.
 */
async function signInTo(page: Page, pharmacy: string) {
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
function daysAhead(days: number) {
  const day = new Date();
  day.setDate(day.getDate() + days);
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`;
}

/** The wizard's step, by its heading, which takes the focus on entering it. */
const stepRegion = (page: Page, title: string) => page.getByRole('region', { name: title });

/** Goes through the wizard: the course, the developer, the due date and maybe a yearly repetition; then Zuweisen. */
async function assign(page: Page, course: string, options: { yearly: boolean }): Promise<Locator> {
  const steps = page.getByRole('list', { name: 'Schritte' });
  const next = page.getByRole('button', { name: 'Weiter', exact: true });

  const courseStep = stepRegion(page, 'Schulung wählen');
  await expect(steps.locator('[aria-current="step"]')).toContainText('Schulung');
  const choose = courseStep.getByRole('button', { name: `${course} wählen` });
  await choose.click();
  await expect(choose).toHaveAttribute('aria-pressed', 'true');
  await next.click();

  const learners = stepRegion(page, 'Teilnehmende wählen');
  await expect(learners.getByRole('heading', { name: 'Teilnehmende wählen', level: 2 })).toBeFocused();
  await expect(steps.locator('[aria-current="step"]')).toContainText('Teilnehmende');
  await learners.getByRole('checkbox', { name: `${developer} auswählen` }).check();
  await expect(learners.getByRole('status')).toHaveText('Eine Person ausgewählt');
  await next.click();

  const due = stepRegion(page, 'Fälligkeit festlegen');
  await expect(due.getByRole('heading', { name: 'Fälligkeit festlegen', level: 2 })).toBeFocused();
  await due.getByLabel('Fällig am').fill(daysAhead(30));
  if (options.yearly) {
    await due.getByRole('checkbox', { name: 'Jährlich wiederholen' }).check();
  }
  await next.click();

  const review = stepRegion(page, 'Zuweisung prüfen');
  await expect(review.getByRole('heading', { name: 'Zuweisung prüfen', level: 2 })).toBeFocused();
  await expect(review.getByRole('definition')).toHaveText([
    course,
    developer,
    /\d{4}$/,
    options.yearly ? 'Jährlich' : 'Keine',
  ]);
  await review.getByRole('button', { name: 'Zuweisen', exact: true }).click();

  const result = stepRegion(page, 'Ergebnis');
  await expect(result.getByRole('heading', { name: 'Ergebnis', level: 2 })).toBeFocused();
  return result;
}

test('the developer assigns a published course to themself, and assigning it again names them as already assigned', async ({
  page,
}) => {
  const course = courseTitle();
  const pharmacy = pharmacyName('Apotheke');
  await publishCourse(page.request, course);
  await registerPharmacy(page.request, pharmacy);
  await signInTo(page, pharmacy);

  await page.goto('/compliance/assign');
  await expect(page.getByRole('heading', { name: 'Schulung zuweisen', level: 1 })).toBeVisible();

  const first = await assign(page, course, { yearly: false });
  await expect(first.getByText('Die Schulung wurde einer Person zugewiesen.', { exact: true })).toBeVisible();
  await expect(first.getByRole('list', { name: 'Nicht zugewiesen' })).toHaveCount(0);

  await first.getByRole('button', { name: 'Weitere Schulung zuweisen' }).click();
  await expect(page.getByRole('heading', { name: 'Schulung wählen', level: 2 })).toBeFocused();

  const again = await assign(page, course, { yearly: false });
  await expect(again.getByText('Die Schulung wurde niemandem zugewiesen.', { exact: true })).toBeVisible();
  await expect(again.getByRole('list', { name: 'Nicht zugewiesen' }).getByRole('listitem')).toHaveText([
    `${developer}: Diese Schulung ist bereits zugewiesen.`,
  ]);
});

test.describe('on a tablet held upright', () => {
  // The desktop sidebar's breakpoint (docs/domain/README.md, "Geräte").
  test.use({ viewport: { width: 768, height: 1024 } });

  test('the developer assigns a published course to themself, repeated yearly', async ({ page }) => {
    const course = courseTitle();
    const pharmacy = pharmacyName('Tablet');
    await publishCourse(page.request, course);
    await registerPharmacy(page.request, pharmacy);
    await signInTo(page, pharmacy);

    await page.goto('/compliance/assign');
    await expect(page.getByRole('heading', { name: 'Schulung zuweisen', level: 1 })).toBeVisible();

    const result = await assign(page, course, { yearly: true });
    await expect(result.getByText('Die Schulung wurde einer Person zugewiesen.', { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)).toBe(
      false,
    );
  });
});
